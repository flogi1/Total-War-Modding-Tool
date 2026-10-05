using TotalWarModTool.Projects;
using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.App;

/// <summary>
/// What a <see cref="FileStep"/> carries from one run to the next: the file in the trash after a step moved it there (null while the file is
/// in the project), and the folders that the last run created and that the other direction has to remove again (outermost first).
/// </summary>
public sealed record FileStepState(TrashedFile? Trash, IReadOnlyList<string> CreatedFolders)
{
    /// <summary>Nothing in the trash and no folder created.</summary>
    public static readonly FileStepState Empty = new(null, []);
}

/// <summary>
/// The outcome of one run of a step: a problem with the technical detail of the library, or <see cref="Problem"/> None with the state for
/// the next run.
/// </summary>
public sealed record FileStepResult(FileOpProblem Problem, string? Detail, FileStepState State);

/// <summary>
/// One file operation in the undo history. Do and Undo run on the UI thread, when the history asks for an undo or a redo; they throw
/// <see cref="InvalidOperationException"/> with a short English detail when the step cannot run (the history then drops it). The first
/// execution of an action is not a run of the step: the controller does the work in the runner and then records the step.
/// </summary>
public sealed class FileStep : IUndoableAction
{
    private readonly Func<FileStepState, FileStepResult> _do;
    private readonly Func<FileStepState, FileStepResult> _undo;
    private FileStepState _state;

    /// <param name="filesAfterDo">The files (below pack\, with '\') that exist after Do.</param>
    /// <param name="filesAfterUndo">The files (below pack\, with '\') that exist after Undo.</param>
    /// <param name="initial">The state after the first execution.</param>
    /// <param name="doWork">Repeats the action (redo). Gets the state the last run left.</param>
    /// <param name="undoWork">Takes the action back. Gets the state the last run left.</param>
    public FileStep(Guid projectId, string description, IReadOnlyList<string> filesAfterDo, IReadOnlyList<string> filesAfterUndo,
        FileStepState initial, Func<FileStepState, FileStepResult> doWork, Func<FileStepState, FileStepResult> undoWork)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(filesAfterDo);
        ArgumentNullException.ThrowIfNull(filesAfterUndo);
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(doWork);
        ArgumentNullException.ThrowIfNull(undoWork);

        ProjectId = projectId;
        Description = description;
        FilesAfterDo = filesAfterDo;
        FilesAfterUndo = filesAfterUndo;
        _state = initial;
        _do = doWork;
        _undo = undoWork;
    }

    public Guid ProjectId { get; }

    /// <summary>The files (below pack\, with '\') that exist after Do. Used to find tabs and to refresh the explorer.</summary>
    public IReadOnlyList<string> FilesAfterDo { get; }

    /// <summary>The files (below pack\, with '\') that exist after Undo.</summary>
    public IReadOnlyList<string> FilesAfterUndo { get; }

    public string Description { get; }

    public void Do() => Run(_do);

    public void Undo() => Run(_undo);

    // The state changes only when the run worked, so that a run that is tried again (it never is: the history drops the step) starts from the truth.
    private void Run(Func<FileStepState, FileStepResult> work)
    {
        var result = work(_state);
        if (result.Problem != FileOpProblem.None)
        {
            throw new InvalidOperationException(result.Detail is null ? result.Problem.ToString() : result.Problem + ": " + result.Detail);
        }

        _state = result.State;
    }
}
