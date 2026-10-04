using System.Globalization;
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

    /// <summary>
    /// Reads the pack folder of the project and prepares every entry. Writes nothing. Table and loc files are read and converted into
    /// memory, other files are opened only when the pack is written. Every problem is collected, one per file; a file with a problem
    /// contributes no entry.
    /// </summary>
    public static BuildPlan ComputeBuild(string projectFolder, ModProject project, GameSchema? schema, Func<string> newGuid,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(newGuid);

        var entries = new List<PackWriteEntry>();
        var problems = new List<BuildProblem>();
        var packRoot = Path.Combine(projectFolder, ProjectFile.PackFolder);

        if (!Directory.Exists(packRoot))
        {
            problems.Add(new BuildProblem(string.Empty, 0, PackFolderMissing));
            return new BuildPlan(entries, project.Dependencies, problems);
        }

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(packRoot, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(packRoot, file))
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(new BuildProblem(string.Empty, 0, "the pack folder cannot be read (" + ex.GetType().Name + ")"));
            return new BuildPlan(entries, project.Dependencies, problems);
        }

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Prepare(packRoot, files[i], schema, newGuid, entries, problems);
            progress?.Report(new OperationProgress(i + 1, files.Count, files[i]));
        }

        var validation = PackWriter.Validate(project.Dependencies, entries);
        if (validation.Problem != PackWriteProblem.None)
        {
            problems.Add(new BuildProblem(string.Empty, 0, validation.Detail ?? validation.Problem.ToString()));
        }

        return new BuildPlan(entries, project.Dependencies, problems);
    }

    /// <summary>
    /// Writes build\&lt;PackName&gt; through a temp file in the same folder. A plan with problems writes nothing. After a failure or a
    /// cancel the previous pack is untouched and the temp file is removed. Only <see cref="OperationCanceledException"/> and argument
    /// errors leave as exceptions.
    /// </summary>
    public static BuildResult WriteBuild(string projectFolder, ModProject project, BuildPlan plan, DateTimeOffset buildTime,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Problems.Count > 0)
        {
            return new BuildResult(BuildOutcome.HasProblems, null, null);
        }

        var buildFolder = Path.Combine(projectFolder, ProjectFile.BuildFolder);
        var target = Path.Combine(buildFolder, project.PackName);
        var temp = Path.Combine(buildFolder, project.PackName + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            try
            {
                Directory.CreateDirectory(buildFolder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failed("the build folder cannot be created (" + ex.GetType().Name + ")");
            }

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
                return Failed("the pack cannot replace the previous one (" + ex.GetType().Name + ": " + ex.Message + ")");
            }

            return new BuildResult(BuildOutcome.Built, target, null);
        }
        finally
        {
            RemoveIfPresent(temp);
        }
    }

    private static void Prepare(string packRoot, string relativeFile, GameSchema? schema, Func<string> newGuid,
        List<PackWriteEntry> entries, List<BuildProblem> problems)
    {
        var (packPath, kind) = ProjectLayout.FromRelativeFile(relativeFile);
        var fullPath = Path.Combine(packRoot, relativeFile);
        try
        {
            if (kind == ProjectFileKind.Raw)
            {
                var length = new FileInfo(fullPath).Length;
                entries.Add(new PackWriteEntry(packPath, length, () => new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read)));
                return;
            }

            var converted = EntryConversion.ToPackEntry(kind, packPath, File.ReadAllBytes(fullPath), schema, newGuid);
            if (converted.Bytes is null)
            {
                problems.Add(new BuildProblem(relativeFile, converted.Line, converted.Detail ?? "the file cannot be converted"));
                return;
            }

            var bytes = converted.Bytes;
            entries.Add(new PackWriteEntry(packPath, bytes.Length, () => new MemoryStream(bytes, writable: false)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(new BuildProblem(relativeFile, 0, "the file cannot be read (" + ex.GetType().Name + ")"));
        }
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
