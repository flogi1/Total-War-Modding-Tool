using System.Globalization;
using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Projects;

/// <summary>
/// One file in the trash. <paramref name="TrashFile"/> is the full path <c>&lt;trash root&gt;\&lt;project id D&gt;\&lt;entry&gt;\&lt;file name&gt;</c>;
/// <paramref name="RelativeFile"/> is where it came from (below the pack folder, with '\').
/// </summary>
public sealed record TrashedFile(Guid ProjectId, string TrashFile, string RelativeFile);

/// <summary>
/// The outcome of <see cref="ProjectTrash.MoveToTrash(string, Guid, string, string, TimeProvider)"/>. <paramref name="Detail"/> is short technical
/// English and null on success; for a failure it also names what the call could not take back. <paramref name="File"/> is set for Problem None only.
/// </summary>
public sealed record TrashResult(FileOpProblem Problem, string? Detail, TrashedFile? File)
{
    /// <summary>
    /// True when a call that did not succeed could not take back everything it did (a folder or a copy in the trash stays; the Detail names
    /// it). False on success and when the failed call left the disk as it found it.
    /// </summary>
    public bool LeftBehind { get; init; }
}

/// <summary>An entry of the trash that <see cref="ProjectTrash.CleanupDetailed"/> chose to delete but could not: its full path and a short English reason.</summary>
public sealed record TrashCleanupFailure(string Entry, string Reason);

/// <summary>The outcome of <see cref="ProjectTrash.CleanupDetailed"/>: the number of deleted entries and the entries that stay although they were due.</summary>
public sealed record TrashCleanupResult(int Deleted, IReadOnlyList<TrashCleanupFailure> Failed);

/// <summary>
/// The trash of the tool for files deleted from the pack folder of a project: <c>&lt;trash root&gt;\&lt;project id D&gt;\&lt;yyyyMMdd-HHmmss&gt;-&lt;8 hex&gt;\&lt;file name&gt;</c>
/// (the time is the UTC time of the delete; the 8 hex characters keep two deletes of the same second apart). The trash root lies outside
/// every project and is the only place besides the pack folders where the tool writes: everything this class creates, moves or deletes
/// lies below <c>&lt;trash root&gt;\&lt;project id D&gt;\&lt;entry&gt;\</c>, and every part of that path is built from the project id and a
/// generated name, never from caller text. The trash root must be a fully qualified path that is neither a drive root nor a <c>\\?\</c>
/// form (UnsafePath, and <see cref="Cleanup"/> does nothing otherwise). Nothing happens through a link (junction or symbolic link): the
/// whole way to a trash folder is looked at before it is used and again right before a file is moved and before every retry of the move.
/// Nothing is overwritten. Never throws for the file system; only argument errors are thrown. A call that does not succeed leaves the file
/// where it was and takes back the folders it created (innermost first, only when empty); what could not be taken back is named in the Detail.
/// </summary>
public static class ProjectTrash
{
    /// <summary>The points at which a test can change the disk, as another process would.</summary>
    internal sealed record TrashHooks
    {
        /// <summary>After the folders were made, right before the move (MoveToTrash and Restore).</summary>
        public Action? BeforeMove { get; init; }

        /// <summary>Right after the move returned (MoveToTrash and Restore): a test can bring the source back, as a move across drives whose delete failed does.</summary>
        public Action? AfterMove { get; init; }

        /// <summary>In Cleanup, with the full path of the entry, after the entry was chosen and before anything is checked or deleted.</summary>
        public Action<string>? BeforeDeleteEntry { get; init; }
    }

    private const string EntryDateFormat = "yyyyMMdd-HHmmss";
    private const int EntryNameLength = 24;   // yyyyMMdd-HHmmss-xxxxxxxx
    private const int EntryNameAttempts = 5;
    private const int MaxEntryDepth = 64;
    private const string TrashBaseName = "trash";
    private const string ChangedWayReason = "a part of the way is a link, is gone or cannot be checked";

    /// <summary>
    /// Moves pack\&lt;relativeFile&gt; to <c>&lt;trashRoot&gt;\&lt;projectId D&gt;\&lt;yyyyMMdd-HHmmss&gt;-&lt;8 hex&gt;\&lt;file name&gt;</c> without
    /// overwriting; the missing folders of the trash root, the folder of the project and the entry folder are created one by one, each checked for
    /// a link. The source goes through <see cref="PackFolderPaths.ResolveForWrite"/> (UnsafePath, Link) and, like the trash side, is checked
    /// again right before the move and before every retry. SourceMissing when it is not a file (a folder is never moved). A sharing violation
    /// of the move is retried briefly; Locked when the move then fails while the source is still there. Locked also when the move reported
    /// success but a byte-identical file is still at the source (a move across drives copies and then deletes, and the delete can fail while the copy worked):
    /// the copy is deleted again so that exactly one copy survives (a different file there is another program's new file: the move stands). Link when a trash folder is a link. When the move fails, the folders
    /// created for it are removed again.
    /// </summary>
    public static TrashResult MoveToTrash(string trashRoot, Guid projectId, string projectFolder, string relativeFile, TimeProvider time) =>
        MoveToTrash(trashRoot, projectId, projectFolder, relativeFile, time, null);

    internal static TrashResult MoveToTrash(string trashRoot, Guid projectId, string projectFolder, string relativeFile, TimeProvider time,
        TrashHooks? hooks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trashRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);
        ArgumentNullException.ThrowIfNull(time);

        if (ValidateRoot(trashRoot, out var root) is { } badRoot)
        {
            return AsTrash(badRoot);
        }

        var check = PackFolderPaths.ResolveForWrite(projectFolder, relativeFile);
        if (SafeFolders.Refuse(check, null) is { } refused)
        {
            return AsTrash(refused);
        }

        var source = check.FullPath!;
        if (!File.Exists(source))
        {
            return AsTrash(SafeFolders.Failure(FileOpProblem.SourceMissing, "the file to delete does not exist: " + relativeFile));
        }

        // The base of this call is the trash root, or, when that is missing, its first existing ancestor: the missing parts are made one by one.
        var baseFolder = root;
        var missing = new List<string>();
        while (!Directory.Exists(baseFolder))
        {
            var parent = Path.GetDirectoryName(baseFolder);
            if (parent is null || File.Exists(baseFolder))
            {
                return AsTrash(SafeFolders.Failure(FileOpProblem.Failed, "the trash folder cannot be made: " + baseFolder));
            }

            missing.Insert(0, Path.GetFileName(baseFolder));
            baseFolder = parent;
        }

        var trashResolve = TrashResolver(baseFolder);
        var folders = new CreatedFolders(baseFolder, TrashBaseName, trashResolve);
        var finished = false;
        try
        {
            var result = MoveCore(root, projectId, projectFolder, relativeFile, time, hooks, source, baseFolder, [.. missing], trashResolve, folders);
            finished = true;
            if (result.Problem == FileOpProblem.None)
            {
                return result;
            }

            var leftovers = folders.Clean();
            return result with { Detail = result.Detail + Leftovers(leftovers), LeftBehind = result.LeftBehind || leftovers.Count > 0 };
        }
        finally
        {
            if (!finished)
            {
                folders.Clean();   // an unexpected error: the exception goes on
            }
        }
    }

    private static TrashResult MoveCore(string root, Guid projectId, string projectFolder, string relativeFile, TimeProvider time, TrashHooks? hooks,
        string source, string baseFolder, string[] missing, FolderResolver trashResolve, CreatedFolders folders)
    {
        FolderResolver packResolve = relative => PackFolderPaths.ResolveForWrite(projectFolder, relative);
        var projectName = projectId.ToString("D");
        string? entryName = null;
        for (var attempt = 0; attempt < EntryNameAttempts && entryName is null; attempt++)
        {
            var name = NewEntryName(time);
            var candidate = Path.Combine(root, projectName, name);
            if (!Directory.Exists(candidate) && !File.Exists(candidate) && !IsLinkOrUnknown(candidate))
            {
                entryName = name;   // a name taken by something that is not ours is never used
            }
        }

        if (entryName is null)
        {
            return AsTrash(SafeFolders.Failure(FileOpProblem.Failed, "no free name for the trash entry was found"));
        }

        string[] segments = [.. missing, projectName, entryName, Path.GetFileName(source)];
        var targetRelative = string.Join('\\', segments);
        var target = Path.GetFullPath(Path.Combine(baseFolder, targetRelative));
        if (folders.Ensure(segments) is { } folderFailure)
        {
            return AsTrash(folderFailure);
        }

        hooks?.BeforeMove?.Invoke();
        FileOpResult? changed = null;
        bool StillSafe()
        {
            changed = SafeFolders.Reverify(packResolve, relativeFile, source, null) ?? SafeFolders.Reverify(trashResolve, targetRelative, target, "trash");
            return changed is null;
        }

        if (!StillSafe())
        {
            return AsTrash(changed!);
        }

        try
        {
            if (!ProjectTempFiles.MoveWithoutOverwrite(source, target, () => File.Exists(target) || Directory.Exists(target), CancellationToken.None, StillSafe))
            {
                return AsTrash(changed!);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var sourceThere = File.Exists(source);
            var targetThere = File.Exists(target);
            if (!sourceThere && targetThere)
            {
                return Moved(projectId, target, relativeFile);   // the move was done although it reported an error
            }

            if (sourceThere && targetThere && !folders.TryDeleteFile(target))
            {
                return AsTrash(SafeFolders.Failure(FileOpProblem.Locked, $"the file could not be moved: {ex.Message} (a copy is left behind: '{target}')")) with { LeftBehind = true };
            }

            return AsTrash(SafeFolders.Failure(sourceThere ? FileOpProblem.Locked : FileOpProblem.Failed, "the file could not be moved to the trash: " + ex.Message));
        }

        hooks?.AfterMove?.Invoke();
        // A move across drives copies and then deletes, and Windows reports success when the copy worked but the source stayed. Exactly one copy must survive: the source.
        // Only a source that is proven to be a separate, byte-identical file is that case: a different file at the source is another program's new file, and a source that
        // reaches the copy through a link is the copy itself; in both the move stands and nothing is deleted.
        if (CopyDeletedAsHalfMove(source, () => SafeFolders.Reverify(packResolve, relativeFile, source, null) is null, target, () => folders.TryDeleteFile(target)))
        {
            return AsTrash(SafeFolders.Failure(FileOpProblem.Locked,
                "the file was copied to the trash but could not be removed from the project (another program may hold it)"));
        }

        return Moved(projectId, target, relativeFile);
    }

    private static TrashResult Moved(Guid projectId, string target, string relativeFile) =>
        new(FileOpProblem.None, null, new TrashedFile(projectId, target, relativeFile.Replace('/', '\\')));

    /// <summary>
    /// Moves the file back to pack\&lt;<see cref="TrashedFile.RelativeFile"/>&gt; (missing folders, also the pack folder, are created and reported in
    /// <see cref="FileOpResult.CreatedFolders"/>) without overwriting. Before anything is touched the trash file is validated: it must lie exactly at
    /// <c>&lt;trashRoot&gt;\&lt;ProjectId D&gt;\&lt;yyyyMMdd-HHmmss&gt;-&lt;8 hex&gt;\&lt;file name&gt;</c> (a full path, the file name is the file name of
    /// <see cref="TrashedFile.RelativeFile"/>), and no part of it may be a link; otherwise UnsafePath, so a forged <see cref="TrashedFile"/> cannot
    /// pull a foreign file into the project. SourceMissing when the trash file is gone, TargetExists when something is at the old place, Link or
    /// UnsafePath from the target path. Both sides are checked again right before the move and before every retry. After the move the empty
    /// entry folder and, when empty, the folder of the project are removed (the trash root never). When the move fails, the trash file stays where
    /// it is and the created folders are taken back; Locked also when the move reported success while the trash file is still there (a move across
    /// drives whose delete failed) and it is byte-identical to the restored file: the copy at the old place is deleted again (a different trash file stays and the restore stands).
    /// </summary>
    public static FileOpResult Restore(TrashedFile file, string projectFolder, string trashRoot) => Restore(file, projectFolder, trashRoot, null);

    internal static FileOpResult Restore(TrashedFile file, string projectFolder, string trashRoot, TrashHooks? hooks)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(trashRoot);

        if (ValidateRoot(trashRoot, out var root) is { } badRoot)
        {
            return badRoot;
        }

        if (ValidateTrashFile(file, root, out var parts) is { } invalid)
        {
            return invalid;
        }

        var check = PackFolderPaths.ResolveForWrite(projectFolder, file.RelativeFile);
        if (SafeFolders.Refuse(check, null) is { } refused)
        {
            return refused;
        }

        var target = check.FullPath!;
        if (!File.Exists(parts!.File))
        {
            return SafeFolders.Failure(FileOpProblem.SourceMissing, "the file is not in the trash any more: " + file.RelativeFile);
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            return SafeFolders.Failure(FileOpProblem.TargetExists, "a file or a folder is already at the old place: " + file.RelativeFile);
        }

        FolderResolver packResolve = relative => PackFolderPaths.ResolveForWrite(projectFolder, relative);
        var packFolder = Path.GetFullPath(Path.Combine(projectFolder, ProjectFile.PackFolder));
        var folders = new CreatedFolders(packFolder, ProjectFile.PackFolder, packResolve);
        var finished = false;
        try
        {
            var result = RestoreCore(parts, file, projectFolder, packFolder, target, hooks, packResolve, folders);
            finished = true;
            if (result.Problem == FileOpProblem.None)
            {
                return result;
            }

            var leftovers = folders.Clean();
            return result with { Detail = result.Detail + Leftovers(leftovers), LeftBehind = result.LeftBehind || leftovers.Count > 0 };
        }
        finally
        {
            if (!finished)
            {
                folders.Clean();
            }
        }
    }

    private static FileOpResult RestoreCore(TrashParts parts, TrashedFile file, string projectFolder, string packFolder, string target, TrashHooks? hooks,
        FolderResolver packResolve, CreatedFolders folders)
    {
        if (EnsurePackFolder(projectFolder, packFolder, folders) is { } packFailure)
        {
            return packFailure;
        }

        if (folders.Ensure(file.RelativeFile.Split(['\\', '/'])) is { } folderFailure)
        {
            return folderFailure;
        }

        var trashResolve = TrashResolver(parts.Root);
        var trashRelative = Path.GetRelativePath(parts.Root, parts.File);
        hooks?.BeforeMove?.Invoke();
        FileOpResult? changed = null;
        bool StillSafe()
        {
            changed = SafeFolders.Reverify(packResolve, file.RelativeFile, target, null) ?? SafeFolders.Reverify(trashResolve, trashRelative, parts.File, "trash");
            if (changed is null && !File.Exists(parts.File))
            {
                changed = SafeFolders.Failure(FileOpProblem.SourceMissing, "the file is not in the trash any more: " + file.RelativeFile);
            }

            return changed is null;
        }

        if (!StillSafe())
        {
            return changed!;
        }

        try
        {
            if (!ProjectTempFiles.MoveWithoutOverwrite(parts.File, target, () => File.Exists(target) || Directory.Exists(target), CancellationToken.None, StillSafe))
            {
                return changed!;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var sourceThere = File.Exists(parts.File);
            var targetThere = File.Exists(target) || Directory.Exists(target);
            if (sourceThere || !targetThere)
            {
                // The trash file is still where it was. Whatever is at the target is left alone: after an error it cannot be told apart from
                // something that another program put there, so it is never deleted here.
                return targetThere
                    ? SafeFolders.Failure(FileOpProblem.TargetExists, "a file or a folder appeared at the old place, or a copy was left there: " + file.RelativeFile)
                    : SafeFolders.Failure(sourceThere ? FileOpProblem.Locked : FileOpProblem.Failed, "the file could not be restored: " + ex.Message);
            }

            // The trash file is gone and the target is there: the move was done although it reported an error.
        }

        hooks?.AfterMove?.Invoke();
        // The move reported success but the trash file stayed (a move across drives whose delete failed). The target did not exist before and the
        // move never overwrites, so what is there is the copy of this call: it goes, and the trash file stays the only copy. Only when the trash
        // file is proven to be a separate, byte-identical file: a different file there (or one that reaches the copy through a link) is no leftover
        // of this move, and then the restore stands (the file stays in the trash, harmless, and Cleanup removes it later).
        if (CopyDeletedAsHalfMove(parts.File,
                () => SafeFolders.Reverify(trashResolve, trashRelative, parts.File, "trash") is null, target, () => folders.TryDeleteFile(target)))
        {
            return SafeFolders.Failure(FileOpProblem.Locked, "the file was copied back but could not be removed from the trash (another program may hold it)");
        }

        // The file is back; the empty folders of the trash go: the entry folder, then the folder of the project when that is empty too.
        if (TryRemoveEmptyFolder(trashResolve, Path.GetRelativePath(parts.Root, parts.Entry)))
        {
            TryRemoveEmptyFolder(trashResolve, Path.GetRelativePath(parts.Root, parts.ProjectFolder));
        }

        return new FileOpResult(FileOpProblem.None, null, FileFingerprint.TryRead(target), [.. folders.Relative]);
    }

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

    /// <summary>
    /// Deletes the entries of every project in the trash that are older than <paramref name="maxAge"/>, by the time stamp in the folder name
    /// (never by file times); an entry from the future is never older, and a negative <paramref name="maxAge"/> deletes nothing. Looks only at
    /// <c>&lt;trashRoot&gt;\&lt;GUID D&gt;\&lt;entry&gt;</c>: a folder whose name is no GUID, an entry whose name is not exactly
    /// <c>yyyyMMdd-HHmmss-&lt;8 lower case hex&gt;</c> with a valid UTC time, and every link at either level is skipped and never followed. A trash
    /// root that is not a fully qualified path, is a drive root, a <c>\\?\</c> form or a link is not touched.
    /// An entry is deleted by a walk of its own, not by the recursive delete of the framework: right before it, and again before every folder it
    /// enters and every file, link and folder it deletes, the trash root, the folder of the project, the entry and the current folder are checked for
    /// links (and still being folders); a link found inside (to a file or to a
    /// folder) is removed as a link and never entered. Read-only files are not changed: such an entry stays. Any error stops only its entry
    /// (it stays, perhaps partly emptied, and does not count). A project folder is removed afterwards only when this run deleted an entry from
    /// it and it is empty now. The token is checked between entries; cancelling returns the count so far. Returns the number of deleted entries.
    /// Residual window: between such a check and the single file system call after it another process can still swap a folder for a link; the
    /// check narrows that window to one call but cannot close it (the framework offers no delete relative to an open handle). Touches nothing outside
    /// <paramref name="trashRoot"/> otherwise.
    /// </summary>
    public static int Cleanup(string trashRoot, TimeSpan maxAge, TimeProvider time, CancellationToken cancellationToken) =>
        CleanupCore(trashRoot, maxAge, time, cancellationToken, null).Deleted;

    internal static int Cleanup(string trashRoot, TimeSpan maxAge, TimeProvider time, CancellationToken cancellationToken, TrashHooks? hooks) =>
        CleanupCore(trashRoot, maxAge, time, cancellationToken, hooks).Deleted;

    /// <summary>
    /// <see cref="Cleanup(string, TimeSpan, TimeProvider, CancellationToken)"/> with the entries it chose to delete (old enough) but could not
    /// delete, each with a short English reason; such an entry stays, perhaps partly emptied. Entries that are not due or not of the form of an
    /// entry are no failures.
    /// </summary>
    public static TrashCleanupResult CleanupDetailed(string trashRoot, TimeSpan maxAge, TimeProvider time, CancellationToken cancellationToken) =>
        CleanupCore(trashRoot, maxAge, time, cancellationToken, null);

    private static TrashCleanupResult CleanupCore(string trashRoot, TimeSpan maxAge, TimeProvider time, CancellationToken cancellationToken, TrashHooks? hooks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trashRoot);
        ArgumentNullException.ThrowIfNull(time);

        var failed = new List<TrashCleanupFailure>();
        if (maxAge < TimeSpan.Zero || ValidateRoot(trashRoot, out var root) is not null)
        {
            return new TrashCleanupResult(0, failed);
        }

        if (!Directory.Exists(root) || IsLinkOrUnknown(root))
        {
            return new TrashCleanupResult(0, failed);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var deleted = 0;
        foreach (var projectTrash in ListFolders(root))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new TrashCleanupResult(deleted, failed);
            }

            if (!Guid.TryParseExact(Path.GetFileName(projectTrash), "D", out _) || !IsBelow(root, projectTrash) || IsLinkOrUnknown(projectTrash))
            {
                continue;
            }

            var deletedHere = 0;
            foreach (var entry in ListFolders(projectTrash))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new TrashCleanupResult(deleted, failed);
                }

                if (!TryParseEntryName(Path.GetFileName(entry), out var stamp) || stamp > now || now - stamp <= maxAge)
                {
                    continue;
                }

                hooks?.BeforeDeleteEntry?.Invoke(entry);
                if (DeleteEntry(root, projectTrash, entry) is { } reason)
                {
                    failed.Add(new TrashCleanupFailure(entry, reason));
                }
                else
                {
                    deleted++;
                    deletedHere++;
                }
            }

            if (deletedHere > 0 && !IsLinkOrUnknown(root))
            {
                TryRemoveEmptyFolder(TrashResolver(root), Path.GetFileName(projectTrash));
            }
        }

        return new TrashCleanupResult(deleted, failed);
    }

    /// <summary>
    /// Deletes one entry folder with everything in it. The chain trash root, folder of the project and entry is checked right before (a link
    /// may have taken the place of one of them) and again before every folder that is entered and every file that is deleted. Null when the
    /// entry is gone; else why it stays: a part of the chain is a link or gone, or something cannot be deleted.
    /// </summary>
    private static string? DeleteEntry(string root, string projectTrash, string entry)
    {
        bool ChainClean()
        {
            foreach (var folder in new[] { root, projectTrash, entry })
            {
                if (IsLinkOrUnknown(folder) || !Directory.Exists(folder))
                {
                    return false;
                }
            }

            return true;
        }

        return ChainClean() ? DeleteTree(entry, 0, ChainClean) : ChangedWayReason;
    }

    /// <summary>
    /// Deletes a folder and its content without following links: a link inside is removed as a link, a real folder is emptied first, a real
    /// file is deleted (a read-only file fails). The chain above the entry (<paramref name="chainClean"/>) and the folder itself are looked at
    /// for a link right before the folder is entered and right before every file, link and folder is deleted. Null when it is gone; else the
    /// reason of the first doubt or failure, where it stops.
    /// </summary>
    private static string? DeleteTree(string folder, int depth, Func<bool> chainClean)
    {
        bool Safe() => chainClean() && !IsLinkOrUnknown(folder);

        if (depth > MaxEntryDepth)
        {
            return "the folders are nested too deep";
        }

        if (!Safe())
        {
            return ChangedWayReason;
        }

        try
        {
            foreach (var path in Directory.GetFileSystemEntries(folder))
            {
                if (!Safe())
                {
                    return ChangedWayReason;
                }

                var attributes = File.GetAttributes(path);
                var isFolder = attributes.HasFlag(FileAttributes.Directory);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    if (isFolder)
                    {
                        Directory.Delete(path, recursive: false);   // the link itself
                    }
                    else
                    {
                        File.Delete(path);
                    }
                }
                else if (isFolder)
                {
                    if (DeleteTree(path, depth + 1, chainClean) is { } inner)
                    {
                        return inner;
                    }
                }
                else
                {
                    File.Delete(path);
                }
            }

            if (!Safe())
            {
                return ChangedWayReason;
            }

            Directory.Delete(folder, recursive: false);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return ex.Message;
        }
    }

    private static string[] ListFolders(string folder)
    {
        try
        {
            return Directory.GetDirectories(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // ---- the trash path ----

    private sealed record TrashParts(string Root, string ProjectFolder, string Entry, string File);

    /// <summary>
    /// Null (with the full, trimmed path in <paramref name="root"/>) when <paramref name="trashRoot"/> is a fully qualified path that is no
    /// drive root, no share root and no <c>\\?\</c> or <c>\\.\</c> form; otherwise UnsafePath.
    /// </summary>
    private static FileOpResult? ValidateRoot(string trashRoot, out string root)
    {
        root = string.Empty;
        try
        {
            if (!Path.IsPathFullyQualified(trashRoot) || trashRoot.StartsWith(@"\\?\", StringComparison.Ordinal) || trashRoot.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                return SafeFolders.Failure(FileOpProblem.UnsafePath, "the trash folder must be a fully qualified path of an ordinary folder");
            }

            var full = Path.GetFullPath(trashRoot);
            var pathRoot = Path.GetPathRoot(full);
            if (pathRoot is null || Path.TrimEndingDirectorySeparator(full).Length <= Path.TrimEndingDirectorySeparator(pathRoot).Length)
            {
                return SafeFolders.Failure(FileOpProblem.UnsafePath, "the trash folder must not be the root of a drive");
            }

            root = Path.TrimEndingDirectorySeparator(full);
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return SafeFolders.Failure(FileOpProblem.UnsafePath, "the trash folder path is not valid: " + ex.Message);
        }
    }

    /// <summary>
    /// The rules for a path below a trash folder (spelled with '\'): not empty, not rooted, no empty, '.' or '..' segment, lies below the base
    /// folder, and neither the base folder nor any part of the way is a link or cannot be checked (Link; the first one is named).
    /// </summary>
    private static FolderResolver TrashResolver(string baseFolder) => relative =>
    {
        const string unsafeDetail = "the path is not a plain path below the trash folder";
        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative))
        {
            return new PackFolderPathCheck(PackPathProblem.UnsafePath, null, null, unsafeDetail);
        }

        var segments = relative.Split(['\\', '/']);
        if (segments.Any(s => s.Length == 0 || s is "." or ".."))
        {
            return new PackFolderPathCheck(PackPathProblem.UnsafePath, null, null, unsafeDetail);
        }

        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(baseFolder, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new PackFolderPathCheck(PackPathProblem.UnsafePath, null, null, "the trash path cannot be resolved: " + ex.Message);
        }

        if (!IsBelow(baseFolder, full))
        {
            return new PackFolderPathCheck(PackPathProblem.UnsafePath, null, null, unsafeDetail);
        }

        if (IsLinkOrUnknown(baseFolder))
        {
            return new PackFolderPathCheck(PackPathProblem.Link, full, TrashBaseName, "the trash folder is a link or cannot be checked");
        }

        var path = baseFolder;
        var walked = string.Empty;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
            walked = walked.Length == 0 ? segment : walked + "\\" + segment;
            if (IsLinkOrUnknown(path))
            {
                return new PackFolderPathCheck(PackPathProblem.Link, full, walked, $"the trash folder '{walked}' is a link or cannot be checked");
            }
        }

        return new PackFolderPathCheck(PackPathProblem.None, full, null, null);
    };

    /// <summary>
    /// Null (with <paramref name="parts"/> set) when <see cref="TrashedFile.TrashFile"/> lies exactly at
    /// <c>&lt;root&gt;\&lt;ProjectId D&gt;\&lt;entry&gt;\&lt;file name&gt;</c> (the entry folder has the form of an entry, the file name is the file
    /// name of <see cref="TrashedFile.RelativeFile"/>, the way from the root down is no link); otherwise UnsafePath.
    /// </summary>
    private static FileOpResult? ValidateTrashFile(TrashedFile file, string root, out TrashParts? parts)
    {
        parts = null;
        static FileOpResult Unsafe(string detail) => SafeFolders.Failure(FileOpProblem.UnsafePath, detail);

        if (string.IsNullOrEmpty(file.TrashFile) || string.IsNullOrEmpty(file.RelativeFile))
        {
            return Unsafe("the trash file or the original path is empty");
        }

        string full;
        try
        {
            if (!Path.IsPathFullyQualified(file.TrashFile) || file.TrashFile.StartsWith(@"\\?\", StringComparison.Ordinal) || file.TrashFile.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                return Unsafe("the trash file is not a full path of an ordinary file");
            }

            full = Path.GetFullPath(file.TrashFile);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Unsafe("the trash file path cannot be resolved: " + ex.Message);
        }

        if (!IsBelow(root, full))
        {
            return Unsafe("the trash file is not in the trash folder");
        }

        var entry = Path.GetDirectoryName(full);
        var projectFolder = entry is null ? null : Path.GetDirectoryName(entry);
        if (entry is null || projectFolder is null
            || !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(projectFolder) ?? string.Empty), root, StringComparison.OrdinalIgnoreCase))
        {
            return Unsafe("the trash file is not in an entry folder of a project folder of the trash");
        }

        if (!string.Equals(Path.GetFileName(projectFolder), file.ProjectId.ToString("D"), StringComparison.Ordinal))
        {
            return Unsafe("the trash file is not in the trash folder of its project");
        }

        if (!IsEntryName(Path.GetFileName(entry)))
        {
            return Unsafe("the folder of the trash file is not a trash entry");
        }

        var originalName = file.RelativeFile.Split(['\\', '/'])[^1];
        if (!string.Equals(Path.GetFileName(full), originalName, StringComparison.Ordinal))
        {
            return Unsafe("the name of the trash file differs from the name of the original file");
        }

        var resolved = TrashResolver(Path.GetDirectoryName(projectFolder)!)(Path.GetRelativePath(Path.GetDirectoryName(projectFolder)!, full));
        if (resolved.Problem != PackPathProblem.None || IsLinkOrUnknown(Path.GetDirectoryName(projectFolder)!))
        {
            return Unsafe("a part of the trash path is a link or cannot be checked: " + (resolved.Detail ?? full));
        }

        parts = new TrashParts(Path.GetDirectoryName(projectFolder)!, projectFolder, entry, full);
        return null;
    }

    /// <summary>
    /// After a move that reported success: true only when the file that stayed at <paramref name="survivor"/> is proven to be a separate file with
    /// the same bytes as <paramref name="copy"/>, and <paramref name="deleteCopy"/> then deleted the copy. Every doubt means false and nothing is
    /// deleted (the move stands): the survivor is gone, has the same full path as the copy, its way is no longer clean or it is a link itself
    /// (<paramref name="survivorIsClean"/> says so; it is asked again after the file was opened), it cannot be read, or the content differs. The
    /// survivor is held open without sharing delete while the content is compared and the copy is deleted: if both paths led to the same file
    /// through a link, the delete of the copy fails with a sharing violation and nothing is lost. (Two hard links are two names of one file: the
    /// delete of the copy name succeeds and the data stays under the survivor name, which is a correct roll back.)
    /// </summary>
    private static bool CopyDeletedAsHalfMove(string survivor, Func<bool> survivorIsClean, string copy, Func<bool> deleteCopy)
    {
        try
        {
            if (!File.Exists(survivor)
                || string.Equals(Path.GetFullPath(survivor), Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase)
                || !survivorIsClean() || IsLinkOrUnknown(survivor))
            {
                return false;
            }

            using var held = new FileStream(survivor, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!survivorIsClean() || IsLinkOrUnknown(survivor))
            {
                return false;
            }

            bool same;
            using (var other = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                same = SameContent(held, other);
            }

            return same && deleteCopy();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// True only when both streams have the same length, end at the same time and carry the same bytes (compared in blocks). A stream that
    /// shrinks or grows while it is compared, a read error and anything else unexpected count as "not identical".
    /// </summary>
    internal static bool SameContent(Stream a, Stream b)
    {
        try
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            var bufferA = new byte[81920];
            var bufferB = new byte[81920];
            while (true)
            {
                var readA = ReadBlock(a, bufferA);
                var readB = ReadBlock(b, bufferB);
                if (readA != readB)
                {
                    return false;   // one stream ended before the other
                }

                if (readA == 0)
                {
                    return true;
                }

                if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                {
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Reads until the buffer is full or the stream ends; returns the number of bytes read.</summary>
    private static int ReadBlock(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static string NewEntryName(TimeProvider time) =>
        time.GetUtcNow().UtcDateTime.ToString(EntryDateFormat, CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>True for exactly <c>yyyyMMdd-HHmmss-xxxxxxxx</c> with ASCII digits and lower case hex (no regular expression: no locale, no line end).</summary>
    private static bool IsEntryName(string name)
    {
        if (name.Length != EntryNameLength)
        {
            return false;
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var ok = i switch
            {
                8 or 15 => c == '-',
                < 15 => c is >= '0' and <= '9',
                _ => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'),
            };
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when the name has the form of an entry and carries a valid UTC time (exact format, invariant culture).</summary>
    private static bool TryParseEntryName(string name, out DateTime stampUtc)
    {
        stampUtc = default;
        return IsEntryName(name) && DateTime.TryParseExact(name[..15], EntryDateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out stampUtc);
    }

    // ---- links and folders ----

    private static TrashResult AsTrash(FileOpResult result) => new(result.Problem, result.Detail, null) { LeftBehind = result.LeftBehind };

    private static string Leftovers(IReadOnlyList<string> leftovers) =>
        leftovers.Count == 0 ? string.Empty : " (left behind: " + string.Join(", ", leftovers) + ")";

    /// <summary>
    /// True when the path is a link (reparse point) or cannot be asked. False for a path that does not exist. Stricter than
    /// <see cref="ProjectFiles.IsLink"/>, which takes an unreadable path for "no link": nothing in the trash is used on a guess.
    /// </summary>
    private static bool IsLinkOrUnknown(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return true;
        }
    }

    /// <summary>True when the full path <paramref name="path"/> lies below <paramref name="root"/>, on a folder boundary, ignoring case.</summary>
    internal static bool IsBelow(string root, string path)
    {
        // Exactly one separator ends the prefix, also for a root such as "D:\" that already has one.
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Removes a folder below the resolver's base when the way to it is clean and it is empty. True when it is gone afterwards.</summary>
    private static bool TryRemoveEmptyFolder(FolderResolver resolve, string relative)
    {
        var check = resolve(relative);
        if (check.Problem != PackPathProblem.None)
        {
            return false;
        }

        try
        {
            Directory.Delete(check.FullPath!, recursive: false);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;   // not empty, or in use
        }
    }
}
