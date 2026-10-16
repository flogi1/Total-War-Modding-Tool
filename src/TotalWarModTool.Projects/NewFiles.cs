using System.Globalization;
using System.Text;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Projects;

/// <summary>Why a name cannot be the name part of a new table or loc file (<see cref="NewFiles.NameIssueOf"/>).</summary>
public enum NameIssue
{
    /// <summary>The name can be used.</summary>
    None,

    /// <summary>The name is empty.</summary>
    Empty,

    /// <summary>The name is longer than 100 characters.</summary>
    TooLong,

    /// <summary>The name contains '/' or '\'.</summary>
    Separator,

    /// <summary>The name contains a dot (the extension decides the kind of the file).</summary>
    Dot,

    /// <summary>The name starts or ends with a space.</summary>
    LeadingOrTrailingSpace,

    /// <summary>The name contains a tab or a line break.</summary>
    TabOrLineBreak,

    /// <summary>The name contains ';' (the separator of the meta line).</summary>
    Semicolon,

    /// <summary>The name contains an unpaired surrogate character.</summary>
    UnpairedSurrogate,

    /// <summary>The name contains an invisible control character (bidi control, zero-width character, C1 control).</summary>
    InvisibleCharacter,

    /// <summary>A project ignores files with this name.</summary>
    Ignored,

    /// <summary>The name contains a character that a Windows file name cannot hold.</summary>
    InvalidCharacter,

    /// <summary>The name is a reserved Windows device name (NUL, CON, COM1, ...).</summary>
    ReservedName,
}

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

    /// <summary>The longest name part of a new table or loc file, in characters.</summary>
    public const int MaxNameLength = 100;
    private const string LocExtension = ".loc";
    private const string DbFolder = "db";
    private const string TextExtension = ".tsv";
    private const string LocTextExtension = ".loc.tsv";

    // A path that has the same segment rules as the final file; only the name part differs.
    private const string ProbeTable = "x_tables";
    private const string IgnoredReason = "A project ignores files with this name.";

    /// <summary>The tables a new file can be created for: in the schema, with a current definition (see <see cref="TableVersionRule"/>) without unsupported field types. Sorted ordinally.</summary>
    public static IReadOnlyList<string> CreatableTables(GameSchema schema, GameTableVersions gameVersions)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(gameVersions);

        // A table is listed only when it also takes the longest allowed name, so that every allowed name works for every listed table.
        var longestName = new string('x', MaxNameLength);
        var result = new List<string>();
        foreach (var tableName in schema.Tables.Keys)
        {
            if (CheckTable(schema, gameVersions, tableName, longestName).Reason is null)
            {
                result.Add(tableName);
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>Null when the name can be the name part of a table or loc file; otherwise a short English reason (one per <see cref="NameIssue"/>).</summary>
    public static string? NameProblem(string name) => NameIssueOf(name) switch
    {
        NameIssue.None => null,
        NameIssue.Empty => "The name is empty.",
        NameIssue.TooLong => $"The name is longer than {MaxNameLength} characters.",
        NameIssue.Separator => "The name contains a slash or a backslash.",
        NameIssue.Dot => "The name contains a dot.",
        NameIssue.LeadingOrTrailingSpace => "The name starts or ends with a space.",
        NameIssue.TabOrLineBreak => "The name contains a tab or a line break.",
        NameIssue.Semicolon => "The name contains ';'.",
        NameIssue.UnpairedSurrogate => "The name contains an unpaired surrogate character.",
        NameIssue.InvisibleCharacter => "The name contains an invisible control character.",
        NameIssue.Ignored => IgnoredReason,
        NameIssue.InvalidCharacter => "The name contains a character that is not allowed in a Windows file name.",
        NameIssue.ReservedName => "The name is a reserved Windows device name.",
        _ => "The name cannot be used.",
    };

    /// <summary>The longest name part of a file that is neither a table nor a loc file, so that name and extension fit into one path segment.</summary>
    public static int MaxPlainNameLength(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return ProjectLayout.MaxSegmentLength - extension.Length;
    }

    /// <summary>
    /// Why <paramref name="name"/> cannot be the name part of a file that is neither a table nor a loc file and keeps
    /// <paramref name="extension"/> (with its dot, or empty), or <see cref="NameIssue.None"/>. A dot is fine here (the extension stays
    /// separate). The rules are the ones of a file name on Windows and of the project; the path as a whole (its length, links) is the job of
    /// the write boundary.
    /// </summary>
    public static NameIssue PlainNameIssueOf(string name, string extension)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(extension);

        if (name.Length == 0)
        {
            return NameIssue.Empty;
        }

        if (name.Contains('\\') || name.Contains('/'))
        {
            return NameIssue.Separator;
        }

        if (name[0] == ' ' || name[^1] == ' ')
        {
            return NameIssue.LeadingOrTrailingSpace;
        }

        if (!PackNameRules.IsWellFormed(name))
        {
            return NameIssue.UnpairedSurrogate;
        }

        if (name.Any(ProjectLayout.IsInvalidSegmentChar))
        {
            return NameIssue.InvalidCharacter;
        }

        if (name.Length > MaxPlainNameLength(extension))
        {
            return NameIssue.TooLong;
        }

        if (PackNameRules.IsReservedDeviceName(name + extension))
        {
            return NameIssue.ReservedName;
        }

        return IsIgnored(name + extension) ? NameIssue.Ignored : NameIssue.None;
    }

    /// <summary>
    /// Why the name cannot be the name part of a table or loc file, or <see cref="NameIssue.None"/>. The checks run in a fixed order and the
    /// first that fails decides; <see cref="NameProblem"/> is null exactly for <see cref="NameIssue.None"/>.
    /// </summary>
    public static NameIssue NameIssueOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0)
        {
            return NameIssue.Empty;
        }

        if (name.Length > MaxNameLength)
        {
            return NameIssue.TooLong;
        }

        if (name.Contains('\\') || name.Contains('/'))
        {
            return NameIssue.Separator;
        }

        // The extensions .tsv and .loc.tsv decide what kind of file it is, so the name has none of its own.
        if (name.Contains('.'))
        {
            return NameIssue.Dot;
        }

        if (name[0] == ' ' || name[^1] == ' ')
        {
            return NameIssue.LeadingOrTrailingSpace;
        }

        if (name.AsSpan().IndexOfAny('\t', '\n', '\r') >= 0)
        {
            return NameIssue.TabOrLineBreak;
        }

        // The meta line of the text file carries the pack path, and ';' separates its parts.
        if (name.Contains(';'))
        {
            return NameIssue.Semicolon;
        }

        if (!PackNameRules.IsWellFormed(name))
        {
            return NameIssue.UnpairedSurrogate;
        }

        // Invisible characters (bidi controls, zero-width characters, C1 controls) make names that look the same but are not.
        foreach (var rune in name.EnumerateRunes())
        {
            if (rune.Value is >= 0x80 and <= 0x9F || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
            {
                return NameIssue.InvisibleCharacter;
            }
        }

        if (IsIgnored(name + TextExtension) || IsIgnored(name + LocTextExtension))
        {
            return NameIssue.Ignored;
        }

        // What is left of the path rules for a name that passed the checks above: a character Windows does not allow, and a device name.
        if (ProjectLayout.UnsafePathReason(DbFolder + "/" + ProbeTable + "/" + name) is null)
        {
            return NameIssue.None;
        }

        return PackNameRules.IsReservedDeviceName(name) && !name.Any(ProjectLayout.IsInvalidSegmentChar)
            ? NameIssue.ReservedName
            : NameIssue.InvalidCharacter;
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

    /// <summary>An empty table in the version the game uses (the newest of the schema when the game lacks the table): db/&lt;tableName&gt;/&lt;name&gt; as text.</summary>
    public static NewFileResult Table(GameSchema schema, GameTableVersions gameVersions, string tableName, string name)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(gameVersions);
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

        var (text, reason) = CheckTable(schema, gameVersions, tableName, name);
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
    /// A table entry in another version than the game uses is refused; <paramref name="gameVersions"/> null means no check (only the snapshot reader, whose write plan checks).
    /// </summary>
    public static NewFileResult Adopt(string packPath, byte[] entryBytes, GameSchema? schema, GameTableVersions? gameVersions, string? newName)
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

        if (gameVersions is not null && DbTablePath.TryGetTableName(packPath, out var entryTable)
            && DbTableDecoder.ReadHeader(entryBytes.AsSpan(0, Math.Min(entryBytes.Length, DbTableDecoder.TableHeaderBytes))) is { } header
            && TableVersionRule.MismatchDetail(gameVersions, entryTable, header.Version) is { } mismatch)
        {
            return Refused(mismatch);
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

    // The text of an empty table in the current definition of the table, or the reason why no file can be created for it. The check writes
    // the table without rows under the given name and reads the text back, which is all that Table does, so that the list and Table
    // never disagree.
    private static (string? Text, string? Reason) CheckTable(GameSchema schema, GameTableVersions gameVersions, string tableName, string name)
    {
        var current = TableVersionRule.Current(schema, gameVersions, tableName);
        if (current.Kind == CurrentDefinitionKind.SchemaLacksGameVersion)
        {
            return (null, $"the schema does not know version {current.GameVersion?.ToString(CultureInfo.InvariantCulture)} of this table, which the game uses; choose a newer schema in the settings");
        }

        if (current.Definition is not { } newest)
        {
            return (null, "The table has no definition in the schema.");
        }

        foreach (var field in newest.Fields)
        {
            if (field.Type == FieldType.Unsupported)
            {
                return (null, "The current definition of the table has a field of an unsupported type.");
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
