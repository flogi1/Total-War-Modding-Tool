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
    private const string LocExtension = ".loc";

    /// <summary>Counts what an import would write, by path only. Nothing is read from the data section of the pack.</summary>
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
    /// and argument errors are thrown. <paramref name="schema"/> may be null: tables then stay binary.
    /// </summary>
    public static ImportReport Import(ImportRequest request, GameSchema? schema, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePack);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetFolder);
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

        if (PackNameRules.Check(request.PackName) is { } nameReason)
        {
            return Fail(ImportProblem.InvalidPackName, nameReason);
        }

        if (request.VanillaPackNames.Contains(request.PackName))
        {
            return Fail(ImportProblem.InvalidPackName, "The pack name is the name of a pack of the game.");
        }

        var read = PackReader.ReadIndex(request.SourcePack);
        if (read.Index is not { } index)
        {
            return Fail(ImportProblem.SourceUnreadable, Describe(read.Problem.ToString(), read.Detail));
        }

        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.TargetFolder));
        bool targetExisted;
        try
        {
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
            return Fail(ImportProblem.TargetUnwritable, "The target folder cannot be read (" + ex.GetType().Name + ").");
        }

        var parent = Path.GetDirectoryName(target);
        if (parent is null)
        {
            return Fail(ImportProblem.TargetUnwritable, "The target folder has no parent folder.");
        }

        var staging = NewStagingPath(target);
        var succeeded = false;
        try
        {
            return WriteProject(request, schema, index, target, targetExisted, staging, progress, cancellationToken, out succeeded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(ImportProblem.TargetUnwritable, "The project folder cannot be written (" + ex.GetType().Name + ").");
        }
        finally
        {
            if (!succeeded)
            {
                RemoveStaging(staging);
            }
        }
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
        out bool succeeded)
    {
        succeeded = false;
        var entries = index.Entries;
        var reasons = ComputeSkips(entries);
        var packRoot = Path.Combine(staging, ProjectFile.PackFolder);
        var stagingPrefix = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        var targetPackRoot = Path.Combine(target, ProjectFile.PackFolder);
        var skipped = new List<SkippedEntry>();
        var stayedBinary = new List<BinaryTable>();
        int tables = 0, locFiles = 0, otherFiles = 0;

        Directory.CreateDirectory(packRoot);

        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];

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
                    else if (!file.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped.Add(new SkippedEntry(entry.Path, "The file would be outside the project folder."));
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                        File.WriteAllBytes(file, converted.Bytes);
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

        cancellationToken.ThrowIfCancellationRequested();

        var project = new ModProject(Guid.NewGuid(), request.ProjectName, request.GameId, request.PackName, index.Dependencies);
        var saved = ProjectFile.Save(staging, project);
        if (saved.Problem != ProjectProblem.None)
        {
            return Fail(ImportProblem.TargetUnwritable, Describe(saved.Problem.ToString(), saved.Detail));
        }

        MoveIntoPlace(staging, target, targetExisted);
        succeeded = true;
        return new ImportReport(project, ImportProblem.None, null, tables, locFiles, otherFiles, stayedBinary, skipped);
    }

    /// <summary>
    /// Replaces the empty target folder (if there is one) by the staging folder. When the move fails after the empty folder was removed,
    /// the empty folder is put back, so the target is in the state it was in before.
    /// </summary>
    private static void MoveIntoPlace(string staging, string target, bool targetExisted)
    {
        if (targetExisted)
        {
            Directory.Delete(target, recursive: false);
        }

        try
        {
            Directory.Move(staging, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (targetExisted)
            {
                try
                {
                    Directory.CreateDirectory(target);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
                {
                    // Best effort; the failure of the move is the one that is reported.
                }
            }

            throw;
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
                files[path] = path;
                for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
                {
                    folders.TryAdd(path[..slash], path);
                }
            }
        }

        return reasons;
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

    private static void RemoveStaging(string staging)
    {
        for (var attempt = 1; attempt <= CleanupAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A virus scanner may hold a file for a moment; the last failure is given up silently.
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private static string Describe(string problem, string? detail) => string.IsNullOrWhiteSpace(detail) ? problem : problem + ": " + detail;

    private static ImportReport Fail(ImportProblem problem, string detail) => new(null, problem, detail, 0, 0, 0, [], []);
}
