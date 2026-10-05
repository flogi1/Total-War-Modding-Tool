using System.IO;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.App;

/// <summary>
/// What a <see cref="FileStep"/> carries from one run to the next: the file in the trash after a step moved it there (null while the file is
/// in the project), the folders that the last run created and that the other direction has to remove again (outermost first), and the state
/// of the file in the project as the step last left it (<paramref name="Placed"/>; null while the file is in the trash, or when it could not be
/// read). A step moves a file to the trash only while the file still is in that state.
/// </summary>
public sealed record FileStepState(TrashedFile? Trash, IReadOnlyList<string> CreatedFolders, FileFingerprint? Placed = null)
{
    /// <summary>Nothing in the trash, no folder created, no file placed.</summary>
    public static readonly FileStepState Empty = new(null, [], null);
}

/// <summary>
/// A step refused to move a file to the trash, because the file is not in the state the step left it in (another program or an edit
/// changed it, or it is gone): the step would have trashed content it did not put there. The history drops the step; the file stays.
/// </summary>
public sealed class FileStepChangedException(string relativeFile)
    : InvalidOperationException("the file was changed or removed since the step left it, so it was not moved to the trash: " + relativeFile)
{
    /// <summary>The file (below pack\) that was left as it is.</summary>
    public string RelativeFile { get; } = relativeFile;
}

/// <summary>
/// A step did not run because the folder of its project (or its pack folder) is missing or cannot be read. Nothing was changed and the
/// history keeps the step; unlike a locked file this may not pass by itself (the folder may have been moved or deleted), so the message names
/// the way out: reconnect the folder, or remove the project from the list.
/// </summary>
public sealed class ProjectUnreachableException(string message) : StepDeferredException(message, null);

/// <summary>
/// The outcome of one run of a step: a problem with the technical detail of the library, or <see cref="Problem"/> None with the state for
/// the next run. <paramref name="LeftBehind"/> is the flag of the library: the failed call could not take back everything it did.
/// <paramref name="LeftInTrash"/> is the full path of a file that the failed call left in the trash (<see cref="TrashResult.LeftCopy"/>), or null.
/// </summary>
public sealed record FileStepResult(FileOpProblem Problem, string? Detail, FileStepState State, bool LeftBehind = false, string? LeftInTrash = null);

/// <summary>
/// One file operation in the undo history. Do and Undo run on the UI thread, when the history asks for an undo or a redo. When the step
/// cannot run and nothing was changed, they throw <see cref="StepDeferredException"/> and the history keeps the step: the plain type when the
/// file is locked (or the file system refused) and the library took back everything, <see cref="ProjectUnreachableException"/> when the project
/// folder or its pack folder is missing or cannot be read. Every other failure
/// throws <see cref="InvalidOperationException"/> with a short English detail (the history then drops the step). The first execution of an
/// action is not a run of the step: the controller does the work in the runner and then records the step.
/// </summary>
public sealed class FileStep : IUndoableAction
{
    private readonly Func<FileStepState, FileStepResult> _do;
    private readonly Func<FileStepState, FileStepResult> _undo;
    private readonly string? _projectFolder;
    private FileStepState _state;

    /// <param name="filesAfterDo">The files (below pack\, with '\') that exist after Do.</param>
    /// <param name="filesAfterUndo">The files (below pack\, with '\') that exist after Undo.</param>
    /// <param name="initial">The state after the first execution.</param>
    /// <param name="doWork">Repeats the action (redo). Gets the state the last run left.</param>
    /// <param name="undoWork">Takes the action back. Gets the state the last run left.</param>
    /// <param name="projectFolder">
    /// The folder of the project. When it is given, it and its pack folder are looked at before every run: a folder that is missing or cannot
    /// be read defers the step (<see cref="ProjectUnreachableException"/>) before anything is touched; a missing pack folder does not.
    /// </param>
    public FileStep(Guid projectId, string description, IReadOnlyList<string> filesAfterDo, IReadOnlyList<string> filesAfterUndo,
        FileStepState initial, Func<FileStepState, FileStepResult> doWork, Func<FileStepState, FileStepResult> undoWork, string? projectFolder = null)
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
        _projectFolder = projectFolder;
    }

    public Guid ProjectId { get; }

    /// <summary>The files (below pack\, with '\') that exist after Do. Used to find tabs and to refresh the explorer.</summary>
    public IReadOnlyList<string> FilesAfterDo { get; }

    /// <summary>The files (below pack\, with '\') that exist after Undo.</summary>
    public IReadOnlyList<string> FilesAfterUndo { get; }

    public string Description { get; }

    /// <summary>The file of this step that is in the trash right now, or null. A step that is dropped while it holds one leaves it there.</summary>
    public TrashedFile? InTrash => _state.Trash;

    /// <summary>The full path of a file that the last run left in the trash although it failed (a copy it could not delete again), or null.</summary>
    public string? LeftInTrash { get; private set; }

    public void Do() => Run(_do);

    public void Undo() => Run(_undo);

    // The state changes only when the run worked, so that a run that is tried again (a deferred step) starts from the truth.
    private void Run(Func<FileStepState, FileStepResult> work)
    {
        if (_projectFolder is not null && UnreachableReason(_projectFolder) is { } unreachable)
        {
            throw new ProjectUnreachableException(unreachable);
        }

        var result = work(_state);
        LeftInTrash = result.LeftInTrash;
        if (result.Problem == FileOpProblem.None)
        {
            _state = result.State;
            return;
        }

        var detail = result.Detail is null ? result.Problem.ToString() : result.Problem + ": " + result.Detail;

        // Locked: the file was held by another program and the library left the disk as it was. With something left behind (a folder, a copy in
        // the trash) the disk is no longer what the step describes, so that case is permanent like every other problem.
        if (result.Problem == FileOpProblem.Locked && !result.LeftBehind)
        {
            throw new StepDeferredException(detail, null);
        }

        throw new InvalidOperationException(detail);
    }

    // Null when the project folder and its pack folder (if it exists; a missing pack folder is a state the library handles) can be read; else
    // a short English reason. Nothing is changed.
    private static string? UnreachableReason(string projectFolder)
    {
        try
        {
            if (!Directory.Exists(projectFolder))
            {
                return "the project folder cannot be reached: " + projectFolder;
            }

            ProbeListing(projectFolder);
            var packFolder = Path.Combine(projectFolder, ProjectFile.PackFolder);
            if (Directory.Exists(packFolder))
            {
                ProbeListing(packFolder);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "the project folder cannot be read: " + ex.Message;
        }
    }

    // Reads the first entry of a folder: throws when the folder cannot be listed.
    private static void ProbeListing(string folder)
    {
        using var entries = Directory.EnumerateFileSystemEntries(folder).GetEnumerator();
        entries.MoveNext();
    }
}
