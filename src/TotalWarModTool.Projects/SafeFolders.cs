using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Projects;

/// <summary>
/// Checks a path relative to a base folder (spelled with '\') for a write and says where it leads. For the pack folder of a project this is
/// <see cref="PackFolderPaths.ResolveForWrite"/>; another base folder (a trash folder) brings its own rules. The result has the meaning
/// of <see cref="PackFolderPathCheck"/>: <c>FullPath</c> is set for None and Link, a problem means "do not touch".
/// </summary>
internal delegate PackFolderPathCheck FolderResolver(string relativePath);

/// <summary>
/// Small helpers for code that changes the disk below a base folder and must never do it through a link, also when a link appears while
/// it works. They know nothing about the pack folder: the base folder and the rules come in through a <see cref="FolderResolver"/>.
/// </summary>
internal static class SafeFolders
{
    public static FileOpResult Failure(FileOpProblem problem, string detail) => new(problem, detail, null, []);

    /// <summary>The refusal for a folder that is a link.</summary>
    public static FileOpResult LinkFolder(string relative) =>
        Failure(FileOpProblem.Link, $"links are not supported: the folder '{relative}' is a link");

    /// <summary>Null when the path is fine; otherwise the refusal. <paramref name="which"/> names the path of a rename in the detail.</summary>
    public static FileOpResult? Refuse(PackFolderPathCheck check, string? which)
    {
        var prefix = which is null ? string.Empty : which + ": ";
        return check.Problem switch
        {
            PackPathProblem.UnsafePath => Failure(FileOpProblem.UnsafePath, prefix + (check.Detail ?? "the path is not a plain file path below the base folder")),
            PackPathProblem.Link => Failure(FileOpProblem.Link,
                prefix + (check.Detail ?? $"links are not supported: the path passes through the link '{check.LinkPath}'")),
            _ => null,
        };
    }

    /// <summary>
    /// The path is checked again right before the disk is changed: a link may have appeared since the first check. Null when it still
    /// resolves to <paramref name="expectedFullPath"/> without a link; otherwise the refusal.
    /// </summary>
    public static FileOpResult? Reverify(FolderResolver resolve, string relativePath, string expectedFullPath, string? which)
    {
        var check = resolve(relativePath);
        if (check.Problem == PackPathProblem.None && string.Equals(check.FullPath, expectedFullPath, StringComparison.Ordinal))
        {
            return null;
        }

        if (check.Problem == PackPathProblem.None)
        {
            return Failure(FileOpProblem.UnsafePath, (which is null ? string.Empty : which + ": ") + "the path resolves to another place than before: " + relativePath);
        }

        var refusal = Refuse(check, which)!;
        return refusal with { Detail = "the path changed while the operation ran: " + refusal.Detail };
    }
}

/// <summary>
/// The folders one call creates below a base folder, and the way to take them back. <see cref="Ensure"/> creates them one by one from
/// the outside in and never enters a link; <see cref="Clean"/> removes them again (innermost first) and checks the way to every folder
/// before it deletes anything. One instance per call; not thread safe.
/// </summary>
internal sealed class CreatedFolders(string baseFolder, string baseName, FolderResolver resolve)
{
    /// <summary>The folders this call created, relative to the base folder with '\', outermost first.</summary>
    public List<string> Relative { get; } = [];

    /// <summary>The refusal when the base folder itself is a link at this moment; otherwise null. A base folder that does not exist is no link.</summary>
    public FileOpResult? CheckBase() => ProjectFiles.IsLink(baseFolder) ? SafeFolders.LinkFolder(baseName) : null;

    /// <summary>
    /// Creates the missing folders of the path (all segments but the last) one by one from the outside in. The base folder must exist and
    /// be no link. Every folder on the way is looked at for a link at this moment, because it may have appeared since the path was
    /// resolved; a link is never entered, never recorded and never removed. A folder is recorded only when it did not exist right before
    /// it was created and is a real folder right after; a real folder that appeared meanwhile is used but not recorded. Null on success.
    /// </summary>
    public FileOpResult? Ensure(string[] segments)
    {
        if (CheckBase() is { } baseFailure)
        {
            return baseFailure;
        }

        if (!Directory.Exists(baseFolder))
        {
            return SafeFolders.Failure(FileOpProblem.Failed, $"the folder '{baseName}' does not exist");
        }

        var path = baseFolder;
        var relative = string.Empty;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            path = Path.Combine(path, segments[i]);
            relative = relative.Length == 0 ? segments[i] : relative + "\\" + segments[i];
            if (Directory.Exists(path))
            {
                if (ProjectFiles.IsLink(path))
                {
                    return SafeFolders.LinkFolder(relative);
                }

                continue;   // a real folder, whether it was there before or appeared meanwhile: used, not ours
            }

            if (File.Exists(path))
            {
                return SafeFolders.Failure(FileOpProblem.TargetExists, $"a file is in the place of the folder '{relative}'");
            }

            if (ProjectFiles.IsLink(path))
            {
                return SafeFolders.LinkFolder(relative);   // a link whose target is gone
            }

            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return SafeFolders.Failure(FileOpProblem.Failed, $"the folder '{relative}' could not be created: " + ex.Message);
            }

            // CreateDirectory succeeds silently on a folder that is already there, so a link that appeared in that moment shows only here.
            if (ProjectFiles.IsLink(path))
            {
                return SafeFolders.LinkFolder(relative);
            }

            if (!Directory.Exists(path))
            {
                return SafeFolders.Failure(FileOpProblem.Failed, $"the folder '{relative}' is not there after it was created");
            }

            Relative.Add(relative);
        }

        return null;
    }

    /// <summary>
    /// Removes the created folders, innermost first (non-recursive: a folder with content stays). Before each folder the whole way to it is
    /// resolved again; when it is no longer clean (a link took the place of a folder on the way) nothing more is removed. Never throws.
    /// Returns what could not be taken back (the stopped folder and the folders around it), for the Detail of a result.
    /// </summary>
    public List<string> Clean()
    {
        var leftovers = new List<string>();
        for (var i = Relative.Count - 1; i >= 0; i--)
        {
            if (!TryRemoveFolder(Relative[i]))
            {
                for (var outer = i; outer >= 0; outer--)
                {
                    leftovers.Add("folder '" + Relative[outer] + "'");
                }

                break;
            }
        }

        Relative.Clear();
        return leftovers;
    }

    /// <summary>
    /// Deletes a file this call created (a temp file) when the way to it is still clean: resolved again, no link, still the same place.
    /// False when it was not deleted (unclean way, or the delete failed).
    /// </summary>
    public bool TryDeleteFile(string fullPath)
    {
        var check = resolve(Path.GetRelativePath(baseFolder, fullPath));
        if (check.Problem != PackPathProblem.None || !string.Equals(check.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ProjectTempFiles.TryDelete(check.FullPath!);
    }

    private bool TryRemoveFolder(string relative)
    {
        var check = resolve(relative);
        if (check.Problem != PackPathProblem.None)
        {
            return false;   // a link on the way (or the folder itself): not what this call made, left alone
        }

        try
        {
            Directory.Delete(check.FullPath!, recursive: false);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;   // gone already
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
