using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Projects;

/// <summary>Why <see cref="ProjectFileOps"/> did not do what was asked.</summary>
public enum FileOpProblem
{
    /// <summary>It was done.</summary>
    None,

    /// <summary>The path is empty, rooted, has a '..' segment or is otherwise not a plain file path below the pack folder. Nothing was touched.</summary>
    UnsafePath,

    /// <summary>The pack folder, a folder on the way or the file itself is a link, or became one while the operation ran. Nothing was written through it.</summary>
    Link,

    /// <summary>A file or a folder is already at the target (or a file is in the place of a folder on the way). Nothing was changed.</summary>
    TargetExists,

    /// <summary>The file to rename is not there (or is a folder).</summary>
    SourceMissing,

    /// <summary>The file could not be moved because another program holds it open or the file system refused. The file is unchanged.</summary>
    Locked,

    /// <summary>Anything else: the project folder is missing, a folder or the temp file could not be written.</summary>
    Failed,
}

/// <summary>
/// The outcome of a file operation. <paramref name="Detail"/> is short technical English and null on success; for a failure it also names
/// what this call could not take back (see <see cref="ProjectFileOps"/>).
/// <paramref name="Fingerprint"/> is the state of the file right after the operation. It is set for Problem None unless the file was gone
/// again by the time it was read (then null); for every other problem it is null.
/// <paramref name="CreatedFolders"/> are the folders this call created (Problem None only, otherwise empty), outermost first, relative to
/// the pack folder with '\' and spelled as the caller wrote the path. The pack folder itself is never listed.
/// </summary>
public sealed record FileOpResult(FileOpProblem Problem, string? Detail, FileFingerprint? Fingerprint, IReadOnlyList<string> CreatedFolders);

/// <summary>
/// Creates and renames files below the pack folder of a mod project: never outside the pack folder, never through a link, never over an
/// existing file or folder. Every path goes through <see cref="PackFolderPaths.ResolveForWrite"/>, and again right before the file is
/// written and moved (and before every retry of the move), because a link can appear while the operation runs. Never throws for the file
/// system (a cancelled <see cref="Create"/> throws <see cref="OperationCanceledException"/>); only argument errors are thrown.
/// A call that does not succeed takes back what it did: it deletes its temp file and removes the folders it created (innermost first, only
/// when empty); the way to each of them is resolved again first, and nothing is deleted when it is no longer clean. What can remain: a
/// folder into which someone else put a file meanwhile, a temp file that could not be deleted, and a folder that was replaced by a link
/// (a link is never removed). The remains are named in the Detail of the failed result. A folder is counted as created by this call
/// when it did not exist right before it was created and is a real folder right after; a folder that another process creates in that
/// very moment can be taken for ours.
/// </summary>
public static class ProjectFileOps
{
    /// <summary>The points at which a test can change the disk, as another process would.</summary>
    internal sealed record OpsHooks
    {
        /// <summary>After the path was resolved, before any folder is created.</summary>
        public Action? BeforeFolders { get; init; }

        /// <summary>After the folders were created, before anything is written.</summary>
        public Action? AfterFolders { get; init; }

        /// <summary>After the temp file was written (Create), right before the move.</summary>
        public Action? BeforeMove { get; init; }
    }

    /// <summary>
    /// Creates pack\&lt;relativeFile&gt; with exactly these bytes: first a temp file next to the target (<c>&lt;target&gt;.&lt;8 hex&gt;.tmp</c>,
    /// created new), then a move without overwrite (retried briefly while another program holds the temp file). Missing folders below the
    /// pack folder are created one by one from the outside in and are reported in <see cref="FileOpResult.CreatedFolders"/>. A missing
    /// pack folder is created too when the project folder exists and nothing named "pack" is in it; it is not reported and not removed
    /// again, because a project always may have its pack folder.
    /// The token is checked before the first folder, before the temp file and before the move; once the file is moved the result is
    /// final. Cancelling, like every other failure, takes back the temp file and the folders this call created.
    /// </summary>
    public static FileOpResult Create(string projectFolder, string relativeFile, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        Create(projectFolder, relativeFile, content, cancellationToken, null);

    internal static FileOpResult Create(string projectFolder, string relativeFile, ReadOnlyMemory<byte> content, CancellationToken cancellationToken,
        OpsHooks? hooks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);

        var check = PackFolderPaths.ResolveForWrite(projectFolder, relativeFile);
        if (SafeFolders.Refuse(check, null) is { } refused)
        {
            return refused;
        }

        FolderResolver resolve = relative => PackFolderPaths.ResolveForWrite(projectFolder, relative);
        var packFolder = Path.GetFullPath(Path.Combine(projectFolder, ProjectFile.PackFolder));
        var undo = new Undo(packFolder, resolve);
        var finished = false;
        try
        {
            var result = CreateCore(projectFolder, relativeFile, content, cancellationToken, hooks, check.FullPath!, packFolder, resolve, undo);
            finished = true;
            return result.Problem == FileOpProblem.None ? result : WithLeftovers(result, undo.Clean());
        }
        finally
        {
            if (!finished)
            {
                undo.Clean();   // cancelled or an unexpected error: the exception goes on
            }
        }
    }

    private static FileOpResult CreateCore(string projectFolder, string relativeFile, ReadOnlyMemory<byte> content, CancellationToken cancellationToken,
        OpsHooks? hooks, string target, string packFolder, FolderResolver resolve, Undo undo)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(target) || Directory.Exists(target))
        {
            return SafeFolders.Failure(FileOpProblem.TargetExists, "a file or a folder is already at the target: " + relativeFile);
        }

        hooks?.BeforeFolders?.Invoke();
        if (EnsurePackFolder(projectFolder, packFolder, undo.Folders) is { } packFailure)
        {
            return packFailure;
        }

        if (undo.Folders.Ensure(Segments(relativeFile)) is { } folderFailure)
        {
            return folderFailure;
        }

        hooks?.AfterFolders?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (SafeFolders.Reverify(resolve, relativeFile, target, null) is { } changedBeforeWrite)
        {
            return changedBeforeWrite;
        }

        try
        {
            // A temp file that was written badly and could not be deleted is handed to the undo, which names it if it stays.
            undo.Temp = ProjectTempFiles.Create(target, content, ProjectTempFiles.NewSuffix, undeletable => undo.Temp = undeletable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return SafeFolders.Failure(FileOpProblem.Failed, "the temp file could not be written: " + ex.Message);
        }

        if (undo.Temp is null)
        {
            return SafeFolders.Failure(FileOpProblem.Failed, "no free name for the temp file was found");
        }

        cancellationToken.ThrowIfCancellationRequested();
        hooks?.BeforeMove?.Invoke();

        FileOpResult? changed = null;
        bool StillSafe()
        {
            changed = SafeFolders.Reverify(resolve, relativeFile, target, null);
            return changed is null;
        }

        if (!StillSafe())
        {
            return changed!;
        }

        try
        {
            if (!ProjectTempFiles.MoveWithoutOverwrite(undo.Temp, target, () => File.Exists(target) || Directory.Exists(target), cancellationToken, StillSafe))
            {
                return changed!;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return File.Exists(target) || Directory.Exists(target)
                ? SafeFolders.Failure(FileOpProblem.TargetExists, "a file or a folder appeared at the target: " + relativeFile)
                : SafeFolders.Failure(FileOpProblem.Failed, "the file could not be created: " + ex.Message);
        }

        undo.Temp = null;   // moved: it is the target now
        return new FileOpResult(FileOpProblem.None, null, FileFingerprint.TryRead(target), [.. undo.Folders.Relative]);
    }

    /// <summary>
    /// Renames pack\&lt;from&gt; to pack\&lt;to&gt;, also into another folder below the pack folder, without overwriting. Missing folders of
    /// the target are created like <see cref="Create"/> does and reported; when the move then fails they are taken back. Names that
    /// differ only in case are a rename (on a file system that compares ignoring case the target "exists" and is still allowed); the
    /// target counts as taken when an entry of exactly that name is listed in its folder, so on a case-sensitive folder a second file
    /// that differs only in case is TargetExists, and so is the very same name. A rename that differs only in the case of a FOLDER
    /// segment (<c>db\a.tsv</c> to <c>DB\a.tsv</c>) is TargetExists too: renaming a file cannot change the spelling of its folder.
    /// SourceMissing when <paramref name="from"/> is not a file. A sharing violation of the move is retried briefly; Locked when the
    /// move then fails with an IO or access error while the source is still there and the target is not.
    /// </summary>
    public static FileOpResult Rename(string projectFolder, string from, string to) => Rename(projectFolder, from, to, null);

    internal static FileOpResult Rename(string projectFolder, string from, string to, OpsHooks? hooks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        var fromCheck = PackFolderPaths.ResolveForWrite(projectFolder, from);
        if (SafeFolders.Refuse(fromCheck, "source") is { } fromRefused)
        {
            return fromRefused;
        }

        var toCheck = PackFolderPaths.ResolveForWrite(projectFolder, to);
        if (SafeFolders.Refuse(toCheck, "target") is { } toRefused)
        {
            return toRefused;
        }

        var source = fromCheck.FullPath!;
        var target = toCheck.FullPath!;
        if (!File.Exists(source))
        {
            return SafeFolders.Failure(FileOpProblem.SourceMissing, "the file to rename does not exist: " + from);
        }

        var sameFile = string.Equals(source, target, StringComparison.OrdinalIgnoreCase);
        bool TargetTaken() => sameFile ? EntryListed(target) : File.Exists(target) || Directory.Exists(target);
        if (string.Equals(source, target, StringComparison.Ordinal) || TargetTaken())
        {
            return SafeFolders.Failure(FileOpProblem.TargetExists, "a file or a folder is already at the target: " + to);
        }

        FolderResolver resolve = relative => PackFolderPaths.ResolveForWrite(projectFolder, relative);
        var packFolder = Path.GetFullPath(Path.Combine(projectFolder, ProjectFile.PackFolder));
        var undo = new Undo(packFolder, resolve);
        var finished = false;
        try
        {
            var result = RenameCore(from, to, hooks, source, target, packFolder, resolve, TargetTaken, undo);
            finished = true;
            return result.Problem == FileOpProblem.None ? result : WithLeftovers(result, undo.Clean());
        }
        finally
        {
            if (!finished)
            {
                undo.Clean();
            }
        }
    }

    private static FileOpResult RenameCore(string from, string to, OpsHooks? hooks, string source, string target, string packFolder,
        FolderResolver resolve, Func<bool> targetTaken, Undo undo)
    {
        hooks?.BeforeFolders?.Invoke();
        if (undo.Folders.Ensure(Segments(to)) is { } folderFailure)
        {
            return folderFailure;
        }

        hooks?.AfterFolders?.Invoke();
        hooks?.BeforeMove?.Invoke();

        FileOpResult? changed = null;
        bool StillSafe()
        {
            changed = SafeFolders.Reverify(resolve, from, source, "source") ?? SafeFolders.Reverify(resolve, to, target, "target");
            return changed is null;
        }

        if (!StillSafe())
        {
            return changed!;
        }

        try
        {
            if (!ProjectTempFiles.MoveWithoutOverwrite(source, target, targetTaken, CancellationToken.None, StillSafe))
            {
                return changed!;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (targetTaken())
            {
                return SafeFolders.Failure(FileOpProblem.TargetExists, "a file or a folder appeared at the target: " + to);
            }

            return SafeFolders.Failure(File.Exists(source) ? FileOpProblem.Locked : FileOpProblem.Failed, "the file could not be renamed: " + ex.Message);
        }

        return new FileOpResult(FileOpProblem.None, null, FileFingerprint.TryRead(target), [.. undo.Folders.Relative]);
    }

    /// <summary>
    /// Removes the given folders (relative to the pack folder, as <see cref="Create"/> reported them, outermost first) when they are
    /// empty, starting with the innermost; stops at the first one that is not empty or cannot be removed. A folder that is already gone
    /// is skipped. Never removes the pack folder, a link or anything outside the pack folder (such an entry stops the removal). Never throws
    /// for the file system; only argument errors are thrown.
    /// </summary>
    public static void RemoveEmptyFolders(string projectFolder, IReadOnlyList<string> foldersOutermostFirst)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(foldersOutermostFirst);

        for (var i = foldersOutermostFirst.Count - 1; i >= 0; i--)
        {
            var folder = foldersOutermostFirst[i];
            if (folder is null)
            {
                return;
            }

            var check = PackFolderPaths.ResolveForWrite(projectFolder, folder);
            if (check.Problem != PackPathProblem.None)
            {
                return;
            }

            var path = check.FullPath!;
            if (!Directory.Exists(path))
            {
                continue;
            }

            try
            {
                Directory.Delete(path, recursive: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;   // not empty, or in use: the outer folders hold it
            }
        }
    }

    /// <summary>
    /// True when pack\&lt;relativeFile&gt; is taken, as <see cref="Create"/> sees it: a file or a folder exists there, or a file is in the
    /// place of a folder on the way (so that no folder can be made); compared ignoring case (the file system's own comparison). False for
    /// an unsafe path and for a path through a link: nothing is looked at through a link.
    /// </summary>
    public static bool Exists(string projectFolder, string relativeFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);

        var check = PackFolderPaths.ResolveForWrite(projectFolder, relativeFile);
        if (check.Problem != PackPathProblem.None)
        {
            return false;
        }

        if (File.Exists(check.FullPath!) || Directory.Exists(check.FullPath!))
        {
            return true;
        }

        var path = Path.GetFullPath(Path.Combine(projectFolder, ProjectFile.PackFolder));
        var segments = Segments(relativeFile);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            path = Path.Combine(path, segments[i]);
            if (File.Exists(path))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] Segments(string relativeFile) => relativeFile.Split(['\\', '/']);

    /// <summary>
    /// Makes sure the pack folder exists and is no link. Null when it is fine; a failure when nothing named "pack" can be made into a folder.
    /// The pack folder is never recorded as created: it stays.
    /// </summary>
    private static FileOpResult? EnsurePackFolder(string projectFolder, string packFolder, CreatedFolders folders)
    {
        if (Directory.Exists(packFolder))
        {
            return folders.CheckBase();
        }

        if (File.Exists(packFolder))
        {
            return SafeFolders.Failure(FileOpProblem.Failed, "a file named 'pack' is in the project folder");
        }

        if (!Directory.Exists(projectFolder))
        {
            return SafeFolders.Failure(FileOpProblem.Failed, "the project folder does not exist");
        }

        if (folders.CheckBase() is { } danglingLink)
        {
            return danglingLink;   // a link whose target is gone
        }

        try
        {
            Directory.CreateDirectory(packFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return SafeFolders.Failure(FileOpProblem.Failed, "the pack folder could not be created: " + ex.Message);
        }

        if (folders.CheckBase() is { } linkNow)
        {
            return linkNow;
        }

        return Directory.Exists(packFolder) ? null : SafeFolders.Failure(FileOpProblem.Failed, "the pack folder is not there after it was created");
    }

    /// <summary>True when the folder of <paramref name="path"/> lists an entry with exactly the name of <paramref name="path"/> (ordinal). An unreadable folder counts as listed.</summary>
    private static bool EntryListed(string path)
    {
        var folder = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        if (folder is null)
        {
            return false;
        }

        try
        {
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if (string.Equals(entry.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;   // never assume that the target is free when it cannot be looked at
        }
    }

    private static FileOpResult WithLeftovers(FileOpResult result, IReadOnlyList<string> leftovers) =>
        leftovers.Count == 0 ? result : result with { Detail = result.Detail + " (left behind: " + string.Join(", ", leftovers) + ")" };

    /// <summary>What a call has made so far and has to take back when it does not succeed.</summary>
    private sealed class Undo(string packFolder, FolderResolver resolve)
    {
        /// <summary>The temp file this call created, or null (none yet, or already moved).</summary>
        public string? Temp { get; set; }

        public CreatedFolders Folders { get; } = new(packFolder, ProjectFile.PackFolder, resolve);

        /// <summary>
        /// Deletes the temp file and removes the created folders, innermost first, each only when the way to it is still clean
        /// (see <see cref="CreatedFolders.Clean"/>). Never throws. Returns what could not be taken back, for the Detail.
        /// </summary>
        public List<string> Clean()
        {
            var leftovers = new List<string>();
            if (Temp is not null)
            {
                var temp = Temp;
                Temp = null;
                if (!Folders.TryDeleteFile(temp))
                {
                    leftovers.Add("temp file '" + Path.GetRelativePath(packFolder, temp) + "'");
                }
            }

            leftovers.AddRange(Folders.Clean());
            return leftovers;
        }
    }
}
