using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Projects;

/// <summary>Why <see cref="ProjectFileOps"/> did not do what was asked.</summary>
public enum FileOpProblem
{
    /// <summary>It was done.</summary>
    None,

    /// <summary>The path is empty, rooted, has a '..' segment or is otherwise not a plain file path below the pack folder. Nothing was touched.</summary>
    UnsafePath,

    /// <summary>The pack folder, a folder on the way or the file itself is a link. Nothing was touched.</summary>
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
/// The outcome of a file operation. <paramref name="Detail"/> is short technical English and null on success.
/// <paramref name="Fingerprint"/> is the state of the file right after the operation (Problem None), otherwise null.
/// <paramref name="CreatedFolders"/> are the folders this call created (Problem None only, otherwise empty), outermost first, relative to
/// the pack folder with '\' and spelled as the caller wrote the path. The pack folder itself is never listed.
/// </summary>
public sealed record FileOpResult(FileOpProblem Problem, string? Detail, FileFingerprint? Fingerprint, IReadOnlyList<string> CreatedFolders);

/// <summary>
/// Creates and renames files below the pack folder of a mod project: never outside the pack folder, never through a link, never over an
/// existing file or folder. Every path goes through <see cref="PackFolderPaths.ResolveForWrite"/>. Never throws for the file system
/// (a cancelled <see cref="Create"/> throws <see cref="OperationCanceledException"/>); only argument errors are thrown. A call that
/// does not succeed leaves no temp file and no folder it created.
/// </summary>
public static class ProjectFileOps
{
    private const int TempNameAttempts = 5;

    /// <summary>
    /// Creates pack\&lt;relativeFile&gt; with exactly these bytes: first a temp file next to the target (<c>&lt;target&gt;.&lt;8 hex&gt;.tmp</c>,
    /// created new), then a move without overwrite. Missing folders below the pack folder are created one by one from the outside in and
    /// are reported in <see cref="FileOpResult.CreatedFolders"/>. A missing pack folder is created too when the project folder exists
    /// and nothing named "pack" is in it; it is not reported and not removed again, because a project always may have its pack folder.
    /// The token is checked before the first folder, before the temp file and before the move; once the file is moved the result is
    /// final. Cancelling, like every other failure, removes the temp file and the folders this call created (innermost first, only
    /// when empty).
    /// </summary>
    public static FileOpResult Create(string projectFolder, string relativeFile, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        Create(projectFolder, relativeFile, content, cancellationToken, null);

    /// <summary>The overload with a hook that runs after the folders were created, so that a test can cancel or interfere at that point.</summary>
    internal static FileOpResult Create(string projectFolder, string relativeFile, ReadOnlyMemory<byte> content, CancellationToken cancellationToken,
        Action? afterFoldersCreated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);

        var check = PackFolderPaths.ResolveForWrite(projectFolder, relativeFile);
        if (Refuse(check, null) is { } refused)
        {
            return refused;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var target = check.FullPath!;
        if (File.Exists(target) || Directory.Exists(target))
        {
            return Failure(FileOpProblem.TargetExists, "a file or a folder is already at the target: " + relativeFile);
        }

        var packFolder = Path.GetFullPath(Path.Combine(projectFolder, ProjectFile.PackFolder));
        if (EnsurePackFolder(projectFolder, packFolder) is { } packFailure)
        {
            return packFailure;
        }

        var createdFull = new List<string>();
        var createdRelative = new List<string>();
        string? temp = null;   // set only for a temp file that this call created: only that one is ever deleted
        var succeeded = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (EnsureFolders(packFolder, Segments(relativeFile), createdFull, createdRelative) is { } folderFailure)
            {
                return folderFailure;
            }

            afterFoldersCreated?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                temp = CreateTempFile(target, content);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return Failure(FileOpProblem.Failed, "the temp file could not be written: " + ex.Message);
            }

            if (temp is null)
            {
                return Failure(FileOpProblem.Failed, "no free name for the temp file was found");
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                File.Move(temp, target, overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return File.Exists(target) || Directory.Exists(target)
                    ? Failure(FileOpProblem.TargetExists, "a file or a folder appeared at the target: " + relativeFile)
                    : Failure(FileOpProblem.Failed, "the file could not be created: " + ex.Message);
            }

            succeeded = true;
            temp = null;   // moved: it is the target now
            return new FileOpResult(FileOpProblem.None, null, FileFingerprint.TryRead(target), createdRelative);
        }
        finally
        {
            if (!succeeded)
            {
                if (temp is not null)
                {
                    TryDelete(temp);
                }

                RemoveFolders(createdFull);
            }
        }
    }

    /// <summary>
    /// Renames pack\&lt;from&gt; to pack\&lt;to&gt;, also into another folder below the pack folder, without overwriting. Missing folders of
    /// the target are created like <see cref="Create"/> does and reported; when the move then fails they are removed again. Names that
    /// differ only in case are a rename (the file system compares ignoring case, so this is the one case where the target "exists"
    /// and is still allowed); the very same name is TargetExists. SourceMissing when <paramref name="from"/> is not a file.
    /// Locked when the move fails with an IO or access error while the source is still there and the target is not.
    /// </summary>
    public static FileOpResult Rename(string projectFolder, string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        var fromCheck = PackFolderPaths.ResolveForWrite(projectFolder, from);
        if (Refuse(fromCheck, "source") is { } fromRefused)
        {
            return fromRefused;
        }

        var toCheck = PackFolderPaths.ResolveForWrite(projectFolder, to);
        if (Refuse(toCheck, "target") is { } toRefused)
        {
            return toRefused;
        }

        var source = fromCheck.FullPath!;
        var target = toCheck.FullPath!;
        if (!File.Exists(source))
        {
            return Failure(FileOpProblem.SourceMissing, "the file to rename does not exist: " + from);
        }

        var sameFile = string.Equals(source, target, StringComparison.OrdinalIgnoreCase);
        if (string.Equals(source, target, StringComparison.Ordinal) || (!sameFile && (File.Exists(target) || Directory.Exists(target))))
        {
            return Failure(FileOpProblem.TargetExists, "a file or a folder is already at the target: " + to);
        }

        var packFolder = Path.GetFullPath(Path.Combine(projectFolder, ProjectFile.PackFolder));
        var createdFull = new List<string>();
        var createdRelative = new List<string>();
        var succeeded = false;
        try
        {
            if (EnsureFolders(packFolder, Segments(to), createdFull, createdRelative) is { } folderFailure)
            {
                return folderFailure;
            }

            try
            {
                File.Move(source, target, overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var targetTaken = !sameFile && (File.Exists(target) || Directory.Exists(target));
                if (targetTaken)
                {
                    return Failure(FileOpProblem.TargetExists, "a file or a folder appeared at the target: " + to);
                }

                return Failure(File.Exists(source) ? FileOpProblem.Locked : FileOpProblem.Failed, "the file could not be renamed: " + ex.Message);
            }

            succeeded = true;
            return new FileOpResult(FileOpProblem.None, null, FileFingerprint.TryRead(target), createdRelative);
        }
        finally
        {
            if (!succeeded)
            {
                RemoveFolders(createdFull);
            }
        }
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
    /// True when pack\&lt;relativeFile&gt; is taken: a file or a folder exists there, compared ignoring case (the file system's own
    /// comparison). False for an unsafe path and for a path through a link: nothing is looked at through a link.
    /// </summary>
    public static bool Exists(string projectFolder, string relativeFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);

        var check = PackFolderPaths.ResolveForWrite(projectFolder, relativeFile);
        return check.Problem == PackPathProblem.None && (File.Exists(check.FullPath!) || Directory.Exists(check.FullPath!));
    }

    private static string[] Segments(string relativeFile) => relativeFile.Split(['\\', '/']);

    /// <summary>Null when the path is fine; otherwise the refusal. <paramref name="which"/> names the path of a rename in the detail.</summary>
    private static FileOpResult? Refuse(PackFolderPathCheck check, string? which)
    {
        var prefix = which is null ? string.Empty : which + ": ";
        return check.Problem switch
        {
            PackPathProblem.UnsafePath => Failure(FileOpProblem.UnsafePath, prefix + (check.Detail ?? "the path is not a plain file path below the pack folder")),
            PackPathProblem.Link => Failure(FileOpProblem.Link,
                prefix + (check.Detail ?? $"links are not supported: the path passes through the link '{check.LinkPath}'")),
            _ => null,
        };
    }

    /// <summary>Makes sure the pack folder exists. Null when it does; a failure when nothing named "pack" can be made into one.</summary>
    private static FileOpResult? EnsurePackFolder(string projectFolder, string packFolder)
    {
        if (Directory.Exists(packFolder))
        {
            return null;
        }

        if (File.Exists(packFolder))
        {
            return Failure(FileOpProblem.Failed, "a file named 'pack' is in the project folder");
        }

        if (!Directory.Exists(projectFolder))
        {
            return Failure(FileOpProblem.Failed, "the project folder does not exist");
        }

        try
        {
            Directory.CreateDirectory(packFolder);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Failure(FileOpProblem.Failed, "the pack folder could not be created: " + ex.Message);
        }
    }

    /// <summary>
    /// Creates the missing folders of the path (all segments but the last) one by one from the outside in and records each one that it
    /// made. Existing parts were already checked for links by <see cref="PackFolderPaths.ResolveForWrite"/>; a folder made here is a real
    /// folder. Null on success.
    /// </summary>
    private static FileOpResult? EnsureFolders(string packFolder, string[] segments, List<string> createdFull, List<string> createdRelative)
    {
        var path = packFolder;
        var relative = string.Empty;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            path = Path.Combine(path, segments[i]);
            relative = relative.Length == 0 ? segments[i] : relative + "\\" + segments[i];
            if (Directory.Exists(path))
            {
                continue;
            }

            if (File.Exists(path))
            {
                return Failure(FileOpProblem.TargetExists, $"a file is in the place of the folder '{relative}'");
            }

            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return Failure(FileOpProblem.Failed, $"the folder '{relative}' could not be created: " + ex.Message);
            }

            createdFull.Add(path);
            createdRelative.Add(relative);
        }

        return null;
    }

    /// <summary>Removes the folders this call created, innermost first. Best effort: a folder that is not empty stays (and so do its parents).</summary>
    private static void RemoveFolders(List<string> createdFull)
    {
        for (var i = createdFull.Count - 1; i >= 0; i--)
        {
            try
            {
                Directory.Delete(createdFull[i], recursive: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Creates <c>target.suffix.tmp</c> with <see cref="FileMode.CreateNew"/> and writes the bytes. Returns its path, or null when every
    /// name was taken. A name that is taken (file or folder) is never touched. A write error after the creation deletes the new file.
    /// </summary>
    private static string? CreateTempFile(string target, ReadOnlyMemory<byte> content)
    {
        for (var attempt = 0; attempt < TempNameAttempts; attempt++)
        {
            var temp = target + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            FileStream stream;
            try
            {
                stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && (File.Exists(temp) || Directory.Exists(temp)))
            {
                continue;   // the name is taken by something that is not ours
            }

            try
            {
                using (stream)
                {
                    stream.Write(content.Span);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                TryDelete(temp);
                throw;
            }

            return temp;
        }

        return null;
    }

    private static FileOpResult Failure(FileOpProblem problem, string detail) => new(problem, detail, null, []);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Cleanup is best effort; the problem that is reported is the one that matters.
        }
    }
}
