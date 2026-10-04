namespace TotalWarModTool.Projects;

/// <summary>A file below the pack folder that goes into the pack. <paramref name="RelativeFile"/> uses '\' and is relative to the pack folder.</summary>
public sealed record ProjectFileItem(string RelativeFile, string PackPath, ProjectFileKind Kind);

/// <summary>A folder below the pack folder that is a link or cannot be listed. <paramref name="RelativePath"/> uses '\'; <paramref name="Detail"/> is short English.</summary>
public sealed record ProjectFileProblem(string RelativePath, string Detail);

/// <summary>
/// The files of a project's pack folder. <paramref name="Files"/> go into the pack, <paramref name="Ignored"/> (relative paths with '\')
/// never do. Both and <paramref name="Problems"/> are sorted ordinally by relative path.
/// </summary>
public sealed record ProjectFileList(bool PackFolderExists, IReadOnlyList<ProjectFileItem> Files, IReadOnlyList<string> Ignored,
    IReadOnlyList<ProjectFileProblem> Problems);

/// <summary>
/// The one place that decides which files belong to a mod project: the pack builder and the explorer both use it.
/// Reads the disk but writes nothing.
/// </summary>
public static class ProjectFiles
{
    private const string LinkProblem = "links to folders are not supported";
    private const string ListProblem = "the folder cannot be listed: ";

    private static readonly string[] IgnoredPrefixes = [".~lock.", "~$"];
    private static readonly string[] IgnoredSuffixes = [".bak", ".tmp", "~"];

    /// <summary>
    /// Lists the pack folder of a project folder by folder. A link to a folder is not followed and a folder that cannot be listed is a
    /// problem; both are reported and the rest is still listed. A pack folder that is itself a link is one problem with an empty
    /// relative path, and nothing is listed. Only <see cref="OperationCanceledException"/> (checked per folder and
    /// per file) and argument errors are thrown.
    /// </summary>
    public static ProjectFileList List(string projectFolder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        cancellationToken.ThrowIfCancellationRequested();

        var packRoot = Path.Combine(projectFolder, ProjectFile.PackFolder);
        if (!Directory.Exists(packRoot))
        {
            return new ProjectFileList(false, [], [], []);
        }

        // A linked pack folder is refused like a linked sub folder: nothing behind it belongs to the project.
        if (IsLink(packRoot))
        {
            return new ProjectFileList(true, [], [], [new ProjectFileProblem(string.Empty, LinkProblem)]);
        }

        var files = new List<ProjectFileItem>();
        var ignored = new List<string>();
        var problems = new List<ProjectFileProblem>();
        var pending = new Stack<string>();
        pending.Push(string.Empty);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativeFolder = pending.Pop();
            var folder = relativeFolder.Length == 0 ? packRoot : Path.Combine(packRoot, relativeFolder);
            var folderFiles = new List<ProjectFileItem>();
            var folderIgnored = new List<string>();
            var subfolders = new List<(string Relative, bool IsLink)>();

            try
            {
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = relativeFolder.Length == 0 ? entry.Name : relativeFolder + "\\" + entry.Name;
                    if (entry is DirectoryInfo)
                    {
                        subfolders.Add((relative, entry.Attributes.HasFlag(FileAttributes.ReparsePoint)));
                    }
                    else if (IsIgnored(entry.Name, entry.Attributes))
                    {
                        folderIgnored.Add(relative);
                    }
                    else
                    {
                        var (packPath, kind) = ProjectLayout.FromRelativeFile(relative);
                        folderFiles.Add(new ProjectFileItem(relative, packPath, kind));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing of this folder is kept: its files would be a partial picture next to the problem.
                problems.Add(new ProjectFileProblem(relativeFolder, ListProblem + ex.Message));
                continue;
            }

            files.AddRange(folderFiles);
            ignored.AddRange(folderIgnored);
            foreach (var (relative, isLink) in subfolders)
            {
                if (isLink)
                {
                    problems.Add(new ProjectFileProblem(relative, LinkProblem));
                }
                else
                {
                    pending.Push(relative);
                }
            }
        }

        files.Sort((a, b) => string.CompareOrdinal(a.RelativeFile, b.RelativeFile));
        ignored.Sort(StringComparer.Ordinal);
        problems.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
        return new ProjectFileList(true, files, ignored, problems);
    }

    /// <summary>
    /// True when the file or folder at <paramref name="path"/> exists and is a link (a reparse point such as a junction or a symbolic
    /// link). The link itself is looked at, never its target. A path that cannot be read counts as no link: the caller's own read
    /// reports it.
    /// </summary>
    public static bool IsLink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// True for files that never go into a pack: hidden or system attribute, or an editor lock or backup name (a name that starts with
    /// ".~lock." or "~$" or ends with ".bak", ".tmp" or "~", ignoring case). Meant for files, not for folders.
    /// </summary>
    public static bool IsIgnored(string fileName, FileAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
        {
            return true;
        }

        foreach (var prefix in IgnoredPrefixes)
        {
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var suffix in IgnoredSuffixes)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
