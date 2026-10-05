namespace TotalWarModTool.Projects;

/// <summary>What <see cref="PackFolderPaths"/> found out about a path below the pack folder.</summary>
public enum PackPathProblem
{
    /// <summary>The path stays inside the pack folder and passes through no link.</summary>
    None,

    /// <summary>The path is empty, rooted, has a '..' segment or resolves to a place outside the pack folder (or, for writes, is not a plain Windows file path).</summary>
    UnsafePath,

    /// <summary>
    /// The pack folder, a folder on the way or the file itself is a link (junction or symbolic link). For writes also: a part of the way
    /// whose attributes cannot be read, which is not taken for "no link".
    /// </summary>
    Link,
}

/// <summary>
/// The result of <see cref="PackFolderPaths.Resolve"/> and <see cref="PackFolderPaths.ResolveForWrite"/>.
/// <paramref name="FullPath"/> (a full, normalised path: one spelling for the same file) is set when <paramref name="Problem"/> is None or Link.
/// <paramref name="LinkPath"/> (set for Link) is the first link as a path relative to the project folder, such as "pack\db\linked".
/// <paramref name="Detail"/> (set for UnsafePath, and for a Link that is only suspected because it could not be checked) is a short English reason.
/// </summary>
public sealed record PackFolderPathCheck(PackPathProblem Problem, string? FullPath, string? LinkPath, string? Detail);

/// <summary>
/// The path and link rules for a file below the pack folder of a project. <see cref="Resolve"/> is the check for reading (the opener);
/// <see cref="ResolveForWrite"/> is the stricter check for every write and is the one place for those rules.
/// Looks at the disk for links but reads and writes no file.
/// </summary>
public static class PackFolderPaths
{
    /// <summary>
    /// The check for reading. <paramref name="relativeFile"/> (relative to the pack folder, with '\' or '/') of the project in
    /// <paramref name="projectFolder"/> must be non-empty, not rooted, without a '..' segment and resolve to a place inside the pack folder
    /// (UnsafePath otherwise); then the pack folder, every folder on the way and the file itself must not be a link (Link otherwise, the
    /// first one is named). A part that does not exist or cannot be read is no link here: the caller's own read reports it.
    /// A write must use <see cref="ResolveForWrite"/>. Only argument errors are thrown.
    /// </summary>
    public static PackFolderPathCheck Resolve(string projectFolder, string relativeFile) => Check(projectFolder, relativeFile, strict: false);

    /// <summary>
    /// The check for writing: everything <see cref="Resolve"/> does, and in addition <see cref="ProjectLayout.UnsafePathReason"/> (alternate
    /// data streams, device names, a trailing dot or space, too long a path, empty segments), a path that resolves to the pack folder
    /// itself is refused, and a part of the way whose attributes cannot be read (access denied, other IO error) is refused as a Link with
    /// its own detail instead of being taken for "no link". A part that does not exist is no link. Only argument errors are thrown.
    /// </summary>
    public static PackFolderPathCheck ResolveForWrite(string projectFolder, string relativeFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);

        var reason = ProjectLayout.UnsafePathReason(relativeFile.Replace('\\', '/'));
        return reason is null ? Check(projectFolder, relativeFile, strict: true) : Unsafe(reason);
    }

    private static PackFolderPathCheck Check(string projectFolder, string relativeFile, bool strict)
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

            if (fullPath.Length == root.Length)
            {
                return Unsafe("The path is the pack folder itself, not a file in it.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Unsafe("The file path cannot be resolved: " + ex.Message);
        }

        var (link, detail) = LinkOnPath(packFolder, relativeFile, strict);
        return link is null
            ? new PackFolderPathCheck(PackPathProblem.None, fullPath, null, null)
            : new PackFolderPathCheck(PackPathProblem.Link, fullPath, link, detail);
    }

    private static PackFolderPathCheck Unsafe(string detail) => new(PackPathProblem.UnsafePath, null, null, detail);

    private static (string? Link, string? Detail) LinkOnPath(string packFolder, string relativeFile, bool strict)
    {
        var packFolderState = Inspect(packFolder, strict, ProjectFile.PackFolder);
        if (packFolderState.Refused)
        {
            return (ProjectFile.PackFolder, packFolderState.Detail);
        }

        var path = packFolder;
        var walked = ProjectFile.PackFolder;
        foreach (var segment in relativeFile.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, segment);
            walked += "\\" + segment;
            var state = Inspect(path, strict, walked);
            if (state.Refused)
            {
                return (walked, state.Detail);
            }
        }

        return (null, null);
    }

    /// <summary>Refused when the part is a link or, only for a strict check, cannot be asked (then with a detail).</summary>
    private static (bool Refused, string? Detail) Inspect(string path, bool strict, string shown)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) ? (true, null) : (false, null);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return (false, null);   // a part that does not exist is no link
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return strict ? (true, $"cannot check the folder for links: '{shown}' ({ex.Message})") : (false, null);
        }
    }
}
