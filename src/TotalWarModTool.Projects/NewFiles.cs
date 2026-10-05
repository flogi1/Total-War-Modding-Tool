using System.Globalization;
using System.Text;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Projects;

/// <summary>RelativeFile is below pack\ with '\'. Note says why a table or loc entry stayed binary (null otherwise).</summary>
public sealed record NewFile(string RelativeFile, byte[] Content, ProjectFileKind Kind, string? Note);

/// <summary>Exactly one of File and Reason is set. Reason is short technical English.</summary>
public sealed record NewFileResult(NewFile? File, string? Reason);

/// <summary>
/// Pure rules for the content and the place of a file that is new in a project: an empty table, an empty loc file, or a pack entry that
/// is adopted. Nothing is written here; the caller writes <see cref="NewFile.Content"/> to <see cref="NewFile.RelativeFile"/>.
/// </summary>
public static class NewFiles
{
    /// <summary>The folder of loc files inside a pack.</summary>
    public const string LocFolder = "text/db";

    private const int MaxNameLength = 100;
    private const string LocExtension = ".loc";
    private const string DbFolder = "db";
    private const string TextExtension = ".tsv";
    private const string LocTextExtension = ".loc.tsv";

    // A path that has the same segment rules as the final file; only the name part differs.
    private const string ProbeTable = "x_tables";
    private const string IgnoredReason = "A project ignores files with this name.";

    /// <summary>The tables a new file can be created for: in the schema, with a newest definition without unsupported field types. Sorted ordinally.</summary>
    public static IReadOnlyList<string> CreatableTables(GameSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        // A table is listed only when it also takes the longest allowed name, so that every allowed name works for every listed table.
        var longestName = new string('x', MaxNameLength);
        var result = new List<string>();
        foreach (var (tableName, definitions) in schema.Tables)
        {
            if (CheckTable(schema, tableName, definitions, longestName).Reason is null)
            {
                result.Add(tableName);
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>Null when the name can be the name part of a table or loc file; otherwise a short English reason.</summary>
    public static string? NameProblem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0)
        {
            return "The name is empty.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"The name is longer than {MaxNameLength} characters.";
        }

        if (name.Contains('\\') || name.Contains('/'))
        {
            return "The name contains a slash or a backslash.";
        }

        // The extensions .tsv and .loc.tsv decide what kind of file it is, so the name has none of its own.
        if (name.Contains('.'))
        {
            return "The name contains a dot.";
        }

        if (name[0] == ' ' || name[^1] == ' ')
        {
            return "The name starts or ends with a space.";
        }

        if (name.AsSpan().IndexOfAny('\t', '\n', '\r') >= 0)
        {
            return "The name contains a tab or a line break.";
        }

        // The meta line of the text file carries the pack path, and ';' separates its parts.
        if (name.Contains(';'))
        {
            return "The name contains ';'.";
        }

        if (!PackNameRules.IsWellFormed(name))
        {
            return "The name contains an unpaired surrogate character.";
        }

        // Invisible characters (bidi controls, zero-width characters, C1 controls) make names that look the same but are not.
        foreach (var rune in name.EnumerateRunes())
        {
            if (rune.Value is >= 0x80 and <= 0x9F || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
            {
                return "The name contains an invisible control character.";
            }
        }

        if (IsIgnored(name + TextExtension) || IsIgnored(name + LocTextExtension))
        {
            return IgnoredReason;
        }

        return ProjectLayout.UnsafePathReason(DbFolder + "/" + ProbeTable + "/" + name);
    }

    // The problem of the final file below the pack folder (relative, with '\'): what ResolveForWrite would refuse, or a file name that a
    // project leaves out of the pack.
    private static string? FileProblem(string relativeFile)
    {
        if (ProjectLayout.UnsafePathReason(relativeFile.Replace('\\', '/')) is { } unsafePath)
        {
            return unsafePath;
        }

        return IsIgnored(relativeFile[(relativeFile.LastIndexOf('\\') + 1)..]) ? IgnoredReason : null;
    }

    private static bool IsIgnored(string fileName) => ProjectFiles.IsIgnored(fileName, default);

    /// <summary>An empty table in the newest version of the schema: db/&lt;tableName&gt;/&lt;name&gt; as text.</summary>
    public static NewFileResult Table(GameSchema schema, string tableName, string name)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(name);

        if (NameProblem(name) is { } nameProblem)
        {
            return Refused(nameProblem);
        }

        // The schema looks tables up ignoring case; here the spelling of the key counts.
        if (!schema.Tables.Keys.Any(key => string.Equals(key, tableName, StringComparison.Ordinal)))
        {
            return Refused("The table is not in the schema.");
        }

        var (text, reason) = CheckTable(schema, tableName, schema.Tables[tableName], name);
        return text is null
            ? Refused(reason!)
            : Created(DbFolder + "/" + tableName + "/" + name, ProjectFileKind.Table, text);
    }

    /// <summary>An empty loc file: text/db/&lt;name&gt;.loc as text.</summary>
    public static NewFileResult Loc(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (NameProblem(name) is { } nameProblem)
        {
            return Refused(nameProblem);
        }

        var packPath = LocFolder + "/" + name + LocExtension;
        if (FileProblem(ProjectLayout.ToRelativeFile(packPath, ProjectFileKind.Loc)) is { } fileProblem)
        {
            return Refused(fileProblem);
        }

        TsvWriteResult written;
        try
        {
            written = TableTsv.WriteLoc(packPath, []);
        }
        catch (ArgumentException ex)
        {
            return Refused("The loc file cannot be written: " + ex.Message);
        }

        return written.Text is null
            ? Refused("The loc file cannot be written: " + written.Detail)
            : Created(packPath, ProjectFileKind.Loc, written.Text);
    }

    /// <summary>
    /// The file an entry of a pack becomes in a project. For a table (db/&lt;table&gt;/&lt;x&gt;) and a loc file (&lt;folder&gt;/&lt;x&gt;.loc), newName replaces
    /// the name part (null keeps it); for every other entry newName must be null and the path stays. The content follows <see cref="EntryConversion.ToProjectFile"/>.
    /// </summary>
    public static NewFileResult Adopt(string packPath, byte[] entryBytes, GameSchema? schema, string? newName)
    {
        ArgumentNullException.ThrowIfNull(packPath);
        ArgumentNullException.ThrowIfNull(entryBytes);

        var parts = Split(packPath);
        var target = packPath;
        if (newName is not null)
        {
            if (!parts.Renamable)
            {
                return Refused("This entry keeps its name.");
            }

            if (NameProblem(newName) is { } nameProblem)
            {
                return Refused(nameProblem);
            }

            target = parts.Prefix + newName + parts.Suffix;
        }

        if (ProjectLayout.UnsafePathReason(target) is { } unsafePath)
        {
            return Refused(unsafePath);
        }

        if (ProjectLayout.LooksLikeTextForm(target))
        {
            return Refused("The name collides with the text form of a table or loc file.");
        }

        var converted = EntryConversion.ToProjectFile(target, entryBytes, schema);

        // The file on disk can be longer than the pack path (text form) and can have a name the project ignores.
        var relativeFile = ProjectLayout.ToRelativeFile(target, converted.Kind);
        if (FileProblem(relativeFile) is { } fileProblem)
        {
            return Refused(fileProblem);
        }

        return new NewFileResult(new NewFile(relativeFile, converted.Bytes, converted.Kind, converted.Note), null);
    }

    /// <summary>What Adopt needs to know before the entry is read: whether the entry gets a new name, and the name it has now.</summary>
    public static (bool Renamable, string CurrentName) AdoptName(string packPath)
    {
        ArgumentNullException.ThrowIfNull(packPath);

        var parts = Split(packPath);
        return (parts.Renamable, parts.Name);
    }

    // The name part of a pack path and what lies around it: Prefix + Name + Suffix is the path. Only a table and a loc file are renamable.
    private static (bool Renamable, string Prefix, string Name, string Suffix) Split(string packPath)
    {
        var slash = packPath.LastIndexOf('/');
        var prefix = packPath[..(slash + 1)];
        var file = packPath[(slash + 1)..];

        if (DbTablePath.TryGetTableName(packPath, out _))
        {
            return (true, prefix, file, string.Empty);
        }

        if (file.EndsWith(LocExtension, StringComparison.OrdinalIgnoreCase))
        {
            return (true, prefix, file[..^LocExtension.Length], file[^LocExtension.Length..]);
        }

        return (false, string.Empty, file, string.Empty);
    }

    // The text of an empty table in the newest definition of the table, or the reason why no file can be created for it. The check writes
    // the table without rows under the given name and reads the text back, which is all that Table does, so that the list and Table
    // never disagree.
    private static (string? Text, string? Reason) CheckTable(GameSchema schema, string tableName, IReadOnlyList<TableDefinition> definitions, string name)
    {
        TableDefinition? newest = null;
        foreach (var definition in definitions)
        {
            if (newest is null || definition.Version > newest.Version)
            {
                newest = definition;
            }
        }

        if (newest is null)
        {
            return (null, "The table has no definition in the schema.");
        }

        foreach (var field in newest.Fields)
        {
            if (field.Type == FieldType.Unsupported)
            {
                return (null, "The newest definition of the table has a field of an unsupported type.");
            }
        }

        var packPath = DbFolder + "/" + tableName + "/" + name;
        var file = ProjectLayout.ToRelativeFile(packPath, ProjectFileKind.Table);
        if (FileProblem(file) is { } fileProblem)
        {
            return (null, fileProblem);
        }

        // The file must be found again as a table of this very name, or a build would take it for something else.
        if (!DbTablePath.TryGetTableName(packPath, out var folderTable)
            || !string.Equals(folderTable, tableName, StringComparison.Ordinal)
            || ProjectLayout.FromRelativeFile(file) != (packPath, ProjectFileKind.Table))
        {
            return (null, "The table name cannot be the name of a folder in a pack.");
        }

        string text;
        try
        {
            var written = TableTsv.Write(tableName, newest.Version, packPath, newest, []);
            if (written.Text is null)
            {
                return (null, "The table cannot be written: " + written.Detail);
            }

            text = written.Text;
        }
        catch (ArgumentException ex)
        {
            return (null, "The table cannot be written: " + ex.Message);
        }

        // The empty table must come back as this definition without rows.
        var read = TableTsv.Read(text, schema);
        return read.Table is { Rows.Count: 0 } table && ReferenceEquals(table.Definition, newest)
            ? (text, null)
            : (null, "The empty table cannot be read back.");
    }

    private static NewFileResult Created(string packPath, ProjectFileKind kind, string text) =>
        new(new NewFile(ProjectLayout.ToRelativeFile(packPath, kind), new UTF8Encoding(false).GetBytes(text), kind, null), null);

    private static NewFileResult Refused(string reason) => new(null, reason);
}
