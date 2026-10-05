using System.Globalization;
using System.Text;
using TotalWarModTool.Projects;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// The file commands of a mod project (new table, new loc file, add a pack entry to the project, rename, delete). It asks the user only
/// through <see cref="IFileDialogs"/> and passes the work to the <see cref="ProjectFilesController"/>. Every command first clears
/// <see cref="StatusMessage"/> and sets it again when it ends with something to say. A command that cannot start because another operation
/// runs, or that the user cancels, says nothing; a refused action is shown as a message. A project that cannot be used is reported before
/// anything is asked.
/// </summary>
public sealed class ProjectFilesViewModel : ObservableObject
{
    private const int MaxShownChars = 300;
    private const int LastSuggestionNumber = 99;
    private const string FallbackName = "new";
    private const string TextExtension = ".tsv";
    private const string LocTextExtension = ".loc.tsv";

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
        projects.Changed += (_, _) => OnPropertyChanged(nameof(AdoptDisabledReason));
        projects.OperationStateChanged += (_, _) => OnPropertyChanged(nameof(AdoptDisabledReason));
    }

    /// <summary>
    /// Raised with project and file (below pack\, with '\') when a new or added file, or one that was renamed while it was open, should be
    /// opened in a tab.
    /// </summary>
    public event EventHandler<(Guid ProjectId, string RelativeFile)>? OpenRequested;

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

    /// <summary>The text that says why a file of the project cannot be renamed or deleted right now; null when it can.</summary>
    public string? FileActionDisabledReason(Guid projectId, string relativeFile)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);
        return _files.DisabledReason(projectId);
    }

    // ---- the commands ------------------------------------------------------------------------------------------------------------

    /// <summary>Asks for a table and a name, creates the empty table and asks to open it.</summary>
    public async Task NewTableAsync(Guid projectId)
    {
        StatusMessage = null;
        if (!CanStart(projectId))
        {
            return;
        }

        var tables = _files.CreatableTables;
        var suggestion = Suggest(NameOf(projectId), name => tables.Count == 0 ? null : CheckTable(projectId, tables[0], name));
        if (_dialogs.AskNewTable(tables, suggestion, input => CheckTable(projectId, input.TableName, input.Name)) is not { } chosen)
        {
            return;
        }

        Complete(projectId, await _files.CreateTableAsync(projectId, chosen.TableName, chosen.Name), _texts.FileCreatedStatus, open: true);
    }

    /// <summary>Asks for a name, creates the empty loc file in text\db and asks to open it.</summary>
    public async Task NewLocAsync(Guid projectId)
    {
        StatusMessage = null;
        if (!CanStart(projectId))
        {
            return;
        }

        var folder = NewFiles.LocFolder.Replace('/', '\\');
        string? Check(string name) => _files.CheckNewName(projectId, folder, name, LocTextExtension);

        var suggestion = Suggest(NameOf(projectId), Check);
        if (_dialogs.AskName(_texts.FileNewLocTitle, _texts.DialogName, suggestion, Check) is not { } chosen)
        {
            return;
        }

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

        StatusMessage = null;
        if (_projects.Active is not { } active || !CanStart(active.Id))
        {
            return;
        }

        string? newName = null;
        var (renamable, currentName) = NewFiles.AdoptName(entryPath);
        if (renamable)
        {
            var (folder, extension) = AdoptTarget(entryPath);
            string? Check(string name) => _files.CheckNewName(active.Id, folder, name, extension);

            var suggestion = Suggest(active.DisplayName, Check);
            while (true)
            {
                if (_dialogs.AskName(_texts.FileAddTitle, _texts.DialogName, suggestion, Check) is not { } chosen)
                {
                    return;
                }

                // The same name puts the file over the one of the game: only on an explicit yes; no goes back to the question.
                if (string.Equals(chosen, currentName, StringComparison.Ordinal) && !_dialogs.ConfirmSameName(Shown(entryPath)))
                {
                    suggestion = chosen;
                    continue;
                }

                newName = chosen;
                break;
            }
        }

        Complete(active.Id, await _files.AdoptAsync(active.Id, pack, entryPath, newName), _texts.FileAddedStatus, open: true);
    }

    /// <summary>
    /// Asks for the new name of the file (the name part; folder and extension stay) and renames it. A tab of the file is opened again under
    /// the new name by the controller; this view model passes that request on.
    /// </summary>
    public async Task RenameAsync(Guid projectId, string relativeFile)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);

        StatusMessage = null;
        if (!CanStart(projectId))
        {
            return;
        }

        var file = relativeFile.Replace('/', '\\');
        var (folder, name, extension, _) = ProjectFilesController.SplitFile(file);
        if (_dialogs.AskName(_texts.FileRenameTitle, _texts.DialogName, name, input => _files.CheckNewName(projectId, folder, input, extension, file))
            is not { } chosen)
        {
            return;
        }

        Complete(projectId, await _files.RenameAsync(projectId, file, chosen), renamed => _texts.FileRenamedStatus(Shown(file), renamed), open: false);
    }

    /// <summary>Moves the file to the trash without asking: Undo brings it back, and the status line says so.</summary>
    public async Task DeleteAsync(Guid projectId, string relativeFile)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);

        StatusMessage = null;
        if (!CanStart(projectId))
        {
            return;
        }

        Complete(projectId, await _files.DeleteAsync(projectId, relativeFile), _texts.FileDeletedStatus, open: false);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------

    // False when the command cannot start: another operation runs (nothing to say, as in the other commands) or the project cannot be used
    // (the reason is shown, and no question is asked that could not be answered).
    private bool CanStart(Guid projectId)
    {
        if (!_projects.CanStartOperation)
        {
            return false;
        }

        if (_files.DisabledReason(projectId) is { } reason)
        {
            _dialogs.ShowMessage(_texts.ProjectMessageTitle, reason);
            return false;
        }

        return true;
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

    private string? CheckTable(Guid projectId, string tableName, string name) =>
        _files.CheckNewName(projectId, "db\\" + tableName, name, TextExtension);

    // Where the file of an entry lands in the project and with which extension: a table in db\<table> as .tsv, a loc file in the folder
    // of the entry as .loc.tsv.
    private static (string Folder, string Extension) AdoptTarget(string entryPath)
    {
        if (DbTablePath.TryGetTableName(entryPath, out var tableName))
        {
            return ("db\\" + tableName, TextExtension);
        }

        var slash = entryPath.LastIndexOf('/');
        return ((slash < 0 ? string.Empty : entryPath[..slash]).Replace('/', '\\'), LocTextExtension);
    }

    private string NameOf(Guid projectId) => _projects.Projects.FirstOrDefault(state => state.Id == projectId)?.DisplayName ?? string.Empty;

    // The name that the dialog starts with: the project name as a file name, counted up when it is taken. A name that is refused for another
    // reason (for example too long) gives "new", which is counted up in the same way.
    private string Suggest(string projectName, Func<string, string?> check)
    {
        var name = SuggestedName(projectName);
        var taken = _texts.FileProblemTargetExists;
        var reason = check(name);
        if (reason is not null && reason != taken)
        {
            name = FallbackName;
            reason = check(name);
        }

        if (reason != taken)
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
