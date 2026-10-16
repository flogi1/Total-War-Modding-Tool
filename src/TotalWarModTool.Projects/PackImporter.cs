using TotalWarModTool.Operations;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Projects;

/// <summary>
/// Imports a pack into a new project folder. Everything is built in a staging folder next to the target and moved into place in one
/// step, so a failed or cancelled import leaves the target as it was. The source pack is only read.
/// </summary>
public static class PackImporter
{
    /// <summary>1 GiB; larger entries are skipped and reported.</summary>
    public const long MaxEntryBytes = 1_073_741_824;

    private const int MaxPathLength = 259;
    private const int CleanupAttempts = 3;
    private const int MoveAttempts = 5;
    private const int MoveRetryDelayMs = 100;
    private const int ErrorFileExists = 80;
    private const int ErrorAlreadyExists = 183;
    private const string LocExtension = ".loc";

    /// <summary>
    /// Counts what an import would write, by path only. Nothing is read from the data section of the pack. The file name on disk of a
    /// table or loc file is only known after the conversion, so the import can skip a few more entries than the preview lists.
    /// </summary>
    public static ImportPreview Preview(PackIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        var entries = index.Entries;
        var reasons = ComputeSkips(entries);
        int tables = 0, locFiles = 0, otherFiles = 0;
        long storedBytes = 0;
        var skipped = new List<SkippedEntry>();

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (reasons[i] is { } reason)
            {
                skipped.Add(new SkippedEntry(entry.Path, reason));
                continue;
            }

            if (DbTablePath.TryGetTableName(entry.Path, out _))
            {
                tables++;
            }
            else if (entry.Path.EndsWith(LocExtension, StringComparison.OrdinalIgnoreCase))
            {
                locFiles++;
            }
            else
            {
                otherFiles++;
            }

            storedBytes += entry.StoredSize;
        }

        return new ImportPreview(tables, locFiles, otherFiles, storedBytes, skipped);
    }

    /// <summary>
    /// Writes the project folder for <paramref name="request"/>. Problems are in the report; only <see cref="OperationCanceledException"/>
    /// and argument errors are thrown (a relative <c>SourcePack</c> or <c>TargetFolder</c> is an argument error). <paramref name="schema"/>
    /// may be null: tables then stay binary. The staging folder is removed in every case; a problem report names it in <c>Detail</c> when
    /// it could not be removed. A cancel or another exception has no such channel, so the caller must not assume that the staging folder
    /// (<c>&lt;TargetFolder&gt;.import-&lt;8 hex characters&gt;</c>) is gone when a delete can fail.
    /// </summary>
    public static ImportReport Import(ImportRequest request, GameSchema? schema, GameTableVersions gameVersions,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        return Import(request, schema, gameVersions, progress, cancellationToken, null);
    }

    internal static ImportReport Import(ImportRequest request, GameSchema? schema, GameTableVersions gameVersions,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken, Action? beforeMove)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(gameVersions);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePack);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetFolder);
        if (!Path.IsPathFullyQualified(request.SourcePack) || !Path.IsPathFullyQualified(request.TargetFolder))
        {
            throw new ArgumentException("The source pack and the target folder must be full paths.", nameof(request));
        }

        ArgumentNullException.ThrowIfNull(request.PackName);
        ArgumentNullException.ThrowIfNull(request.VanillaPackNames);
        if (string.IsNullOrWhiteSpace(request.ProjectName) || !PackNameRules.IsWellFormed(request.ProjectName))
        {
            throw new ArgumentException("The project name is empty or not valid Unicode.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.GameId) || !PackNameRules.IsWellFormed(request.GameId))
        {
            throw new ArgumentException("The game id is empty or not valid Unicode.", nameof(request));
        }

        if (PackNameRules.Check(request.PackName, request.VanillaPackNames) is { } nameReason)
        {
            return Fail(ImportProblem.InvalidPackName, nameReason);
        }

        var read = PackReader.ReadIndex(request.SourcePack);
        if (read.Index is not { } index)
        {
            return Fail(ImportProblem.SourceUnreadable, Describe(read.Problem.ToString(), read.Detail));
        }

        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.TargetFolder));
        var parent = Path.GetDirectoryName(target);
        if (parent is null)
        {
            return Fail(ImportProblem.TargetUnwritable, "The target folder has no parent folder.");
        }

        bool targetExisted;
        try
        {
            if (!Directory.Exists(parent))
            {
                return Fail(ImportProblem.TargetUnwritable, "The parent folder of the target does not exist.");
            }

            if (File.Exists(target))
            {
                return Fail(ImportProblem.TargetNotEmpty, "The target is a file.");
            }

            targetExisted = Directory.Exists(target);
            if (targetExisted && Directory.EnumerateFileSystemEntries(target).Any())
            {
                return Fail(ImportProblem.TargetNotEmpty, "The target folder is not empty.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(ImportProblem.TargetUnwritable, "The target folder cannot be read: " + ex.Message);
        }

        if (OutdatedTables(index, gameVersions, cancellationToken) is { } outdated)
        {
            return Fail(ImportProblem.OutdatedTables, outdated);
        }

        var staging = NewStagingPath(target);
        var writing = new WriteState();
        ImportReport? report = null;
        var stagingRemoved = true;
        try
        {
            report = WriteProject(request, schema, index, target, targetExisted, staging, progress, cancellationToken, beforeMove, writing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var entry = writing.CurrentEntry is { } path ? " while writing the entry '" + path + "'" : string.Empty;
            report = Fail(ImportProblem.TargetUnwritable, "The project folder cannot be written" + entry + ": " + ex.Message);
        }
        finally
        {
            // Also runs for a cancel or another exception (report is still null then).
            if (report?.Problem != ImportProblem.None)
            {
                stagingRemoved = RemoveStaging(staging);
            }
        }

        return stagingRemoved
            ? report
            : report with { Detail = Describe(report.Problem.ToString(), report.Detail) + $" The staging folder '{staging}' could not be removed." };
    }

    // One line per table entry in another version than the game uses ("<pack path>: <detail>"); null when there is none. Runs before anything is written.
    private static string? OutdatedTables(PackIndex index, GameTableVersions gameVersions, CancellationToken cancellationToken)
    {
        if (gameVersions.Versions.Count == 0)
        {
            return null;
        }

        var entries = index.Entries;
        var reasons = ComputeSkips(entries);
        var lines = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];
            if (reasons[i] is not null || !DbTablePath.TryGetTableName(entry.Path, out var tableName) || gameVersions.VersionOf(tableName) is null)
            {
                continue;
            }

            var prefix = PackReader.ReadEntryPrefix(index, entry, DbTableDecoder.TableHeaderBytes, cancellationToken);
            if (prefix.Bytes is not null
                && DbTableDecoder.ReadHeader(prefix.Bytes) is { } header
                && TableVersionRule.VersionMismatchDetail(gameVersions, tableName, header.Version) is { } detail)
            {
                lines.Add(entry.Path + ": " + detail);
            }
        }

        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    private static ImportReport WriteProject(
        ImportRequest request,
        GameSchema? schema,
        PackIndex index,
        string target,
        bool targetExisted,
        string staging,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken,
        Action? beforeMove,
        WriteState writing)
    {
        var entries = index.Entries;
        var reasons = ComputeSkips(entries);
        var packRoot = Path.Combine(staging, ProjectFile.PackFolder);
        var packRootPrefix = Path.GetFullPath(packRoot) + Path.DirectorySeparatorChar;
        var targetPackRoot = Path.Combine(target, ProjectFile.PackFolder);
        var skipped = new List<SkippedEntry>();
        var stayedBinary = new List<BinaryTable>();

        // The files written so far and their folders (relative to the pack folder, '/'), to find clashes of the names on disk.
        var writtenFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var writtenFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int tables = 0, locFiles = 0, otherFiles = 0;

        Directory.CreateDirectory(packRoot);

        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];
            writing.CurrentEntry = entry.Path;

            if (reasons[i] is { } reason)
            {
                skipped.Add(new SkippedEntry(entry.Path, reason));
            }
            else if (TooLong(targetPackRoot, ProjectLayout.ToRelativeFile(entry.Path, ProjectFileKind.Raw)))
            {
                skipped.Add(new SkippedEntry(entry.Path, "The file path would be longer than " + MaxPathLength + " characters."));
            }
            else
            {
                var content = PackReader.ReadEntry(index, entry, cancellationToken, MaxEntryBytes);
                if (content.Bytes is null)
                {
                    skipped.Add(new SkippedEntry(entry.Path, Describe(content.Problem.ToString(), content.Detail)));
                }
                else
                {
                    var converted = EntryConversion.ToProjectFile(entry.Path, content.Bytes, schema);
                    var relativeFile = ProjectLayout.ToRelativeFile(entry.Path, converted.Kind);
                    var file = Path.GetFullPath(Path.Combine(packRoot, relativeFile));

                    if (TooLong(targetPackRoot, relativeFile))
                    {
                        skipped.Add(new SkippedEntry(entry.Path, "The file path would be longer than " + MaxPathLength + " characters."));
                    }
                    else if (!file.StartsWith(packRootPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped.Add(new SkippedEntry(entry.Path, "The file would be outside the project folder."));
                    }
                    else if (ClashReason(relativeFile.Replace('\\', '/'), writtenFiles, writtenFolders) is { } clash)
                    {
                        skipped.Add(new SkippedEntry(entry.Path, clash));
                    }
                    else if (!TryCreateFile(file, converted.Bytes))
                    {
                        skipped.Add(new SkippedEntry(entry.Path, "The file already exists on disk under another name or spelling."));
                    }
                    else
                    {
                        Register(relativeFile.Replace('\\', '/'), writtenFiles, writtenFolders);
                        switch (converted.Kind)
                        {
                            case ProjectFileKind.Table:
                                tables++;
                                break;
                            case ProjectFileKind.Loc:
                                locFiles++;
                                break;
                            default:
                                otherFiles++;
                                if (converted.Note is not null)
                                {
                                    stayedBinary.Add(new BinaryTable(entry.Path, converted.Note));
                                }

                                break;
                        }
                    }
                }
            }

            progress?.Report(new OperationProgress(i + 1, entries.Count, entry.Path));
        }

        writing.CurrentEntry = null;
        cancellationToken.ThrowIfCancellationRequested();

        var dependencies = index.Dependencies.Where(IsUsableDependency).ToList();
        var dropped = index.Dependencies.Where(d => !IsUsableDependency(d)).ToList();
        var project = new ModProject(Guid.NewGuid(), request.ProjectName, request.GameId, request.PackName, dependencies);
        var saved = ProjectFile.Save(staging, project);
        if (saved.Problem != ProjectProblem.None)
        {
            return Fail(ImportProblem.TargetUnwritable, Describe(saved.Problem.ToString(), saved.Detail));
        }

        beforeMove?.Invoke();

        // The target is not looked at again: a folder or file that appeared since the first look makes the delete (not empty) or the move
        // (exists) fail, and neither of them replaces or removes anything of the user's.
        if (MoveIntoPlace(staging, target, targetExisted) is { } moveFailure)
        {
            return Fail(ImportProblem.TargetUnwritable, moveFailure);
        }

        return new ImportReport(project, ImportProblem.None, null, tables, locFiles, otherFiles, stayedBinary, skipped, dropped);
    }

    private static bool IsUsableDependency(string name) => !string.IsNullOrWhiteSpace(name) && PackNameRules.IsWellFormed(name);

    /// <summary>
    /// Creates the file with its folders. False when the file already exists although the clash maps did not see it (8.3 short names,
    /// differences in case folding); every other I/O failure is thrown.
    /// </summary>
    private static bool TryCreateFile(string file, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        try
        {
            using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            return true;
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is ErrorFileExists or ErrorAlreadyExists)
        {
            return false;
        }
    }

    /// <summary>
    /// Replaces the empty target folder (if there is one) by the staging folder. Returns null on success, else the detail of the failure.
    /// When the move fails after the empty folder was removed, the empty folder is put back, so the target is in the state it was in before.
    /// </summary>
    private static string? MoveIntoPlace(string staging, string target, bool targetExisted)
    {
        if (targetExisted)
        {
            try
            {
                Retry(() => Directory.Delete(target, recursive: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "The empty target folder cannot be replaced (is it still empty?): " + ex.Message;
            }
        }

        try
        {
            Retry(() => Directory.Move(staging, target));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var detail = "The project folder cannot be moved into place: " + ex.Message;
            if (targetExisted)
            {
                try
                {
                    Directory.CreateDirectory(target);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
                {
                    detail += " The empty target folder no longer exists.";
                }
            }

            return detail;
        }
    }

    /// <summary>Runs <paramref name="action"/> up to 5 times, 100 ms apart, against short locks (virus scanner). The last failure is thrown.</summary>
    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < MoveAttempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(MoveRetryDelayMs);
            }
        }
    }

    /// <summary>
    /// The reason for each entry (same order as <paramref name="entries"/>) why it is left out of the project, or null when it is kept.
    /// The first reason wins. Duplicates and file/folder clashes are found against the entries that are kept, ignoring case.
    /// </summary>
    private static string?[] ComputeSkips(IReadOnlyList<PackEntry> entries)
    {
        var reasons = new string?[entries.Count];
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < entries.Count; i++)
        {
            var path = entries[i].Path;
            var reason = ProjectLayout.UnsafePathReason(path);
            if (reason is null && ProjectLayout.LooksLikeTextForm(path))
            {
                reason = "the name collides with the text form of a table or loc file";
            }

            reason ??= ClashReason(path, files, folders);
            reasons[i] = reason;
            if (reason is null)
            {
                Register(path, files, folders);
            }
        }

        return reasons;
    }

    private static void Register(string path, Dictionary<string, string> files, Dictionary<string, string> folders)
    {
        files[path] = path;
        for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
        {
            folders.TryAdd(path[..slash], path);
        }
    }

    private static string? ClashReason(string path, Dictionary<string, string> files, Dictionary<string, string> folders)
    {
        if (files.TryGetValue(path, out var same))
        {
            return $"duplicate of '{same}'";
        }

        if (folders.TryGetValue(path, out var child))
        {
            return $"'{child}' is a file and a folder";
        }

        for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
        {
            if (files.TryGetValue(path[..slash], out var parentFile))
            {
                return $"'{parentFile}' is a file and a folder";
            }
        }

        return null;
    }

    private static bool TooLong(string targetPackRoot, string relativeFile) =>
        Path.Combine(targetPackRoot, relativeFile).Length > MaxPathLength;

    /// <summary>A staging path next to the target that does not exist yet, so that cleaning it up never touches something else.</summary>
    private static string NewStagingPath(string target)
    {
        while (true)
        {
            var candidate = target + ".import-" + Guid.NewGuid().ToString("N")[..8];
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>True when the staging folder is gone.</summary>
    private static bool RemoveStaging(string staging)
    {
        for (var attempt = 1; attempt <= CleanupAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A virus scanner may hold a file for a moment.
                if (attempt < CleanupAttempts)
                {
                    Thread.Sleep(50 * attempt);
                }
            }
        }

        return false;
    }

    /// <summary>The entry that is being written, so that a write failure can name it. Written by <see cref="WriteProject"/>, read after an exception.</summary>
    private sealed class WriteState
    {
        public string? CurrentEntry { get; set; }
    }

    private static string Describe(string problem, string? detail) => string.IsNullOrWhiteSpace(detail) ? problem : problem + ": " + detail;

    private static ImportReport Fail(ImportProblem problem, string detail) => new(null, problem, detail, 0, 0, 0, [], [], []);
}
