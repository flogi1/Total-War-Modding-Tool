using TotalWarModTool.Operations;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Storage;

namespace TotalWarModTool.Projects;

/// <summary>
/// Builds the mod pack of a project from its pack folder. <see cref="ComputeBuild"/> reads and prepares and writes nothing;
/// <see cref="WriteBuild"/> writes the pack through a temp file, so a failed or cancelled build leaves the previous pack as it was.
/// </summary>
public static class ProjectBuilder
{
    private const string PackFolderMissing = "the pack folder is missing";
    private const string TempExtension = ".tmp";
    private const int TempSuffixLength = 8;

    /// <summary>A file below the pack folder, or a folder that cannot be read (then <paramref name="FolderProblem"/> is set). Relative paths use '\'.</summary>
    private sealed record WalkItem(string RelativePath, ProjectFileItem? File, BuildProblem? FolderProblem);

    /// <summary>
    /// Reads the pack folder of the project and prepares every entry. Writes nothing. Table and loc files are read and converted into
    /// memory, other files are opened only when the pack is written. Every problem is collected, one per file or folder; a file with a
    /// problem contributes no entry. A folder that cannot be listed or that is a link is a problem and its files are not read.
    /// Which files belong to the pack is decided by <see cref="ProjectFiles"/>; files it ignores are named in the plan, not packed.
    /// </summary>
    public static BuildPlan ComputeBuild(string projectFolder, ModProject project, GameSchema? schema, Func<string> newGuid,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(newGuid);
        CheckPackName(project);

        var entries = new List<PackWriteEntry>();
        var problems = new List<BuildProblem>();
        var packRoot = Path.Combine(projectFolder, ProjectFile.PackFolder);

        var list = ProjectFiles.List(projectFolder, cancellationToken);
        if (!list.PackFolderExists)
        {
            problems.Add(new BuildProblem(string.Empty, 0, PackFolderMissing));
            return new BuildPlan(entries, project.Dependencies, problems, list.Ignored);
        }

        var items = list.Files.Select(file => new WalkItem(file.RelativeFile, file, null))
            .Concat(list.Problems.Select(p => new WalkItem(p.RelativePath, null, new BuildProblem(p.RelativePath, 0, p.Detail))))
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToList();
        var total = items.Count(item => item.FolderProblem is null);
        var firstFileOfPackPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var done = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.FolderProblem is { } folderProblem)
            {
                problems.Add(folderProblem);
                continue;
            }

            var (_, packPath, kind) = item.File!;
            if (firstFileOfPackPath.TryGetValue(packPath, out var earlier))
            {
                problems.Add(new BuildProblem(item.RelativePath, 0, $"the pack path '{packPath}' is already used by the file '{earlier}'"));
            }
            else
            {
                firstFileOfPackPath[packPath] = item.RelativePath;
                var entry = Prepare(packRoot, item.RelativePath, packPath, kind, schema, newGuid, problems);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            progress?.Report(new OperationProgress(++done, total, item.RelativePath));
        }

        var validation = PackWriter.Validate(project.Dependencies, entries);
        if (validation.Problem != PackWriteProblem.None)
        {
            problems.Add(new BuildProblem(string.Empty, 0, validation.Detail ?? validation.Problem.ToString()));
        }

        return new BuildPlan(entries, project.Dependencies, problems, list.Ignored);
    }

    /// <summary>
    /// Writes build\&lt;PackName&gt; through a temp file in the same folder. The plan carries the content of the files, so it must come
    /// from <see cref="ComputeBuild"/> for the same project folder. A plan with problems writes nothing, and a build folder that is a link
    /// is a write failure that neither writes nor removes anything there. Stale temp files of earlier
    /// crashed runs are removed first. After a failure or a cancel the previous pack is untouched and the temp file is removed. Only
    /// <see cref="OperationCanceledException"/> and argument errors leave as exceptions.
    /// </summary>
    public static BuildResult WriteBuild(string projectFolder, ModProject project, BuildPlan plan, DateTimeOffset buildTime,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(plan);
        CheckPackName(project);

        if (plan.Problems.Count > 0)
        {
            return new BuildResult(BuildOutcome.HasProblems, null, null);
        }

        var buildFolder = Path.Combine(projectFolder, ProjectFile.BuildFolder);

        // A linked build folder would let the build write and clean up somewhere outside the project: refused before anything is touched.
        if (ProjectFiles.IsLink(buildFolder))
        {
            return Failed($"the build folder '{buildFolder}' is a link; links to folders are not supported and nothing is written there");
        }

        var target = Path.Combine(buildFolder, project.PackName);
        var temp = Path.Combine(buildFolder, project.PackName + "." + Guid.NewGuid().ToString("N")[..TempSuffixLength] + TempExtension);
        try
        {
            try
            {
                Directory.CreateDirectory(buildFolder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failed("the build folder cannot be created: " + ex.Message);
            }

            RemoveStaleTempFiles(buildFolder, project.PackName);

            var total = plan.Entries.Count;
            var written = progress is null ? null : new SyncProgress(done => progress.Report(new OperationProgress(done, total, null)));
            var result = PackWriter.Write(temp, plan.Dependencies, plan.Entries, buildTime, written, cancellationToken);
            if (result.Problem != PackWriteProblem.None)
            {
                return Failed(result.Problem + ": " + result.Detail);
            }

            try
            {
                AtomicFile.Replace(temp, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failed("the pack cannot replace the previous one: " + ex.Message);
            }

            return new BuildResult(BuildOutcome.Built, target, null);
        }
        finally
        {
            RemoveIfPresent(temp);
        }
    }

    private static void CheckPackName(ModProject project)
    {
        if (PackNameRules.Check(project.PackName) is { } reason)
        {
            throw new ArgumentException("The pack name of the project is not valid: " + reason, nameof(project));
        }
    }

    private static PackWriteEntry? Prepare(string packRoot, string relativeFile, string packPath, ProjectFileKind kind, GameSchema? schema,
        Func<string> newGuid, List<BuildProblem> problems)
    {
        var fullPath = Path.Combine(packRoot, relativeFile);
        try
        {
            if (kind == ProjectFileKind.Raw)
            {
                var length = new FileInfo(fullPath).Length;
                return new PackWriteEntry(packPath, length, () => new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read));
            }

            var converted = EntryConversion.ToPackEntry(kind, packPath, File.ReadAllBytes(fullPath), schema, newGuid);
            if (converted.Bytes is null)
            {
                problems.Add(new BuildProblem(relativeFile, converted.Line, converted.Detail ?? "the file cannot be converted"));
                return null;
            }

            var bytes = converted.Bytes;
            return new PackWriteEntry(packPath, bytes.Length, () => new MemoryStream(bytes, writable: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(new BuildProblem(relativeFile, 0, "the file cannot be read: " + ex.Message));
            return null;
        }
    }

    /// <summary>Removes <c>&lt;PackName&gt;.&lt;8 hex digits&gt;.tmp</c> files that an earlier crashed run left behind. Best effort.</summary>
    private static void RemoveStaleTempFiles(string buildFolder, string packName)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(buildFolder))
            {
                if (IsTempFileOf(Path.GetFileName(file), packName))
                {
                    RemoveIfPresent(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Stale files are only clutter; the build goes on.
        }
    }

    private static bool IsTempFileOf(string fileName, string packName)
    {
        var prefixLength = packName.Length + 1;
        if (fileName.Length != prefixLength + TempSuffixLength + TempExtension.Length
            || !fileName.StartsWith(packName, StringComparison.OrdinalIgnoreCase)
            || fileName[packName.Length] != '.'
            || !fileName.EndsWith(TempExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsHex(fileName.AsSpan(prefixLength, TempSuffixLength));
    }

    private static bool IsHex(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static BuildResult Failed(string detail) => new(BuildOutcome.WriteFailed, null, detail);

    private static void RemoveIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort; the original outcome is the one that matters.
        }
    }

    /// <summary>Reports on the calling thread: <see cref="Progress{T}"/> would post, and a cancel from inside the callback must take effect at once.</summary>
    private sealed class SyncProgress(Action<long> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
