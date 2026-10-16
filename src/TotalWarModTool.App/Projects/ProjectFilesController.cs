using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TotalWarModTool.Logging;
using TotalWarModTool.Operations;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// Outcome of a file action. With <paramref name="Ok"/> false, <paramref name="Message"/> says why (a ready UI text) and nothing changed.
/// <paramref name="RelativeFile"/> is the new or renamed file (below pack\, with '\') when there is one. <paramref name="Note"/> is a hint
/// (a ready UI text) the user should see although the action worked, for example a table that stayed binary. <paramref name="Problem"/> is
/// the problem of the file operation behind a refusal (for example <see cref="FileOpProblem.TargetExists"/> for a taken name), so that a caller
/// decides on it instead of comparing texts; null for a success and for a refusal of another kind.
/// </summary>
public sealed record FileActionResult(bool Ok, string? Message, string? RelativeFile = null, string? Note = null, FileOpProblem? Problem = null);

/// <summary>
/// The file operations of a mod project: a new table, a new loc file, a pack entry added to the project, rename and delete. Every action
/// runs through the operation runner, is recorded as one <see cref="FileStep"/> of the global undo history and keeps the open tabs
/// consistent. The write boundary is the one of the library: below pack\ of a project, and the trash. Lives on the UI thread; the work on
/// the pool thread touches only what was read before the run. Failures come back as ready UI texts, never as exceptions.
/// </summary>
public sealed class ProjectFilesController
{
    private const int MaxNameChars = 100;
    private const int MaxCleanupFailureLines = 20;
    private static readonly TimeSpan TrashMaxAge = TimeSpan.FromDays(30);
    private const string TextExtension = ".tsv";
    private const string LocTextExtension = ".loc.tsv";

    private readonly ProjectsController _projects;
    private readonly WorkspaceController _workspace;
    private readonly DocumentsController _documents;
    private readonly ActionHistory _history;
    private readonly IOperationRunner _runner;
    private readonly string _trashRoot;
    private readonly IAppLog _log;
    private readonly AppTexts _texts;
    private readonly TimeProvider _time;

    // The creatable tables for the schema they were worked out for: working them out writes and reads every table of the schema once.
    private GameSchema? _creatableFor;
    private IReadOnlyList<string> _creatable = [];

    /// <param name="trashRoot">The trash of the app (<see cref="AppPaths.TrashDirectory"/>): a fully qualified path.</param>
    public ProjectFilesController(ProjectsController projects, WorkspaceController workspace, DocumentsController documents,
        ActionHistory history, IOperationRunner runner, string trashRoot, IAppLog log, AppTexts texts, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrWhiteSpace(trashRoot);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(time);

        _projects = projects;
        _workspace = workspace;
        _documents = documents;
        _history = history;
        _runner = runner;
        _trashRoot = trashRoot;
        _log = log;
        _texts = texts;
        _time = time;

        projects.Changed += (_, _) => DropStepsOfGoneProjects();
        history.RedoDiscarded += LogDiscardedSteps;
        documents.FileStepApplied += (_, projectId) => FilesChanged?.Invoke(this, projectId);
        documents.FileReopenRequested += (_, request) => OpenRequested?.Invoke(this, request);
    }

    /// <summary>Raised after a file action or the undo or redo of a file step changed the files of a project.</summary>
    public event EventHandler<Guid>? FilesChanged;

    /// <summary>Tells the listeners of <see cref="FilesChanged"/> that something else (the apply of an object) changed the files of the project.</summary>
    public void NotifyFilesChanged(Guid projectId) => FilesChanged?.Invoke(this, projectId);

    /// <summary>
    /// Raised when a file that was open in a tab should be opened again under the name it has now: after a rename and after the undo or redo
    /// of one. The tab of the old name is closed already. The composition root connects it to the opening of a project file.
    /// </summary>
    public event EventHandler<(Guid ProjectId, string RelativeFile)>? OpenRequested;

    /// <summary>
    /// The tables a new file can be created for, sorted, as <see cref="GetCreatableTablesAsync"/> worked them out for the schema that is
    /// loaded now; empty while that has not run for this schema, and while no schema is loaded. Never computes anything.
    /// </summary>
    public IReadOnlyList<string> CreatableTables =>
        _projects.Schema?.Schema is { } schema && ReferenceEquals(schema, _creatableFor) ? _creatable : [];

    /// <summary>
    /// The tables a new file can be created for, sorted. Working them out writes and reads every table of the schema once, so it runs through
    /// the operation runner, once per schema instance (a schema that was loaded again is another instance); the answer is kept for
    /// <see cref="CreatableTables"/>. Empty without a schema (nothing runs then). Null when nothing can be started right now or the run was
    /// cancelled or failed.
    /// </summary>
    public async Task<IReadOnlyList<string>?> GetCreatableTablesAsync()
    {
        if (_projects.Schema?.Schema is not { } schema)
        {
            return [];
        }

        if (ReferenceEquals(schema, _creatableFor))
        {
            return _creatable;
        }

        if (!_projects.CanStartOperation)
        {
            return null;
        }

        // Not through RunAsync: a cancelled run has no list, whatever the work returned.
        var outcome = await _runner.RunAsync(_texts.OperationPrepareTables, (_, _) => NewFiles.CreatableTables(schema, GameTableVersions.Empty)); // Task 7 passes the real versions
        if (outcome.Status != OperationStatus.Completed || outcome.Value is not { } tables)
        {
            return null;
        }

        // Another schema was loaded meanwhile: this list is not the one that belongs to it.
        if (!ReferenceEquals(_projects.Schema?.Schema, schema))
        {
            return null;
        }

        _creatable = tables;
        _creatableFor = schema;
        return tables;
    }

    /// <summary>
    /// Null when no open tab of the file holds work that a rename or delete would lose; else the UI text that says to save or close the tab
    /// first. Nothing is committed or asked.
    /// </summary>
    public string? OpenTabReason(Guid projectId, string relativeFile)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);

        return _documents.FirstTabWithUnsavedWork(_documents.TabsOfFile(projectId, relativeFile.Replace('/', '\\'))) is { } unsaved
            ? _texts.FileTabUnsaved(Shown(unsaved.EntryPath))
            : null;
    }

    /// <summary>
    /// Null when a file action can start for this project; else the UI text that says why not: the project is not in the list or cannot
    /// be used, or another operation runs. A schema where one is needed (<see cref="CreatableTables"/> is empty) is the caller's check.
    /// </summary>
    public string? DisabledReason(Guid projectId)
    {
        if (StateOf(projectId) is not { } state)
        {
            return _texts.ProjectUnknown;
        }

        if (!state.IsUsable)
        {
            return UnusableText(state);
        }

        return _projects.CanStartOperation ? null : _texts.ProjectBusy;
    }

    /// <summary>
    /// Null when <paramref name="name"/> can be the name of a new or renamed file in <paramref name="relativeFolder"/> (below pack\, with '\';
    /// empty for the pack folder) with <paramref name="extension"/> (".tsv", ".loc.tsv", another extension with its dot, or empty); else the
    /// UI text that says why not. A table or loc file name must follow <see cref="NewFiles.NameIssueOf"/>; every file must be a path that
    /// the write boundary accepts and must not be one that a project leaves out of the pack, and nothing may be there already.
    /// </summary>
    public string? CheckNewName(Guid projectId, string relativeFolder, string name, string extension, string? currentFile = null) =>
        CheckNewNameResult(projectId, relativeFolder, name, extension, currentFile)?.Message;

    /// <summary>
    /// <see cref="CheckNewName"/> as a refusal: null when the name can be used; else a result with the UI text and, for a name that is taken,
    /// <see cref="FileActionResult.Problem"/> <see cref="FileOpProblem.TargetExists"/>.
    /// </summary>
    public FileActionResult? CheckNewNameResult(Guid projectId, string relativeFolder, string name, string extension, string? currentFile = null)
    {
        ArgumentNullException.ThrowIfNull(relativeFolder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(extension);

        return StateOf(projectId) is not { } state ? Fail(_texts.ProjectUnknown) : NameCheck(state.Folder, relativeFolder, name, extension, currentFile);
    }

    // ---- the actions -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Creates an empty table <c>db\&lt;tableName&gt;\&lt;name&gt;.tsv</c> in the newest version of the schema. Null when nothing can be started
    /// right now or the run was cancelled or failed.
    /// </summary>
    public async Task<FileActionResult?> CreateTableAsync(Guid projectId, string tableName, string name)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(name);

        if (Gate(projectId, out var refusal) is not { } state)
        {
            return refusal;
        }

        if (_projects.Schema?.Schema is not { } schema)
        {
            return Fail(_texts.FileNoSchema);
        }

        return await WriteNewFileAsync(state, _texts.OperationCreateFile(Shown(name)), Shown(name), _ => NewFiles.Table(schema, GameTableVersions.Empty, tableName, name)); // Task 7 passes the real versions
    }

    /// <summary>Creates an empty loc file <c>text\db\&lt;name&gt;.loc.tsv</c>. Null when nothing can be started right now or the run was cancelled or failed.</summary>
    public async Task<FileActionResult?> CreateLocAsync(Guid projectId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Gate(projectId, out var refusal) is not { } state)
        {
            return refusal;
        }

        return await WriteNewFileAsync(state, _texts.OperationCreateFile(Shown(name)), Shown(name), _ => NewFiles.Loc(name));
    }

    /// <summary>
    /// Adds the entry of a pack to the project: the bytes are read through the runner (a read problem stops the action with a message and
    /// writes nothing), then the file is made and written in a second run. A table or loc file becomes text when that is lossless, else it
    /// stays as it is and <see cref="FileActionResult.Note"/> says why. <paramref name="newName"/> renames a table or loc file; null keeps
    /// the name. Null when nothing can be started right now or a run was cancelled or failed.
    /// </summary>
    public async Task<FileActionResult?> AdoptAsync(Guid projectId, PackSource pack, string entryPath, string? newName)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(entryPath);

        if (Gate(projectId, out var refusal) is null)
        {
            return refusal;
        }

        var content = await _workspace.ReadEntryBytesAsync(pack, entryPath);
        if (content is null)
        {
            return null;
        }

        if (content.Problem != EntryProblem.None || content.Bytes is not { } bytes)
        {
            // The workspace logged the read problem already.
            var problem = content.Problem == EntryProblem.None ? EntryProblem.Corrupt : content.Problem;
            return Fail(MessageText.WithDetail(_texts.EntryProblemText(problem), ShownDetail(content.Detail)));
        }

        // The runner of the read has ended; what can start now is looked at again, and the project is looked up again.
        if (Gate(projectId, out refusal) is not { } state)
        {
            return refusal;
        }

        var schema = _projects.Schema?.Schema;
        var shownName = Shown(newName ?? entryPath[(entryPath.LastIndexOf('/') + 1)..]);
        return await WriteNewFileAsync(state, _texts.OperationAddFile(shownName), shownName, _ => NewFiles.Adopt(entryPath, bytes, schema, GameTableVersions.Empty, newName)); // Task 7 passes the real versions
    }

    /// <summary>
    /// Renames a file of the project: only the name part changes; the folder and the extension (".tsv", ".loc.tsv" or the one it has)
    /// stay, and so does the content. A tab of the file is closed and the file is opened again under the new name
    /// (<see cref="OpenRequested"/>). A file with unsaved changes in a tab is not renamed. Null when nothing can be started right now or
    /// the run was cancelled or failed.
    /// </summary>
    public async Task<FileActionResult?> RenameAsync(Guid projectId, string relativeFile, string newName)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);
        ArgumentNullException.ThrowIfNull(newName);

        if (Gate(projectId, out var refusal) is not { } state)
        {
            return refusal;
        }

        relativeFile = relativeFile.Replace('/', '\\');
        var (folder, _, extension, kind) = SplitFile(relativeFile);
        if (NameCheck(state.Folder, folder, newName, extension, relativeFile) is { } nameRefusal)
        {
            return nameRefusal;
        }

        var target = JoinFile(folder, newName + extension);
        if (ProjectLayout.FromRelativeFile(target).Kind != kind)
        {
            return Fail(_texts.FileRenameChangesKind);
        }

        if (OpenTabReason(projectId, relativeFile) is { } tabReason)
        {
            return Fail(tabReason);
        }

        var projectFolder = state.Folder;
        var run = await RunAsync(_texts.OperationRenameFile(Shown(FileNameOf(relativeFile))), Shown(FileNameOf(relativeFile)),
            _ => ProjectFileOps.Rename(projectFolder, relativeFile, target));
        if (run is null)
        {
            return null;
        }

        if (run.Problem != FileOpProblem.None)
        {
            return Failed("Rename", relativeFile, run.Problem, run.Detail, _texts.FileProblemText(run.Problem));
        }

        // The tabs are looked up again: the files of the tabs do not change while the main window is locked, but the list is the truth.
        var open = _documents.TabsOfFile(projectId, relativeFile);
        _documents.CloseTabsOfFile(open);
        _history.Record(RenamedStep(state, relativeFile, target));
        FilesChanged?.Invoke(this, projectId);
        if (open.Count > 0)
        {
            OpenRequested?.Invoke(this, (projectId, target));
        }

        return new FileActionResult(true, null, target);
    }

    /// <summary>
    /// Moves a file of the project to the trash. A tab of the file is closed; a file with unsaved changes in a tab is not deleted. Undo
    /// brings the same bytes back. Null when nothing can be started right now or the run was cancelled or failed.
    /// </summary>
    public async Task<FileActionResult?> DeleteAsync(Guid projectId, string relativeFile)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);

        if (Gate(projectId, out var refusal) is not { } state)
        {
            return refusal;
        }

        relativeFile = relativeFile.Replace('/', '\\');
        if (OpenTabReason(projectId, relativeFile) is { } tabReason)
        {
            return Fail(tabReason);
        }

        var projectFolder = state.Folder;
        var trashRoot = _trashRoot;
        var time = _time;
        var run = await RunAsync(_texts.OperationDeleteFile(Shown(FileNameOf(relativeFile))), Shown(FileNameOf(relativeFile)),
            _ => ProjectTrash.MoveToTrash(trashRoot, projectId, projectFolder, relativeFile, time));
        if (run is null)
        {
            return null;
        }

        if (run.File is not { } trashed)
        {
            return Failed("Delete", relativeFile, run.Problem, run.Detail, _texts.TrashProblemText(run.Problem));
        }

        _documents.CloseTabsOfFile(_documents.TabsOfFile(projectId, relativeFile));
        _history.Record(DeletedStep(state, relativeFile, trashed));
        FilesChanged?.Invoke(this, projectId);
        return new FileActionResult(true, null, relativeFile);
    }

    // ---- shared steps of the actions -------------------------------------------------------------------------------------------------

    // What the pool thread made of a new file: the file (or why it cannot be made) and, when there is one, the outcome of writing it.
    private sealed record NewFileRun(NewFileResult Made, FileOpResult? Created);

    // Makes a new file on the pool thread, writes it without overwriting, and records the step. Used by the new table, the new loc file and
    // the adopted entry: they differ only in how the file is made.
    private async Task<FileActionResult?> WriteNewFileAsync(ProjectState state, string title, string shownName, Func<CancellationToken, NewFileResult> make)
    {
        var projectFolder = state.Folder;
        var run = await RunAsync(title, shownName, ct =>
        {
            var made = make(ct);
            return made.File is { } file
                ? new NewFileRun(made, ProjectFileOps.Create(projectFolder, file.RelativeFile, file.Content, ct))
                : new NewFileRun(made, null);
        });
        if (run is null)
        {
            return null;
        }

        if (run.Made.File is not { } newFile)
        {
            var reason = run.Made.Reason ?? string.Empty;
            _log.Warn($"A file was not created in the project \"{DisplayText.Safe(state.DisplayName, MaxNameChars)}\": {DisplayText.Safe(reason)}");
            return Fail(_texts.FileCannotCreate(Shown(reason)));
        }

        var created = run.Created!;
        if (created.Problem != FileOpProblem.None)
        {
            return Failed("Create", newFile.RelativeFile, created.Problem, created.Detail, _texts.FileProblemText(created.Problem));
        }

        _history.Record(CreatedStep(state, newFile.RelativeFile, created));
        FilesChanged?.Invoke(this, state.Id);
        return new FileActionResult(true, null, newFile.RelativeFile, newFile.Note is { } note ? _texts.FileAddedBinary(Shown(note)) : null);
    }

    // Runs work through the runner, with one progress report so that the window names the file. The value counts, whatever the status says:
    // work that ended with a result happened (a cancel that came too late); a cancelled or failed run has none. A failed run was reported by
    // the runner already.
    private async Task<T?> RunAsync<T>(string title, string shownName, Func<CancellationToken, T> work)
        where T : class
    {
        var outcome = await _runner.RunAsync(title, (progress, ct) =>
        {
            progress.Report(new OperationProgress(0, null, shownName));
            return work(ct);
        });
        return outcome.Value;
    }

    // The project of the action. Null with a refusal: the project is not in the list or cannot be used. Null without one: another operation
    // runs, so nothing starts.
    private ProjectState? Gate(Guid projectId, out FileActionResult? refusal)
    {
        refusal = null;
        if (StateOf(projectId) is not { } state)
        {
            refusal = Fail(_texts.ProjectUnknown);
            return null;
        }

        if (!state.IsUsable)
        {
            refusal = Fail(UnusableText(state));
            return null;
        }

        return _projects.CanStartOperation ? state : null;
    }

    private ProjectState? StateOf(Guid projectId) => _projects.Projects.FirstOrDefault(state => state.Id == projectId);

    private string UnusableText(ProjectState state) => MessageText.WithDetail(_texts.ProjectProblemText(state.Problem), ShownDetail(state.Detail));

    private static FileActionResult Fail(string message, FileOpProblem? problem = null) => new(false, message, Problem: problem);

    // A problem of the library as a ready message (the text of the problem and the technical detail behind it, escaped), and a log line.
    private FileActionResult Failed(string action, string relativeFile, FileOpProblem problem, string? detail, string text)
    {
        _log.Warn($"{action} of \"{DisplayText.Safe(relativeFile)}\" failed ({problem}): {DisplayText.Safe(detail ?? string.Empty)}");
        return Fail(MessageText.WithDetail(text, ShownDetail(detail)), problem);
    }

    // Foreign or user text (a path, a technical detail) as the UI shows it: escaped and cut with the marker of the current language.
    private string Shown(string text, int maxChars = 300) => DisplayText.Safe(text, maxChars, " (" + _texts.TextShortened + ")");

    private string? ShownDetail(string? detail) => string.IsNullOrEmpty(detail) ? null : Shown(detail);

    // File steps of a project that left the list are not executable: without its folder, nothing can be created, trashed or renamed. A file
    // that such a step holds in the trash stays there with nothing to bring it back, so its place is logged.
    private void DropStepsOfGoneProjects()
    {
        var present = _projects.Projects.Select(state => state.Id).ToHashSet();
        var left = new List<TrashedFile>();
        _history.RemoveWhere(step =>
        {
            if (step is not FileStep fileStep || present.Contains(fileStep.ProjectId))
            {
                return false;
            }

            if (fileStep.InTrash is { } inTrash)
            {
                left.Add(inTrash);
            }

            return true;
        });

        foreach (var trashed in left)
        {
            _log.Warn($"The file \"{DisplayText.Safe(trashed.RelativeFile)}\" of a project that left the list stays in the trash: {DisplayText.Safe(trashed.TrashFile)}");
        }
    }

    // ---- the trash -------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Deletes the entries of the trash that are older than 30 days (<see cref="ProjectTrash.CleanupDetailed"/>). Silent housekeeping for the
    /// start of the app: it runs on a pool thread, not through the operation runner (no window, no lock of the main window), logs the number of
    /// removed entries when there are any and one line per entry that could not be deleted (at most <see cref="MaxCleanupFailureLines"/>, then
    /// their number), and never throws for the file system: a problem with the trash is a log line, the app goes on.
    /// </summary>
    public async Task CleanupTrashAsync()
    {
        var (trashRoot, time) = (_trashRoot, _time);
        try
        {
            var result = await Task.Run(() => ProjectTrash.CleanupDetailed(trashRoot, TrashMaxAge, time, CancellationToken.None));
            if (result.Deleted > 0)
            {
                _log.Info($"The cleanup of the trash removed {result.Deleted} entries older than {TrashMaxAge.Days} days.");
            }

            foreach (var failure in result.Failed.Take(MaxCleanupFailureLines))
            {
                _log.Warn($"The cleanup of the trash could not delete the entry {DisplayText.Safe(failure.Entry)}: {DisplayText.Safe(failure.Reason)}");
            }

            if (result.Failed.Count > MaxCleanupFailureLines)
            {
                _log.Warn($"The cleanup of the trash could not delete {result.Failed.Count - MaxCleanupFailureLines} further entries.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _log.Warn($"The cleanup of the trash failed: {DisplayText.Safe(ex.Message)}");
        }
    }

    // A new step clears the redo stack without asking. A file step that was undone holds its file in the trash and waits there for its redo;
    // with the step gone nothing brings the file back, so its place is logged (the user can fetch it by hand).
    private void LogDiscardedSteps(IReadOnlyList<IUndoableAction> steps)
    {
        foreach (var step in steps)
        {
            if (step is FileStep { InTrash: { } trashed })
            {
                _log.Warn($"The file \"{DisplayText.Safe(trashed.RelativeFile)}\" stays in the trash, the step that brings it back was discarded by a new step: {DisplayText.Safe(trashed.TrashFile)}");
            }
        }
    }

    // ---- names -----------------------------------------------------------------------------------------------------------------------

    // The check behind CheckNewName. ownFile is the file that is renamed, if one is: the name it has already is "unchanged", and a name that
    // differs from it only in case is no collision with itself. Null when the name can be used, else the refusal.
    private FileActionResult? NameCheck(string projectFolder, string relativeFolder, string name, string extension, string? ownFile)
    {
        // Callers may give either separator (the entry path of a tab uses '/'): the candidate is compared with the current file as '\' text.
        relativeFolder = relativeFolder.Replace('/', '\\');
        var candidate = JoinFile(relativeFolder, name + extension);
        if (ownFile is not null && string.Equals(candidate, ownFile.Replace('/', '\\'), StringComparison.Ordinal))
        {
            return Fail(_texts.FileNameUnchanged);
        }

        var isText = extension.Equals(TextExtension, StringComparison.OrdinalIgnoreCase)
            || extension.Equals(LocTextExtension, StringComparison.OrdinalIgnoreCase);
        var issue = isText ? NewFiles.NameIssueOf(name) : NewFiles.PlainNameIssueOf(name, extension);
        if (issue != NameIssue.None)
        {
            return Fail(_texts.FileNameIssueText(issue, isText ? NewFiles.MaxNameLength : NewFiles.MaxPlainNameLength(extension)));
        }

        // A path that the write boundary refuses (too long, a character or a name Windows does not allow): the sentence says what to do, the
        // English detail of the library stays as technical detail behind it.
        var check = PackFolderPaths.ResolveForWrite(projectFolder, candidate);
        if (check.Problem == PackPathProblem.UnsafePath)
        {
            return Fail(MessageText.WithDetail(_texts.FileProblemUnsafePath, ShownDetail(check.Detail)), FileOpProblem.UnsafePath);
        }

        if (check.Problem == PackPathProblem.Link)
        {
            return Fail(_texts.FileProblemLink, FileOpProblem.Link);
        }

        var onlyCaseDiffers = ownFile is not null && SameFile(candidate, ownFile);   // the exactly equal name returned above
        return !onlyCaseDiffers && ProjectFileOps.Exists(projectFolder, candidate) ? Fail(_texts.FileProblemTargetExists, FileOpProblem.TargetExists) : null;
    }

    private static bool SameFile(string a, string b) =>
        string.Equals(a.Replace('/', '\\'), b.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);

    private static string JoinFile(string folder, string fileName) => folder.Length == 0 ? fileName : folder.TrimEnd('\\') + "\\" + fileName;

    private static string FileNameOf(string relativeFile) => relativeFile[(relativeFile.LastIndexOfAny(['/', '\\']) + 1)..];

    // The parts of a file that a rename keeps (folder, extension and the kind that the project takes the file for) and the name part it replaces.
    // Internal: the rename dialog asks for the name part and checks it against the same split.
    internal static (string Folder, string Name, string Extension, ProjectFileKind Kind) SplitFile(string relativeFile)
    {
        var file = relativeFile.Replace('/', '\\');
        var slash = file.LastIndexOf('\\');
        var fileName = file[(slash + 1)..];
        var kind = ProjectLayout.FromRelativeFile(file).Kind;
        var extension = kind switch
        {
            ProjectFileKind.Table => fileName[^TextExtension.Length..],
            ProjectFileKind.Loc => fileName[^LocTextExtension.Length..],
            _ => Path.GetExtension(fileName),
        };

        // A name that is nothing but a dot and letters (".gitkeep") has no extension in the sense of a rename.
        if (extension.Length == fileName.Length)
        {
            extension = string.Empty;
        }

        return (slash < 0 ? string.Empty : file[..slash], fileName[..^extension.Length], extension, kind);
    }

    // ---- the undo steps --------------------------------------------------------------------------------------------------------------

    // A new file: undo moves it to the trash and removes the folders the creation made, redo brings it back (and notes the folders that makes).
    private FileStep CreatedStep(ProjectState state, string file, FileOpResult created)
    {
        var (projectId, folder, trashRoot, time) = (state.Id, state.Folder, _trashRoot, _time);
        return new FileStep(projectId, "Create " + file, [file], [], new FileStepState(null, created.CreatedFolders, created.Fingerprint),
            doWork: current => BringBack(trashRoot, folder, current),
            undoWork: current => MoveAway(trashRoot, projectId, folder, file, time, current, removeCreatedFolders: true),
            projectFolder: folder);
    }

    // A deleted file: undo brings it back from the trash, redo moves it there again (a new entry of the trash; the step remembers the last).
    private FileStep DeletedStep(ProjectState state, string file, TrashedFile trashed)
    {
        var (projectId, folder, trashRoot, time) = (state.Id, state.Folder, _trashRoot, _time);
        return new FileStep(projectId, "Delete " + file, [], [file], new FileStepState(trashed, [], null),
            doWork: current => MoveAway(trashRoot, projectId, folder, file, time, current, removeCreatedFolders: false),
            undoWork: current => BringBack(trashRoot, folder, current),
            projectFolder: folder);
    }

    // A renamed file: undo renames it back, redo renames it again. The content is never touched.
    private FileStep RenamedStep(ProjectState state, string from, string to)
    {
        var folder = state.Folder;
        return new FileStep(state.Id, "Rename " + from + " to " + to, [to], [from], FileStepState.Empty,
            doWork: current => Renamed(ProjectFileOps.Rename(folder, from, to), current),
            undoWork: current => Renamed(ProjectFileOps.Rename(folder, to, from), current),
            projectFolder: folder);
    }

    // Moves the file to the trash, but only while it is the file the step left in the project: a step never puts content into the trash that it
    // did not put on disk itself (a saved edit, a file another program wrote). Otherwise it throws and the history drops the step.
    private static FileStepResult MoveAway(string trashRoot, Guid projectId, string folder, string file, TimeProvider time, FileStepState current,
        bool removeCreatedFolders)
    {
        RequireUnchanged(folder, file, current.Placed);
        var moved = ProjectTrash.MoveToTrash(trashRoot, projectId, folder, file, time);
        if (moved.File is not { } trashed)
        {
            return new FileStepResult(moved.Problem, moved.Detail, current, moved.LeftBehind, moved.LeftCopy);
        }

        if (removeCreatedFolders)
        {
            ProjectFileOps.RemoveEmptyFolders(folder, current.CreatedFolders);
        }

        return new FileStepResult(FileOpProblem.None, null, new FileStepState(trashed, [], null));
    }

    // Brings the file back from the trash. When that fails the file stays in the trash; the step still holds it, and the message of a dropped
    // step lists its place.
    private static FileStepResult BringBack(string trashRoot, string folder, FileStepState current)
    {
        if (current.Trash is not { } trashed)
        {
            return new FileStepResult(FileOpProblem.SourceMissing, "the step has no file in the trash", current);
        }

        var restored = ProjectTrash.Restore(trashed, folder, trashRoot);
        return restored.Problem == FileOpProblem.None
            ? new FileStepResult(FileOpProblem.None, null, new FileStepState(null, restored.CreatedFolders, restored.Fingerprint))
            : new FileStepResult(restored.Problem, "the file stays in the trash: " + restored.Detail, current, restored.LeftBehind);
    }

    private static void RequireUnchanged(string folder, string file, FileFingerprint? placed)
    {
        FileProbe probe;
        try
        {
            probe = ProjectFileWriter.Probe(folder, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new FileStepChangedException(file);
        }

        // A file that cannot be told (a link, a folder in its place) or whose state the step never knew is not the file the step left.
        if (probe.State != FileProbeState.Present || placed is null || !Equals(probe.Fingerprint, placed))
        {
            throw new FileStepChangedException(file);
        }
    }

    private static FileStepResult Renamed(FileOpResult result, FileStepState current) => new(result.Problem, result.Detail, current, result.LeftBehind);
}
