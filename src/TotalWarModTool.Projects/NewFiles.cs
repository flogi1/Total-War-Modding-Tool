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

    // A path that has the same segment rules as the final file; only the name part differs.
    private const string ProbeTable = "x_tables";
    private const string ProbeName = "x";

    /// <summary>The tables a new file can be created for: in the schema, with a newest definition without unsupported field types. Sorted ordinally.</summary>
    public static IReadOnlyList<string> CreatableTables(GameSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var result = new List<string>();
        foreach (var (tableName, definitions) in schema.Tables)
        {
            if (CheckTable(tableName, definitions, ProbeName).Reason is null)
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

        return ProjectLayout.UnsafePathReason(DbFolder + "/" + ProbeTable + "/" + name);
    }

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

        var (definition, reason) = CheckTable(tableName, schema.Tables[tableName], name);
        if (definition is null)
        {
            return Refused(reason!);
        }

        var packPath = DbFolder + "/" + tableName + "/" + name;
        TsvWriteResult written;
        try
        {
            written = TableTsv.Write(tableName, definition.Version, packPath, definition, []);
        }
        catch (ArgumentException ex)
        {
            return Refused("The table cannot be written: " + ex.Message);
        }

        return written.Text is null
            ? Refused("The table cannot be written: " + written.Detail)
            : Created(packPath, ProjectFileKind.Table, written.Text);
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
        if (ProjectLayout.UnsafePathReason(packPath) is { } unsafePath)
        {
            return Refused(unsafePath);
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
        return new NewFileResult(new NewFile(ProjectLayout.ToRelativeFile(target, converted.Kind), converted.Bytes, converted.Kind, converted.Note), null);
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

    // The newest definition of a table, or the reason why no file can be created for it. The check writes a table without rows under the
    // given name, which is the same call Table makes, so that the list and Table never disagree.
    private static (TableDefinition? Definition, string? Reason) CheckTable(string tableName, IReadOnlyList<TableDefinition> definitions, string name)
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
        if (ProjectLayout.UnsafePathReason(packPath) is { } unsafePath)
        {
            return (null, unsafePath);
        }

        // The file must be found again as a table of this very name, or a build would take it for something else.
        var file = ProjectLayout.ToRelativeFile(packPath, ProjectFileKind.Table);
        if (!DbTablePath.TryGetTableName(packPath, out var folderTable)
            || !string.Equals(folderTable, tableName, StringComparison.Ordinal)
            || ProjectLayout.FromRelativeFile(file) != (packPath, ProjectFileKind.Table))
        {
            return (null, "The table name cannot be the name of a folder in a pack.");
        }

        try
        {
            var written = TableTsv.Write(tableName, newest.Version, packPath, newest, []);
            if (written.Text is null)
            {
                return (null, "The table cannot be written: " + written.Detail);
            }
        }
        catch (ArgumentException ex)
        {
            return (null, "The table cannot be written: " + ex.Message);
        }

        return (newest, null);
    }

    private static NewFileResult Created(string packPath, ProjectFileKind kind, string text) =>
        new(new NewFile(ProjectLayout.ToRelativeFile(packPath, kind), new UTF8Encoding(false).GetBytes(text), kind, null), null);

    private static NewFileResult Refused(string reason) => new(null, reason);
}
