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
public sealed record TrashResult(FileOpProblem Problem, string? Detail, TrashedFile? File);

/// <summary>
/// The trash of the tool for files deleted from the pack folder of a project: <c>&lt;trash root&gt;\&lt;project id D&gt;\&lt;yyyyMMdd-HHmmss&gt;-&lt;8 hex&gt;\&lt;file name&gt;</c>
/// (the time is the UTC time of the delete; the 8 hex characters keep two deletes of the same second apart). The trash root lies outside
/// every project and is the only place besides the pack folders where the tool writes: everything this class creates, moves or deletes
/// lies below <c>&lt;trash root&gt;\&lt;project id D&gt;\&lt;entry&gt;\</c>, and every part of that path is built from the project id and a
/// generated name, never from caller text. Nothing happens through a link (junction or symbolic link): the trash root, the folder of the
/// project and the entry folder are looked at before they are used, and again right before a file is moved. Nothing is overwritten.
/// Never throws for the file system; only argument errors are thrown. A call that does not succeed leaves the file where it was and takes
/// back the folders it created (innermost first, only when empty); what could not be taken back is named in the Detail.
/// </summary>
public static class ProjectTrash
{
    private const string EntryDateFormat = "yyyyMMdd-HHmmss";
    private const int EntryNameLength = 24;   // yyyyMMdd-HHmmss-xxxxxxxx
    private const int EntryNameAttempts = 5;
    private const int MaxEntryDepth = 64;

    /// <summary>
    /// Moves pack\&lt;relativeFile&gt; to <c>&lt;trashRoot&gt;\&lt;projectId D&gt;\&lt;yyyyMMdd-HHmmss&gt;-&lt;8 hex&gt;\&lt;file name&gt;</c> without
    /// overwriting; the trash root and the folder of the project are created when missing. The source goes through
    /// <see cref="PackFolderPaths.ResolveForWrite"/> (UnsafePath, Link) and is checked again right before the move. SourceMissing when it is not a
    /// file (a folder is never moved). A sharing violation of the move is retried briefly; Locked when the move then fails while the source is
    /// still there. Link when the trash root, the folder of the project or the entry folder is a link. When the move fails, the folders created
    /// for it are removed again, and a copy that a move across drives left in the trash is deleted so that exactly one copy survives.
    /// </summary>
    public static TrashResult MoveToTrash(string trashRoot, Guid projectId, string projectFolder, string relativeFile, TimeProvider time) =>
        MoveToTrash(trashRoot, projectId, projectFolder, relativeFile, time, null);

    internal static TrashResult MoveToTrash(string trashRoot, Guid projectId, string projectFolder, string relativeFile, TimeProvider time,
        Action? beforeMove)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trashRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(relativeFile);
        ArgumentNullException.ThrowIfNull(time);

        var check = PackFolderPaths.ResolveForWrite(projectFolder, relativeFile);
        if (Refuse(check) is { } refused)
        {
            return Failure(refused);
        }

        var source = check.FullPath!;
        if (!File.Exists(source))
        {
            return Failure(FileOpProblem.SourceMissing, "the file to delete does not exist: " + relativeFile);
        }

        string root;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trashRoot));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(FileOpProblem.Failed, "the trash folder path is not valid: " + ex.Message);
        }

        var made = new MadeFolders();
        var finished = false;
        try
        {
            var result = MoveCore(root, projectId, projectFolder, relativeFile, time, beforeMove, source, made);
            finished = true;
            return result.Problem == FileOpProblem.None ? result : result with { Detail = result.Detail + Leftovers(made.Clean()) };
        }
        finally
        {
            if (!finished)
            {
                made.Clean();   // an unexpected error: the exception goes on
            }
        }
    }

    private static TrashResult MoveCore(string root, Guid projectId, string projectFolder, string relativeFile, TimeProvider time, Action? beforeMove,
        string source, MadeFolders made)
    {
        if (EnsureFolder(root, "the trash folder", FileOpProblem.Failed, null) is { } rootFailure)
        {
            return Failure(rootFailure);
        }

        var projectName = projectId.ToString("D");
        var projectTrash = Path.Combine(root, projectName);
        if (EnsureFolder(projectTrash, projectName, FileOpProblem.Failed, made) is { } projectFailure)
        {
            return Failure(projectFailure);
        }

        string? entry = null;
        for (var attempt = 0; attempt < EntryNameAttempts && entry is null; attempt++)
        {
            var name = NewEntryName(time);
            var candidate = Path.Combine(projectTrash, name);
            if (Directory.Exists(candidate) || File.Exists(candidate) || IsLinkOrUnknown(candidate))
            {
                continue;   // taken by something that is not ours: never used
            }

            if (EnsureFolder(candidate, projectName + "\\" + name, FileOpProblem.Failed, made) is { } entryFailure)
            {
                return Failure(entryFailure);
            }

            entry = candidate;
        }

        if (entry is null)
        {
            return Failure(FileOpProblem.Failed, "no free name for the trash entry was found");
        }

        var target = Path.Combine(entry, Path.GetFileName(source));
        beforeMove?.Invoke();
        if (RecheckSource(projectFolder, relativeFile, source) is { } changed)
        {
            return Failure(changed);
        }

        foreach (var part in new[] { root, projectTrash, entry })
        {
            if (IsLinkOrUnknown(part))
            {
                return Failure(FileOpProblem.Link, "the trash folder changed while the operation ran: a folder is a link now");
            }
        }

        try
        {
            ProjectTempFiles.MoveWithoutOverwrite(source, target, () => File.Exists(target) || Directory.Exists(target), CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var sourceThere = File.Exists(source);
            var targetThere = File.Exists(target);
            if (!sourceThere && targetThere)
            {
                return Moved(projectId, target, relativeFile);   // the move was done although it reported an error
            }

            if (sourceThere && targetThere && !TryDeleteFile(target, entry))
            {
                // A move across drives copies and then deletes: the copy is here, the source could not be deleted, and the copy could not be taken back.
                return Failure(FileOpProblem.Locked, $"the file could not be moved: {ex.Message} (a copy is left behind: '{target}')");
            }

            return Failure(sourceThere ? FileOpProblem.Locked : FileOpProblem.Failed, "the file could not be moved to the trash: " + ex.Message);
        }

        return Moved(projectId, target, relativeFile);
    }

    private static TrashResult Moved(Guid projectId, string target, string relativeFile) =>
        new(FileOpProblem.None, null, new TrashedFile(projectId, target, relativeFile.Replace('/', '\\')));

    /// <summary>
    /// Moves the file back to pack\&lt;<see cref="TrashedFile.RelativeFile"/>&gt; (missing folders, also the pack folder, are created and reported in
    /// <see cref="FileOpResult.CreatedFolders"/>) without overwriting. Before anything is touched the trash file is validated by its form: a full path
    /// <c>&lt;root&gt;\&lt;ProjectId D&gt;\&lt;yyyyMMdd-HHmmss&gt;-&lt;8 hex&gt;\&lt;file name&gt;</c> whose file name is the file name of
    /// <see cref="TrashedFile.RelativeFile"/>, and no part of it a link; otherwise UnsafePath. This form alone cannot tell the real trash from
    /// another folder of the same shape: the App uses <see cref="Restore(TrashedFile, string, string)"/>, which also requires the root.
    /// SourceMissing when the trash file is gone, TargetExists when something is at the old place, Link or UnsafePath from the target path.
    /// After the move the empty entry folder and, when empty, the folder of the project are removed; when the move fails, the trash file stays
    /// where it is and the created folders are taken back.
    /// </summary>
    public static FileOpResult Restore(TrashedFile file, string projectFolder) => Restore(file, projectFolder, null, null);

    /// <summary>
    /// Like <see cref="Restore(TrashedFile, string)"/>, and in addition the trash file must lie exactly at
    /// <c>&lt;trashRoot&gt;\&lt;ProjectId D&gt;\&lt;entry&gt;\&lt;file name&gt;</c> (UnsafePath otherwise). This is the overload for the App.
    /// </summary>
    public static FileOpResult Restore(TrashedFile file, string projectFolder, string trashRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trashRoot);
        return Restore(file, projectFolder, trashRoot, null);
    }

    internal static FileOpResult Restore(TrashedFile file, string projectFolder, string? trashRoot, Action? beforeMove)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        var parts = ValidateTrashFile(file, trashRoot, out var invalid);
        if (parts is null)
        {
            return OpFailure(invalid!.Value.Problem, invalid.Value.Detail);
        }

        var check = PackFolderPaths.ResolveForWrite(projectFolder, file.RelativeFile);
        if (Refuse(check) is { } refused)
        {
            return OpFailure(refused.Problem, refused.Detail);
        }

        var target = check.FullPath!;
        if (!File.Exists(parts.File))
        {
            return OpFailure(FileOpProblem.SourceMissing, "the file is not in the trash any more: " + file.RelativeFile);
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            return OpFailure(FileOpProblem.TargetExists, "a file or a folder is already at the old place: " + file.RelativeFile);
        }

        var made = new MadeFolders();
        var finished = false;
        try
        {
            var result = RestoreCore(parts, file, projectFolder, target, beforeMove, made);
            finished = true;
            return result.Problem == FileOpProblem.None ? result : result with { Detail = result.Detail + Leftovers(made.Clean()) };
        }
        finally
        {
            if (!finished)
            {
                made.Clean();
            }
        }
    }

    private static FileOpResult RestoreCore(TrashParts parts, TrashedFile file, string projectFolder, string target, Action? beforeMove, MadeFolders made)
    {
        if (!Directory.Exists(projectFolder))
        {
            return OpFailure(FileOpProblem.Failed, "the project folder does not exist");
        }

        var path = Path.GetFullPath(projectFolder);
        path = Path.Combine(path, ProjectFile.PackFolder);
        if (EnsureFolder(path, ProjectFile.PackFolder, FileOpProblem.Failed, null) is { } packFailure)
        {
            return OpFailure(packFailure.Problem, packFailure.Detail);
        }

        var segments = file.RelativeFile.Split(['\\', '/']);
        var relative = string.Empty;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            path = Path.Combine(path, segments[i]);
            relative = relative.Length == 0 ? segments[i] : relative + "\\" + segments[i];
            if (EnsureFolder(path, relative, FileOpProblem.TargetExists, made) is { } folderFailure)
            {
                return OpFailure(folderFailure.Problem, folderFailure.Detail);
            }
        }

        beforeMove?.Invoke();
        if (RecheckSource(projectFolder, file.RelativeFile, target) is { } changed)
        {
            return OpFailure(changed.Problem, changed.Detail);
        }

        foreach (var part in new[] { parts.Root, parts.ProjectFolder, parts.Entry, parts.File })
        {
            if (IsLinkOrUnknown(part))
            {
                return OpFailure(FileOpProblem.UnsafePath, "the trash changed while the operation ran: a part of the trash path is a link now");
            }
        }

        if (!File.Exists(parts.File))
        {
            return OpFailure(FileOpProblem.SourceMissing, "the file is not in the trash any more: " + file.RelativeFile);
        }

        try
        {
            ProjectTempFiles.MoveWithoutOverwrite(parts.File, target, () => File.Exists(target) || Directory.Exists(target), CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var sourceThere = File.Exists(parts.File);
            var targetThere = File.Exists(target) || Directory.Exists(target);
            if (sourceThere || !targetThere)
            {
                // The trash file is still where it was. A file at the target is left alone: it cannot be told apart from one that another
                // program put there, so it is never deleted here.
                return targetThere
                    ? OpFailure(FileOpProblem.TargetExists, "a file or a folder appeared at the old place, or a copy was left there: " + file.RelativeFile)
                    : OpFailure(sourceThere ? FileOpProblem.Locked : FileOpProblem.Failed, "the file could not be restored: " + ex.Message);
            }

            // The trash file is gone and the target is there: the move was done although it reported an error.
        }

        // The file is back; the empty folders of the trash go. The folder of the project is removed only when this made it empty, and the
        // trash root never.
        if (TryRemoveEmptyFolder(parts.Entry))
        {
            TryRemoveEmptyFolder(parts.ProjectFolder);
        }

        return new FileOpResult(FileOpProblem.None, null, FileFingerprint.TryRead(target), [.. made.Shown]);
    }

    /// <summary>
    /// Deletes the entries of every project in the trash that are older than <paramref name="maxAge"/>, by the time stamp in the folder name
    /// (never by file times); an entry from the future is never older, and a negative <paramref name="maxAge"/> deletes nothing. Looks only at
    /// <c>&lt;trashRoot&gt;\&lt;GUID D&gt;\&lt;entry&gt;</c>: a folder whose name is no GUID, an entry whose name is not exactly
    /// <c>yyyyMMdd-HHmmss-&lt;8 lower case hex&gt;</c> with a valid UTC time, and every link at either level is skipped and never followed.
    /// A read-only file inside an entry does not stop the clean-up; an error costs only its entry. Afterwards empty project folders are removed.
    /// The token is checked between entries; cancelling returns the count so far. Returns the number of deleted entries. Touches nothing outside
    /// <paramref name="trashRoot"/>.
    /// </summary>
    public static int Cleanup(string trashRoot, TimeSpan maxAge, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trashRoot);
        ArgumentNullException.ThrowIfNull(time);

        if (maxAge < TimeSpan.Zero)
        {
            return 0;
        }

        string root;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trashRoot));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return 0;
        }

        if (!Directory.Exists(root) || IsLinkOrUnknown(root))
        {
            return 0;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var deleted = 0;
        foreach (var projectTrash in ListFolders(root))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return deleted;
            }

            if (!Guid.TryParseExact(Path.GetFileName(projectTrash), "D", out _) || !IsBelow(root, projectTrash) || IsLinkOrUnknown(projectTrash))
            {
                continue;
            }

            foreach (var entry in ListFolders(projectTrash))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return deleted;
                }

                if (!TryParseEntryName(Path.GetFileName(entry), out var stamp) || stamp > now || now - stamp <= maxAge)
                {
                    continue;
                }

                if (TryDeleteEntry(root, entry))
                {
                    deleted++;
                }
            }

            TryRemoveEmptyFolder(projectTrash);
        }

        return deleted;
    }

    /// <summary>Deletes one entry folder with everything in it. False (and the entry stays, at least in part) when it is a link, lies outside the root or cannot be deleted.</summary>
    private static bool TryDeleteEntry(string root, string entry)
    {
        try
        {
            if (!IsBelow(root, Path.GetFullPath(entry)) || IsLinkOrUnknown(entry))
            {
                return false;
            }

            ClearReadOnly(entry, 0);
            if (IsLinkOrUnknown(entry))
            {
                return false;   // a link may have taken its place meanwhile
            }

            Directory.Delete(entry, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Clears the read-only attribute of the files below a folder; links are neither entered nor changed. Throws when something cannot be done (the entry is then skipped).</summary>
    private static void ClearReadOnly(string folder, int depth)
    {
        if (depth > MaxEntryDepth)
        {
            throw new IOException("the folder is nested too deeply");
        }

        foreach (var path in Directory.GetFileSystemEntries(folder))
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            if (attributes.HasFlag(FileAttributes.Directory))
            {
                ClearReadOnly(path, depth + 1);
            }
            else if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
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
    /// Null (with <paramref name="invalid"/> set) unless <see cref="TrashedFile.TrashFile"/> has the form of a file in the trash of
    /// <see cref="TrashedFile.ProjectId"/> (and lies in <paramref name="trashRoot"/> when that is given) and no part of it is a link.
    /// </summary>
    private static TrashParts? ValidateTrashFile(TrashedFile file, string? trashRoot, out Refusal? invalid)
    {
        invalid = null;
        if (string.IsNullOrEmpty(file.TrashFile) || string.IsNullOrEmpty(file.RelativeFile))
        {
            invalid = new Refusal(FileOpProblem.UnsafePath, "the trash file or the original path is empty");
            return null;
        }

        string full;
        try
        {
            if (!Path.IsPathFullyQualified(file.TrashFile) || file.TrashFile.StartsWith(@"\\?\", StringComparison.Ordinal) || file.TrashFile.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                invalid = new Refusal(FileOpProblem.UnsafePath, "the trash file is not a full path of an ordinary file");
                return null;
            }

            full = Path.GetFullPath(file.TrashFile);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            invalid = new Refusal(FileOpProblem.UnsafePath, "the trash file path cannot be resolved: " + ex.Message);
            return null;
        }

        var entry = Path.GetDirectoryName(full);
        var projectFolder = entry is null ? null : Path.GetDirectoryName(entry);
        var root = projectFolder is null ? null : Path.GetDirectoryName(projectFolder);
        if (entry is null || projectFolder is null || root is null)
        {
            invalid = new Refusal(FileOpProblem.UnsafePath, "the trash file is not in an entry folder of a project folder");
            return null;
        }

        if (!string.Equals(Path.GetFileName(projectFolder), file.ProjectId.ToString("D"), StringComparison.Ordinal))
        {
            invalid = new Refusal(FileOpProblem.UnsafePath, "the trash file is not in the trash folder of its project");
            return null;
        }

        if (!IsEntryName(Path.GetFileName(entry)))
        {
            invalid = new Refusal(FileOpProblem.UnsafePath, "the folder of the trash file is not a trash entry");
            return null;
        }

        var originalName = file.RelativeFile.Split(['\\', '/'])[^1];
        if (!string.Equals(Path.GetFileName(full), originalName, StringComparison.Ordinal))
        {
            invalid = new Refusal(FileOpProblem.UnsafePath, "the name of the trash file differs from the name of the original file");
            return null;
        }

        if (trashRoot is not null)
        {
            string expected;
            try
            {
                expected = Path.GetFullPath(trashRoot);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                invalid = new Refusal(FileOpProblem.UnsafePath, "the trash folder path is not valid: " + ex.Message);
                return null;
            }

            if (!string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(expected), StringComparison.OrdinalIgnoreCase))
            {
                invalid = new Refusal(FileOpProblem.UnsafePath, "the trash file is not in the trash folder");
                return null;
            }
        }

        foreach (var part in new[] { root, projectFolder, entry, full })
        {
            if (IsLinkOrUnknown(part))
            {
                invalid = new Refusal(FileOpProblem.UnsafePath, "a part of the trash path is a link or cannot be checked: " + part);
                return null;
            }
        }

        return new TrashParts(root, projectFolder, entry, full);
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

    // ---- folders and links ----

    private readonly record struct Refusal(FileOpProblem Problem, string Detail);

    /// <summary>The refusal for a path that <see cref="PackFolderPaths.ResolveForWrite"/> did not accept; null when it is fine.</summary>
    private static Refusal? Refuse(PackFolderPathCheck check) => check.Problem switch
    {
        PackPathProblem.UnsafePath => new Refusal(FileOpProblem.UnsafePath, check.Detail ?? "the path is not a plain file path below the pack folder"),
        PackPathProblem.Link => new Refusal(FileOpProblem.Link, check.Detail ?? $"links are not supported: the path passes through the link '{check.LinkPath}'"),
        _ => null,
    };

    /// <summary>The path is resolved again right before the disk is changed: a link may have appeared since. Null when it still resolves to the same place without a link.</summary>
    private static Refusal? RecheckSource(string projectFolder, string relativeFile, string expectedFullPath)
    {
        var check = PackFolderPaths.ResolveForWrite(projectFolder, relativeFile);
        if (check.Problem == PackPathProblem.None)
        {
            return string.Equals(check.FullPath, expectedFullPath, StringComparison.Ordinal)
                ? null
                : new Refusal(FileOpProblem.UnsafePath, "the path resolves to another place than before: " + relativeFile);
        }

        var refusal = Refuse(check)!.Value;
        return refusal with { Detail = "the path changed while the operation ran: " + refusal.Detail };
    }

    /// <summary>
    /// Makes sure the folder exists and is no link; creates it when missing (for the trash root also the folders above it). A folder is
    /// recorded in <paramref name="made"/> only when it did not exist right before it was created and is a real folder right after.
    /// </summary>
    private static Refusal? EnsureFolder(string path, string shown, FileOpProblem fileInTheWay, MadeFolders? made)
    {
        if (Directory.Exists(path))
        {
            return IsLinkOrUnknown(path) ? LinkRefusal(shown) : null;
        }

        if (File.Exists(path))
        {
            return new Refusal(fileInTheWay, $"a file is in the place of the folder '{shown}'");
        }

        if (IsLinkOrUnknown(path))
        {
            return LinkRefusal(shown);   // a link whose target is gone
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new Refusal(FileOpProblem.Failed, $"the folder '{shown}' could not be created: " + ex.Message);
        }

        // CreateDirectory succeeds silently on a folder that is already there, so a link that appeared in that moment shows only here.
        if (IsLinkOrUnknown(path))
        {
            return LinkRefusal(shown);
        }

        if (!Directory.Exists(path))
        {
            return new Refusal(FileOpProblem.Failed, $"the folder '{shown}' is not there after it was created");
        }

        made?.Add(path, shown);
        return null;
    }

    private static Refusal LinkRefusal(string shown) => new(FileOpProblem.Link, $"links are not supported: the folder '{shown}' is a link");

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
    private static bool IsBelow(string root, string path)
    {
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Removes a folder when it is empty and no link. True when it is gone afterwards.</summary>
    private static bool TryRemoveEmptyFolder(string path)
    {
        if (IsLinkOrUnknown(path))
        {
            return false;
        }

        try
        {
            Directory.Delete(path, recursive: false);
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

    /// <summary>Deletes a file this call put into a folder it created. False when it is still there afterwards.</summary>
    private static bool TryDeleteFile(string file, string folder)
    {
        if (IsLinkOrUnknown(folder) || IsLinkOrUnknown(file))
        {
            return false;
        }

        try
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
            return !File.Exists(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return !File.Exists(file);
        }
    }

    private static TrashResult Failure(Refusal refusal) => Failure(refusal.Problem, refusal.Detail);

    private static TrashResult Failure(FileOpProblem problem, string detail) => new(problem, detail, null);

    private static string Leftovers(IReadOnlyList<string> leftovers) =>
        leftovers.Count == 0 ? string.Empty : " (left behind: " + string.Join(", ", leftovers) + ")";

    /// <summary>The folders a call has made so far and has to take back when it does not succeed (outermost first).</summary>
    private sealed class MadeFolders
    {
        private readonly List<string> _full = [];

        /// <summary>The folders as shown to the caller (relative names), in the order they were made.</summary>
        public List<string> Shown { get; } = [];

        public void Add(string full, string shown)
        {
            _full.Add(full);
            Shown.Add(shown);
        }

        /// <summary>
        /// Removes the made folders, innermost first (non-recursive: a folder with content stays; a link that took the place of a folder is
        /// not removed). Never throws. Returns what could not be taken back.
        /// </summary>
        public List<string> Clean()
        {
            var leftovers = new List<string>();
            for (var i = _full.Count - 1; i >= 0; i--)
            {
                if (!TryRemoveEmptyFolder(_full[i]))
                {
                    for (var outer = i; outer >= 0; outer--)
                    {
                        leftovers.Add("folder '" + Shown[outer] + "'");
                    }

                    break;
                }
            }

            _full.Clear();
            Shown.Clear();
            return leftovers;
        }
    }

    private static FileOpResult OpFailure(FileOpProblem problem, string? detail) =>
        new(problem, detail, null, []);
}
