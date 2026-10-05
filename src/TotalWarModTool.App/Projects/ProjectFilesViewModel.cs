using System.Globalization;
using System.Text;
using TotalWarModTool.Projects;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// The file commands of a mod project (new table, new loc file, add a pack entry to the project, rename, delete). It asks the user only
/// through <see cref="IFileDialogs"/> and passes the work to the <see cref="ProjectFilesController"/>. A command that really starts (it can
/// start, and its dialog was confirmed) clears <see cref="StatusMessage"/> and sets it again when it ends with something to say; one that
/// cannot start or that the user cancels leaves the last result line alone. Another operation that runs when the command is started says
/// nothing (the menu is disabled with the reason); one that turns up while the dialog is open is reported, so that the input is not lost
/// silently. A refusal that is known before the dialog (a project that cannot be used, a tab with unsaved work) is reported before anything
/// is asked, and so is a refused action.
/// </summary>
public sealed class ProjectFilesViewModel : ObservableObject
{
    private const int MaxShownChars = 300;
    private const int LastSuggestionNumber = 99;
    private const string FallbackName = "new";
    private const string TextExtension = ".tsv";
    private const string LocTextExtension = ".loc.tsv";
    private const string LocRawExtension = ".loc";

    private readonly ProjectFilesController _files;
    private readonly ProjectsController _projects;
    private readonly IFileDialogs _dialogs;
    private readonly AppTexts _texts;
    private string? _statusMessage;

    public ProjectFilesViewModel(ProjectFilesController files, ProjectsController projects, IFileDialogs dialogs, AppTexts texts)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(texts);

        _files = files;
        _projects = projects;
        _dialogs = dialogs;
        _texts = texts;

        // A file that was open is opened again under its new name: the controller asks for that, the rest of the app listens in one place.
        files.OpenRequested += (_, request) => OpenRequested?.Invoke(this, request);
        files.FilesChanged += (_, projectId) => FilesChanged?.Invoke(this, projectId);
        projects.Changed += (_, _) => OnPropertyChanged(nameof(AdoptDisabledReason));
        projects.OperationStateChanged += (_, _) => OnPropertyChanged(nameof(AdoptDisabledReason));
    }

    /// <summary>
    /// Raised with project and file (below pack\, with '\') when a new or added file, or one that was renamed while it was open, should be
    /// opened in a tab.
    /// </summary>
    public event EventHandler<(Guid ProjectId, string RelativeFile)>? OpenRequested;

    /// <summary>Raised with the project after a file action, or the undo or redo of one, changed its files (the explorer lists the open projects again).</summary>
    public event EventHandler<Guid>? FilesChanged;

    /// <summary>The result line of the last command (a note or a success text), for the status bar; null when there is none. It can have several lines.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// The text that says why a new file cannot be created in the project; null when it can. No table to choose from is not a reason: the
    /// dialog of a new table says that, and a loc file needs no schema.
    /// </summary>
    public string? NewFileDisabledReason(Guid projectId) => _files.DisabledReason(projectId);

    /// <summary>The text that says why a pack entry cannot be added to the active project (none is active, it cannot be used, another operation runs); null when it can.</summary>
    public string? AdoptDisabledReason => _projects.Active is { } active ? _files.DisabledReason(active.Id) : _texts.ProjectNoneActive;

    /// <summary>
    /// The text that says why a file of the project cannot be renamed or deleted right now (the project, another operation, or a tab of the
    /// file with unsaved changes or a cell that is being edited: save or close it first); null when it can.
    /// </summary>
    public string? FileActionDisabledReason(Guid projectId, string relativeFile)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);
        return _files.DisabledReason(projectId) ?? _files.OpenTabReason(projectId, relativeFile);
    }

    // ---- the commands ------------------------------------------------------------------------------------------------------------

    /// <summary>Asks for a table and a name, creates the empty table and asks to open it.</summary>
    public async Task NewTableAsync(Guid projectId)
    {
        if (!CanStart(projectId))
        {
            return;
        }

        // The list of the tables is worked out in the background, once per schema, before the dialog opens. Null: it was cancelled.
        if (await _files.GetCreatableTablesAsync() is not { } tables)
        {
            return;
        }

        var suggestion = Suggest(NameOf(projectId), name => tables.Count == 0 ? null : CheckTableResult(projectId, tables[0], name));
        if (_dialogs.AskNewTable(tables, suggestion, input => CheckTable(projectId, input.TableName, input.Name)) is not { } chosen || !StillCanStart())
        {
            return;
        }

        StatusMessage = null;
        Complete(projectId, await _files.CreateTableAsync(projectId, chosen.TableName, chosen.Name), _texts.FileCreatedStatus, open: true);
    }

    /// <summary>Asks for a name, creates the empty loc file in text\db and asks to open it.</summary>
    public async Task NewLocAsync(Guid projectId)
    {
        if (!CanStart(projectId))
        {
            return;
        }

        var folder = NewFiles.LocFolder.Replace('/', '\\');
        FileActionResult? Result(string name) => _files.CheckNewNameResult(projectId, folder, name, LocTextExtension);
        string? Check(string name) => Result(name)?.Message;

        var suggestion = Suggest(NameOf(projectId), Result);
        if (_dialogs.AskName(_texts.FileNewLocTitle, _texts.DialogName, suggestion, Check) is not { } chosen || !StillCanStart())
        {
            return;
        }

        StatusMessage = null;
        Complete(projectId, await _files.CreateLocAsync(projectId, chosen), _texts.FileCreatedStatus, open: true);
    }

    /// <summary>
    /// Adds the entry of a pack to the active project and asks to open it. A table or a loc file asks for its name in the project; the same
    /// name as the entry needs a confirmation (the file then replaces that whole file in the game), and "no" asks for the name again. Every
    /// other entry is added without a question.
    /// </summary>
    public async Task AdoptAsync(PackSource pack, string entryPath)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(entryPath);

        if (_projects.Active is not { } active)
        {
            _dialogs.ShowMessage(_texts.ProjectMessageTitle, _texts.ProjectNoneActive);
            return;
        }

        if (!CanStart(active.Id))
        {
            return;
        }

        string? newName = null;
        var (renamable, currentName) = NewFiles.AdoptName(entryPath);
        if (renamable)
        {
            var (folder, extension, rawExtension) = AdoptTarget(entryPath);

            // The file becomes text when that is lossless and stays as it is otherwise, and the dialog cannot know which: a name is free only
            // when it is free in both forms, so that the dialog never says "free" for a name that the action refuses.
            FileActionResult? Result(string name) => _files.CheckNewNameResult(active.Id, folder, name, extension)
                ?? (_files.CheckNewNameResult(active.Id, folder, name, rawExtension) is { Problem: FileOpProblem.TargetExists } taken ? taken : null);
            string? Check(string name) => Result(name)?.Message;

            var suggestion = Suggest(active.DisplayName, Result);
            while (true)
            {
                if (_dialogs.AskName(_texts.FileAddTitle, _texts.DialogName, suggestion, Check) is not { } chosen)
                {
                    return;
                }

                // The same name puts the file over the one of the game: only on an explicit yes; no goes back to the question. Paths of a pack
                // are case-insensitive for the game, so a name that differs in case only is the same name.
                if (string.Equals(chosen, currentName, StringComparison.OrdinalIgnoreCase) && !_dialogs.ConfirmSameName(Shown(entryPath)))
                {
                    suggestion = chosen;
                    continue;
                }

                newName = chosen;
                break;
            }
        }

        if (!StillCanStart())
        {
            return;
        }

        StatusMessage = null;
        Complete(active.Id, await _files.AdoptAsync(active.Id, pack, entryPath, newName), _texts.FileAddedStatus, open: true);
    }

    /// <summary>
    /// Asks for the new name of the file (the name part; folder and extension stay) and renames it. A tab of the file is opened again under
    /// the new name by the controller; this view model passes that request on.
    /// </summary>
    public async Task RenameAsync(Guid projectId, string relativeFile)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);

        if (!CanStart(projectId, relativeFile))
        {
            return;
        }

        var file = relativeFile.Replace('/', '\\');
        var (folder, name, extension, _) = ProjectFilesController.SplitFile(file);
        if (_dialogs.AskName(_texts.FileRenameTitle, _texts.DialogName, name, input => _files.CheckNewName(projectId, folder, input, extension, file))
            is not { } chosen || !StillCanStart())
        {
            return;
        }

        StatusMessage = null;
        Complete(projectId, await _files.RenameAsync(projectId, file, chosen), renamed => _texts.FileRenamedStatus(Shown(file), renamed), open: false);
    }

    /// <summary>Moves the file to the trash without asking: Undo brings it back, and the status line says so.</summary>
    public async Task DeleteAsync(Guid projectId, string relativeFile)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);

        if (!CanStart(projectId, relativeFile))
        {
            return;
        }

        StatusMessage = null;
        Complete(projectId, await _files.DeleteAsync(projectId, relativeFile), _texts.FileDeletedStatus, open: false);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------

    // False when the command cannot start: another operation runs (nothing to say, as in the other commands), or the project cannot be used or,
    // for a rename or delete of relativeFile, a tab of that file holds work that would be lost (the reason is shown, and no question is
    // asked that could not be answered).
    private bool CanStart(Guid projectId, string? relativeFile = null)
    {
        if (!_projects.CanStartOperation)
        {
            return false;
        }

        if ((_files.DisabledReason(projectId) ?? (relativeFile is null ? null : _files.OpenTabReason(projectId, relativeFile))) is { } reason)
        {
            _dialogs.ShowMessage(_texts.ProjectMessageTitle, reason);
            return false;
        }

        return true;
    }

    // The dialog was confirmed; another operation may have started while it was open. Then the action would not start, so the user is told
    // instead of losing what was typed without a word.
    private bool StillCanStart()
    {
        if (_projects.CanStartOperation)
        {
            return true;
        }

        _dialogs.ShowMessage(_texts.ProjectMessageTitle, _texts.ProjectBusy);
        return false;
    }

    // A refusal is shown as a message; a success becomes the status line (the text and the note of the controller), and a new or added file
    // is opened. A null result means nothing was started or the run was cancelled: nothing to say.
    private void Complete(Guid projectId, FileActionResult? result, Func<string, string> success, bool open)
    {
        if (result is null)
        {
            return;
        }

        if (!result.Ok)
        {
            _dialogs.ShowMessage(_texts.ProjectMessageTitle, result.Message ?? string.Empty);
            return;
        }

        var lines = new[] { success(Shown(result.RelativeFile ?? string.Empty)), result.Note }.Where(line => !string.IsNullOrEmpty(line));
        StatusMessage = string.Join("\n", lines);
        if (open && result.RelativeFile is { } file)
        {
            OpenRequested?.Invoke(this, (projectId, file));
        }
    }

    private string? CheckTable(Guid projectId, string tableName, string name) => CheckTableResult(projectId, tableName, name)?.Message;

    private FileActionResult? CheckTableResult(Guid projectId, string tableName, string name) =>
        _files.CheckNewNameResult(projectId, "db\\" + tableName, name, TextExtension);

    // Where the file of an entry lands in the project and with which extension: a table in db\<table> as .tsv (as it is: no extension), a
    // loc file in the folder of the entry as .loc.tsv (as it is: .loc).
    private static (string Folder, string TextExtension, string RawExtension) AdoptTarget(string entryPath)
    {
        if (DbTablePath.TryGetTableName(entryPath, out var tableName))
        {
            return ("db\\" + tableName, TextExtension, string.Empty);
        }

        var slash = entryPath.LastIndexOf('/');
        return ((slash < 0 ? string.Empty : entryPath[..slash]).Replace('/', '\\'), LocTextExtension, LocRawExtension);
    }

    private string NameOf(Guid projectId) => _projects.Projects.FirstOrDefault(state => state.Id == projectId)?.DisplayName ?? string.Empty;

    // The name that the dialog starts with: the project name as a file name, counted up when it is taken. A name that is refused for another
    // reason (for example too long) gives "new", which is counted up in the same way. "Taken" is the problem of the refusal, not its text.
    private static string Suggest(string projectName, Func<string, FileActionResult?> check)
    {
        static bool Taken(FileActionResult? refusal) => refusal?.Problem == FileOpProblem.TargetExists;

        var name = SuggestedName(projectName);
        var refusal = check(name);
        if (refusal is not null && !Taken(refusal))
        {
            name = FallbackName;
            refusal = check(name);
        }

        if (!Taken(refusal))
        {
            return name;
        }

        for (var number = 2; number <= LastSuggestionNumber; number++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{name}_{number}");
            if (check(candidate) is null)
            {
                return candidate;
            }
        }

        return name;
    }

    // Lower case; every character outside a-z, 0-9 and _ becomes _ (a run of them stays a run); nothing left gives "new".
    private static string SuggestedName(string projectName)
    {
        var name = new StringBuilder(projectName.Length);
        foreach (var c in projectName.ToLowerInvariant())
        {
            name.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' ? c : '_');
        }

        return name.Length == 0 ? FallbackName : name.ToString();
    }

    // Foreign or user text (a path of a pack) as the UI shows it: escaped and cut with the marker of the current language.
    private string Shown(string text) => DisplayText.Safe(text, MaxShownChars, " (" + _texts.TextShortened + ")");
}
