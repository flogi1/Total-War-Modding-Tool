using System.Text;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;

namespace TotalWarModTool.Editing;

/// <summary>
/// Writes a <see cref="WritePlan"/> into the pack folder of a project: only through <see cref="ProjectFileWriter"/> and
/// <see cref="ProjectFileOps"/>, only files the plan names, only after their fingerprints were checked.
/// </summary>
public static class ProjectWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private sealed record Written(PlannedFile File, FileFingerprint Fingerprint, IReadOnlyList<string> CreatedFolders);

    /// <summary>Throws ArgumentException for a plan with questions, errors or no files. Cancellation is honoured only before the first write.</summary>
    public static WriteOutcome Apply(WritePlan plan, Guid projectId, string projectFolder, string description, string trashRoot, TimeProvider time,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(trashRoot);
        ArgumentNullException.ThrowIfNull(time);
        if (plan.HasQuestions || plan.HasErrors || plan.Files.Count == 0)
        {
            throw new ArgumentException("The plan has open questions or errors, or nothing to write.", nameof(plan));
        }

        if (Check(plan, projectFolder) is { } mismatch)
        {
            return new WriteOutcome(null, mismatch);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var written = new List<Written>(plan.Files.Count);
        foreach (var file in plan.Files)
        {
            var (fingerprint, folders, problem) = Write(file, projectFolder);
            if (fingerprint is not null)
            {
                written.Add(new Written(file, fingerprint, folders));
                continue;
            }

            // The file that failed is unchanged (the library takes back its own temp file and folders), except when it vanished right after it
            // was written: then it is part of the rollback.
            if (problem.Vanished)
            {
                written.Add(new Written(file, new FileFingerprint(string.Empty, 0, default), folders));
            }

            return new WriteOutcome(null, RollBack(plan, written, problem.Detail, projectId, projectFolder, trashRoot, time));
        }

        var entries = written.Select(static w => new FileContentsEntry(w.File.RelativeFile, w.File.PreviousText, w.File.NewText, w.Fingerprint, w.CreatedFolders)).ToList();
        return new WriteOutcome(new FileContentsStep(projectId, projectFolder, description, entries, trashRoot, time), null);
    }

    // Every file must be as the snapshot saw it, before the first one is written.
    private static WriteFailure? Check(WritePlan plan, string projectFolder)
    {
        var unchanged = plan.Files.Select(static f => (f.RelativeFile, WrittenFileState.Unchanged)).ToList();
        foreach (var file in plan.Files)
        {
            var probe = ProjectFileWriter.Probe(projectFolder, file.RelativeFile);
            if (probe.State == FileProbeState.Unknown)
            {
                return new WriteFailure(WriteFailureKind.FileUnknown, file.RelativeFile, probe.Detail ?? "the file cannot be asked: " + file.RelativeFile, unchanged);
            }

            if (file.Previous is null)
            {
                if (probe.State == FileProbeState.Present)
                {
                    return new WriteFailure(WriteFailureKind.FileChanged, file.RelativeFile, "a file appeared at the target: " + file.RelativeFile, unchanged);
                }

                continue;
            }

            if (probe.State == FileProbeState.Missing
                || probe.Fingerprint!.Length != file.Previous.Length || probe.Fingerprint.LastWriteTimeUtc != file.Previous.LastWriteTimeUtc)
            {
                return new WriteFailure(WriteFailureKind.FileChanged, file.RelativeFile, "the file changed since the object was built: " + file.RelativeFile, unchanged);
            }
        }

        return null;
    }

    private sealed record WriteProblem(string Detail, bool Vanished);

    private static (FileFingerprint? Fingerprint, IReadOnlyList<string> Folders, WriteProblem Problem) Write(PlannedFile file, string projectFolder)
    {
        if (file.Kind == PlannedFileKind.Changed)
        {
            var result = ProjectFileWriter.WriteText(projectFolder, file.RelativeFile, file.NewText);
            if (result.Problem != FileWriteProblem.None)
            {
                return (null, [], new WriteProblem(Describe(file.RelativeFile, result.Problem.ToString(), result.Detail), false));
            }

            var fingerprint = result.Fingerprint ?? ProjectFileWriter.Fingerprint(projectFolder, file.RelativeFile);
            return (fingerprint, [], new WriteProblem("the file vanished right after it was written: " + file.RelativeFile, true));
        }

        var created = ProjectFileOps.Create(projectFolder, file.RelativeFile, Utf8NoBom.GetBytes(file.NewText), CancellationToken.None);
        if (created.Problem != FileOpProblem.None)
        {
            return (null, [], new WriteProblem(Describe(file.RelativeFile, created.Problem.ToString(), created.Detail), false));
        }

        var createdFingerprint = created.Fingerprint ?? ProjectFileWriter.Fingerprint(projectFolder, file.RelativeFile);
        return (createdFingerprint, created.CreatedFolders, new WriteProblem("the file vanished right after it was written: " + file.RelativeFile, true));
    }

    private static string Describe(string file, string problem, string? detail) => detail is null ? $"{file}: {problem}" : $"{file}: {problem}: {detail}";

    // Writes the files that were written back, last first: a changed file gets its previous text, a created or copied file goes to the trash.
    private static WriteFailure RollBack(WritePlan plan, List<Written> written, string detail, Guid projectId, string projectFolder,
        string trashRoot, TimeProvider time)
    {
        var states = plan.Files.Select(static f => (f.RelativeFile, State: WrittenFileState.Unchanged)).ToList();
        var allRolledBack = true;
        for (var i = written.Count - 1; i >= 0; i--)
        {
            var item = written[i];
            var rolledBack = item.File.Kind == PlannedFileKind.Changed
                ? ProjectFileWriter.WriteText(projectFolder, item.File.RelativeFile, item.File.PreviousText!).Problem == FileWriteProblem.None
                : MoveToTrash(item, projectId, projectFolder, trashRoot, time);
            states[i] = (item.File.RelativeFile, rolledBack ? WrittenFileState.RolledBack : WrittenFileState.RollbackFailed);
            allRolledBack &= rolledBack;
        }

        return new WriteFailure(allRolledBack ? WriteFailureKind.WriteFailed : WriteFailureKind.RollbackFailed, null, detail, states);
    }

    // A file that is gone already needs no rollback.
    private static bool MoveToTrash(Written item, Guid projectId, string projectFolder, string trashRoot, TimeProvider time)
    {
        var trashed = ProjectTrash.MoveToTrash(trashRoot, projectId, projectFolder, item.File.RelativeFile, time);
        if (trashed.Problem == FileOpProblem.None || trashed.Problem == FileOpProblem.SourceMissing)
        {
            ProjectFileOps.RemoveEmptyFolders(projectFolder, item.CreatedFolders);
            return true;
        }

        return false;
    }
}
