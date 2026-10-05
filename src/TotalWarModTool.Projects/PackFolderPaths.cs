namespace TotalWarModTool.Projects;

/// <summary>What <see cref="PackFolderPaths.Resolve"/> found out about a path below the pack folder.</summary>
public enum PackPathProblem
{
    /// <summary>The path stays inside the pack folder and passes through no link.</summary>
    None,

    /// <summary>The path is empty, rooted, has a '..' segment or resolves to a place outside the pack folder.</summary>
    UnsafePath,

    /// <summary>The pack folder, a folder on the way or the file itself is a link (junction or symbolic link).</summary>
    Link,
}

/// <summary>
/// The result of <see cref="PackFolderPaths.Resolve"/>. <paramref name="FullPath"/> (a full, normalised path: one spelling for the same file) is set when <paramref name="Problem"/> is None or Link.
/// <paramref name="LinkPath"/> (set for Link) is the first link as a path relative to the project folder, such as "pack\db\linked".
/// <paramref name="Detail"/> (set for UnsafePath) is a short English reason.
/// </summary>
public sealed record PackFolderPathCheck(PackPathProblem Problem, string? FullPath, string? LinkPath, string? Detail);

/// <summary>
/// The one place that decides whether a file below the pack folder of a project may be touched: the opener (read) and the writer
/// both use it. Looks at the disk for links but reads and writes no file.
/// </summary>
public static class PackFolderPaths
{
    /// <summary>
    /// Checks <paramref name="relativeFile"/> (relative to the pack folder, with '\' or '/') of the project in <paramref name="projectFolder"/>:
    /// it must be non-empty, not rooted, without a '..' segment and resolve to a place inside the pack folder (UnsafePath otherwise);
    /// then the pack folder, every folder on the way and the file itself must not be a link (Link otherwise, the first one is named).
    /// A part that does not exist is no link. Only argument errors are thrown.
    /// </summary>
    public static PackFolderPathCheck Resolve(string projectFolder, string relativeFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);

        if (relativeFile.Length == 0)
        {
            return Unsafe("The file path is empty.");
        }

        if (Path.IsPathRooted(relativeFile) || relativeFile.Split('\\', '/').Contains(".."))
        {
            return Unsafe("The file must be a relative path below the pack folder.");
        }

        var packFolder = Path.Combine(projectFolder, ProjectFile.PackFolder);
        var combined = Path.Combine(packFolder, relativeFile);
        string fullPath;

        // Second line of defence behind the segment check: whatever the path resolves to, it must lie inside the pack folder.
        try
        {
            var root = Path.GetFullPath(packFolder);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
            {
                root += Path.DirectorySeparatorChar;
            }

            fullPath = Path.GetFullPath(combined);
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return Unsafe("The file must be a relative path below the pack folder: " + relativeFile);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Unsafe("The file path cannot be resolved: " + ex.Message);
        }

        return LinkOnPath(packFolder, relativeFile) is { } link
            ? new PackFolderPathCheck(PackPathProblem.Link, fullPath, link, null)
            : new PackFolderPathCheck(PackPathProblem.None, fullPath, null, null);
    }

    private static PackFolderPathCheck Unsafe(string detail) => new(PackPathProblem.UnsafePath, null, null, detail);

    private static string? LinkOnPath(string packFolder, string relativeFile)
    {
        if (ProjectFiles.IsLink(packFolder))
        {
            return ProjectFile.PackFolder;
        }

        var path = packFolder;
        var walked = ProjectFile.PackFolder;
        foreach (var segment in relativeFile.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, segment);
            walked += "\\" + segment;
            if (ProjectFiles.IsLink(path))
            {
                return walked;
            }
        }

        return null;
    }
}
