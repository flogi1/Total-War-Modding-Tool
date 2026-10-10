using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.Editing;

/// <summary>One file of a step as the write left it: Previous is the text before (null: the file did not exist), Next the text written, After the fingerprint after the write, CreatedFolders the folders the write created (outermost first, relative to the pack folder; empty for an existing file).</summary>
public sealed record FileContentsEntry(string RelativeFile, string? Previous, string Next, FileFingerprint After, IReadOnlyList<string> CreatedFolders);

/// <summary>A step did not run because a file is not in the state the step left it in (changed or gone). Nothing was changed; the history keeps the step.</summary>
public sealed class FileContentsChangedException(string relativeFile, string message) : StepDeferredException(message, null)
{
    /// <summary>The file (below pack\) that is not as the step left it.</summary>
    public string RelativeFile { get; } = relativeFile;
}

/// <summary>
/// The undo step of one object write. Undo writes Previous back (a file with Previous null goes to the trash and its created folders are
/// removed when empty), Redo writes Next back (a trashed file is restored from the trash). Before anything is touched, every file is probed
/// against the fingerprint the step last left; a mismatch throws <see cref="FileContentsChangedException"/> (step stays). A locked file before the first
/// write throws <see cref="StepDeferredException"/> (step stays); a failure after the first write throws <see cref="InvalidOperationException"/> whose message names every
/// file and its state (step dropped). Do and Undo run on the UI thread when the history asks; the first execution is the writer's job.
/// </summary>
public sealed class FileContentsStep : IUndoableAction
{
    private sealed class FileState(FileContentsEntry entry)
    {
        public FileContentsEntry Entry { get; } = entry;

        /// <summary>The state of the file as the step last left it; null while the file is in the trash.</summary>
        public FileFingerprint? Current { get; set; } = entry.After;

        public TrashedFile? Trash { get; set; }

        /// <summary>The folders the file's creation made, outermost first.</summary>
        public IReadOnlyList<string> Folders { get; set; } = entry.CreatedFolders;
    }

    private readonly string _projectFolder;
    private readonly string _trashRoot;
    private readonly TimeProvider _time;
    private readonly List<FileState> _states;

    public FileContentsStep(Guid projectId, string projectFolder, string description, IReadOnlyList<FileContentsEntry> files, string trashRoot, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(trashRoot);
        ArgumentNullException.ThrowIfNull(time);

        ProjectId = projectId;
        Description = description;
        _projectFolder = projectFolder;
        _trashRoot = trashRoot;
        _time = time;
        _states = [.. files.Select(static f => new FileState(f))];
        Files = [.. files.Select(static f => f.RelativeFile).OrderBy(static f => f, StringComparer.Ordinal)];
    }

    public Guid ProjectId { get; }

    public string Description { get; }

    /// <summary>The files of the step (below pack\, with '\'), ordinal.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>Files of this step that are in the trash right now (after an undo of created files).</summary>
    public IReadOnlyList<TrashedFile> InTrash => [.. _states.Where(static s => s.Trash is not null).Select(static s => s.Trash!)];

    public void Do() => Run(undo: false);

    public void Undo() => Run(undo: true);

    private void Run(bool undo)
    {
        foreach (var state in _states)
        {
            Probe(state);
        }

        var done = new List<string>();
        for (var i = 0; i < _states.Count; i++)
        {
            var failure = Apply(_states[i], undo, out var outcome);
            if (failure is null)
            {
                done.Add(outcome);
                continue;
            }

            // Nothing was changed yet and the library took back everything: the step can be tried again.
            if (i == 0 && failure.Deferrable)
            {
                throw new StepDeferredException(failure.Detail, null);
            }

            var lines = new List<string>();
            for (var j = 0; j < _states.Count; j++)
            {
                var state = j < done.Count ? done[j] : j == i ? "failed: " + failure.Detail : "unchanged";
                lines.Add($"{_states[j].Entry.RelativeFile}: {state}");
            }

            throw new InvalidOperationException(string.Join("; ", lines));
        }
    }

    // Every file must be in the state the step left it in, before the first one is written.
    private void Probe(FileState state)
    {
        var file = state.Entry.RelativeFile;
        var probe = ProjectFileWriter.Probe(_projectFolder, file);
        if (probe.State == FileProbeState.Unknown)
        {
            throw new StepDeferredException(probe.Detail ?? "the file cannot be asked: " + file, null);
        }

        if (state.Current is null)
        {
            if (probe.State != FileProbeState.Missing)
            {
                throw new FileContentsChangedException(file, "the file was changed since the step left it: " + file);
            }

            return;
        }

        if (probe.State == FileProbeState.Missing)
        {
            throw new FileContentsChangedException(file, "the file is gone: " + file);
        }

        if (probe.Fingerprint!.Length != state.Current.Length || probe.Fingerprint.LastWriteTimeUtc != state.Current.LastWriteTimeUtc)
        {
            throw new FileContentsChangedException(file, "the file was changed since the step left it: " + file);
        }
    }

    private sealed record Failure(string Detail, bool Deferrable);

    // Null when the file was done (outcome says how); else what went wrong.
    private Failure? Apply(FileState state, bool undo, out string outcome)
    {
        var entry = state.Entry;
        outcome = string.Empty;

        if (entry.Previous is not null)
        {
            var result = ProjectFileWriter.WriteText(_projectFolder, entry.RelativeFile, undo ? entry.Previous : entry.Next);
            if (result.Problem != FileWriteProblem.None)
            {
                return new Failure(Describe(result.Problem.ToString(), result.Detail), result.Problem == FileWriteProblem.Locked);
            }

            state.Current = result.Fingerprint;
            outcome = "written";
            return null;
        }

        if (undo)
        {
            var trashed = ProjectTrash.MoveToTrash(_trashRoot, ProjectId, _projectFolder, entry.RelativeFile, _time);
            if (trashed.Problem != FileOpProblem.None)
            {
                return new Failure(Describe(trashed.Problem.ToString(), trashed.Detail), trashed.Problem == FileOpProblem.Locked && !trashed.LeftBehind);
            }

            state.Trash = trashed.File;
            state.Current = null;
            ProjectFileOps.RemoveEmptyFolders(_projectFolder, state.Folders);
            outcome = "trashed";
            return null;
        }

        if (state.Trash is null)
        {
            outcome = "unchanged";
            return null;
        }

        var restored = ProjectTrash.Restore(state.Trash, _projectFolder, _trashRoot);
        if (restored.Problem != FileOpProblem.None)
        {
            return new Failure(Describe(restored.Problem.ToString(), restored.Detail), restored.Problem == FileOpProblem.Locked && !restored.LeftBehind);
        }

        state.Current = restored.Fingerprint;
        state.Folders = restored.CreatedFolders;
        state.Trash = null;
        outcome = "restored";
        return null;
    }

    private static string Describe(string problem, string? detail) => detail is null ? problem : problem + ": " + detail;
}
