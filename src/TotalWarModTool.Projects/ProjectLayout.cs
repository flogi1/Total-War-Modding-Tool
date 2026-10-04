using TotalWarModTool.Tables;

namespace TotalWarModTool.Projects;

/// <summary>How a pack entry is stored as a file of a project.</summary>
public enum ProjectFileKind
{
    /// <summary>A DB table in text form (<c>db/&lt;table&gt;/&lt;name&gt;.tsv</c>).</summary>
    Table,

    /// <summary>A Loc file in text form (<c>&lt;name&gt;.loc.tsv</c>).</summary>
    Loc,

    /// <summary>The bytes of the entry, unchanged.</summary>
    Raw,
}

/// <summary>Pure rules that map a pack path (with '/') to a file below the pack folder of a project and back.</summary>
public static class ProjectLayout
{
    private const int MaxSegmentLength = 255;
    private const string TextExtension = ".tsv";
    private const string LocTextExtension = ".loc.tsv";
    private const string InvalidSegmentChars = "<>:\"|?*\\";

    /// <summary>Null when the pack path can be a file below a project folder on Windows; otherwise a short English reason.</summary>
    public static string? UnsafePathReason(string packPath)
    {
        ArgumentNullException.ThrowIfNull(packPath);

        if (packPath.Length == 0)
        {
            return "The path is empty.";
        }

        if (!PackNameRules.IsWellFormed(packPath))
        {
            return "The path contains an unpaired surrogate character.";
        }

        foreach (var segment in packPath.Split('/'))
        {
            var reason = UnsafeSegmentReason(segment);
            if (reason is not null)
            {
                return reason;
            }
        }

        return null;
    }

    /// <summary>The relative file (with '\') for an entry stored as <paramref name="kind"/>: Table and Loc get ".tsv" appended, Raw keeps the path.</summary>
    public static string ToRelativeFile(string packPath, ProjectFileKind kind)
    {
        ArgumentNullException.ThrowIfNull(packPath);

        var file = packPath.Replace('/', '\\');
        return kind == ProjectFileKind.Raw ? file : file + TextExtension;
    }

    /// <summary>What a file below the pack folder is, and the pack path it becomes. <paramref name="relativeFile"/> uses '\' or '/'.</summary>
    public static (string PackPath, ProjectFileKind Kind) FromRelativeFile(string relativeFile)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);

        var path = relativeFile.Replace('\\', '/');
        var parts = path.Split('/');
        var name = parts[^1];

        if (name.EndsWith(TextExtension, StringComparison.OrdinalIgnoreCase) && DbTablePath.TryGetTableName(path[..^TextExtension.Length], out _))
        {
            return (path[..^TextExtension.Length], ProjectFileKind.Table);
        }

        if (name.Length > LocTextExtension.Length && name.EndsWith(LocTextExtension, StringComparison.OrdinalIgnoreCase))
        {
            return (path[..^TextExtension.Length], ProjectFileKind.Loc);
        }

        return (path, ProjectFileKind.Raw);
    }

    /// <summary>True when an entry with this pack path would be taken for a table or loc file in text form.</summary>
    public static bool LooksLikeTextForm(string packPath) => FromRelativeFile(packPath).Kind != ProjectFileKind.Raw;

    private static string? UnsafeSegmentReason(string segment)
    {
        if (segment.Length == 0)
        {
            return "The path has an empty segment.";
        }

        if (segment is "." or "..")
        {
            return "The path has a '.' or '..' segment.";
        }

        if (segment.Length > MaxSegmentLength)
        {
            return $"A path segment is longer than {MaxSegmentLength} characters.";
        }

        foreach (var c in segment)
        {
            if (c < ' ' || InvalidSegmentChars.Contains(c))
            {
                return "The path contains a character that is not allowed in a Windows file name.";
            }
        }

        if (segment.EndsWith(' ') || segment.EndsWith('.'))
        {
            return "A path segment ends with a space or a dot.";
        }

        if (PackNameRules.IsReservedDeviceName(segment))
        {
            return "A path segment is a reserved Windows device name.";
        }

        return null;
    }
}
