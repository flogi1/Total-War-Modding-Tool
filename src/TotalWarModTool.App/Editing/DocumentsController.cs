using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using TotalWarModTool.Editing;
using TotalWarModTool.Logging;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// The editable documents of the open tabs: decides which project files open as an <see cref="EditableRowsTabViewModel"/>, runs the global
/// undo and redo across the tabs, forgets the steps of a tab that is closed, follows which tabs are modified, saves them and guards
/// unsaved changes (closing a tab or the app, removing a project, building). Lives on the UI thread. Sets <see cref="TabsViewModel.SpecialTab"/>
/// and <see cref="TabsViewModel.CanClose"/> of its tabs.
/// </summary>
public sealed partial class DocumentsController
{
    // A technical message in a UI message shows at most this many characters.
    private const int MaxDetailChars = 300;

    private readonly TabsViewModel _tabs;
    private readonly ProjectsController _projects;
    private readonly ActionHistory _history;
    private readonly IOperationRunner _runner;
    private readonly IEditDialogs _dialogs;
    private readonly IAppLog _log;
    private readonly AppTexts _texts;
    private readonly RelayCommand _undoCommand;
    private readonly RelayCommand _redoCommand;
    private readonly RelayCommand _saveCommand;
    private readonly RelayCommand _saveAllCommand;

    // The editable tabs this controller listens to (to their IsModified) and the ones that were modified at the last check.
    private readonly List<EditableRowsTabViewModel> _tracked = [];
    private HashSet<EditableRowsTabViewModel> _modified = [];

    /// <param name="projects">
    /// Raises <see cref="ProjectsController.OperationStateChanged"/>, which tells the commands to ask again; gives the folder of a project, to
    /// save its files into, and says whether an operation may start.
    /// </param>
    /// <param name="runner">While it runs an operation, undo, redo and save are disabled. Saving runs through it.</param>
    public DocumentsController(TabsViewModel tabs, ProjectsController projects, ActionHistory history, IOperationRunner runner,
        IEditDialogs dialogs, IAppLog log, AppTexts texts)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(texts);

        _tabs = tabs;
        _projects = projects;
        _history = history;
        _runner = runner;
        _dialogs = dialogs;
        _log = log;
        _texts = texts;
        _undoCommand = new RelayCommand(() => Step(undo: true), () => CanStep(undo: true));
        _redoCommand = new RelayCommand(() => Step(undo: false), () => CanStep(undo: false));
        _saveCommand = new RelayCommand(SaveSelected, CanSaveSelected);
        _saveAllCommand = new RelayCommand(SaveAllModified, CanSaveAll);

        tabs.SpecialTab = CreateEditableTab;
        tabs.CanClose = CanCloseTab;
        tabs.CanLeave = CanLeaveTab;
        tabs.Tabs.CollectionChanged += OnTabsCollectionChanged;
        tabs.Changed += (_, _) => RaiseSaveCommandsChanged();
        history.Changed += RaiseCommandsChanged;
        projects.OperationStateChanged += (_, _) => RaiseCommandsChanged();
        SyncTabs();
    }

    /// <summary>The tabs the controller works on. <see cref="MainViewModel"/> takes them from here, so that both use the same list.</summary>
    public TabsViewModel Tabs => _tabs;

    /// <summary>Reverts the latest step of the history and selects the tab it belongs to. Disabled while an operation runs and when there is no step.</summary>
    public ICommand UndoCommand => _undoCommand;

    /// <summary>Repeats the latest undone step and selects the tab it belongs to. Disabled while an operation runs and when there is no step.</summary>
    public ICommand RedoCommand => _redoCommand;

    /// <summary>The open editable tabs, in the order of the tab list.</summary>
    public IReadOnlyList<EditableRowsTabViewModel> EditableTabs => _tabs.Tabs.OfType<EditableRowsTabViewModel>().ToList();

    /// <summary>True when at least one editable tab differs from its last save.</summary>
    public bool HasModifiedTabs => _tracked.Any(tab => tab.IsModified);

    /// <summary>Raised when the set of modified tabs changed: a tab was modified or saved, or a modified tab left.</summary>
    public event EventHandler? ModifiedChanged;

    /// <summary>
    /// Raised when a cell of an editable tab went into or out of edit mode with a text that differs from the value (the state that
    /// <see cref="EditableRowsTabViewModel.HasPendingEdit"/> tells). The reason why a project cannot leave the list (<see cref="UnsavedReason"/>)
    /// depends on it, although the set of modified tabs does not change.
    /// </summary>
    public event EventHandler? PendingEditChanged;

    // ---- opening ---------------------------------------------------------------------------------------------------------------------

    // An entry of a project that is a table or loc file in text form and was read without a problem gets an editable tab; everything else
    // keeps the tab the factory makes (null). Called for every entry that is opened, so it also notes the state of the file of a project
    // entry for the tab that is made next (see Track).
    private TabViewModel? CreateEditableTab(TabSource source, OpenedEntry entry, Action<TabViewModel> close)
    {
        if (!TabSource.TryGetProjectId(source.Key, out var projectId))
        {
            return null;
        }

        NoteOpening(projectId, source, entry);
        if (entry.ReadProblem != EntryProblem.None)
        {
            return null;
        }

        try
        {
            if (CreateDocument(entry) is not { } document)
            {
                return null;
            }

            return new EditableRowsTabViewModel(source, entry.EntryPath, _texts, close, document, _history, _tabs.Clipboard)
            {
                ReloadHandler = tab => RunDetached(() => ReloadAsync(tab), "Reloading a tab"),
            };
        }
        catch (ArgumentException ex)
        {
            // An unsupported field type or a row with a wrong cell count: the file is shown read-only instead.
            _log.Warn($"The file \"{DisplayText.Safe(entry.EntryPath)}\" cannot be edited and is shown read-only: {DisplayText.Safe(ex.Message)}");
            return null;
        }
    }

    // The document of an entry that can be edited (a table or loc file in text form that was decoded), else null. Throws ArgumentException for
    // content that the document cannot carry.
    private static RowsDocument? CreateDocument(OpenedEntry entry)
    {
        var (packPath, kind) = ProjectLayout.FromRelativeFile(entry.EntryPath.Replace('/', '\\'));
        switch (kind)
        {
            case ProjectFileKind.Table when entry.Table is { Status: TableDecodeStatus.Ok, Definition: { } definition } table:
                return RowsDocument.ForTable(table.TableName, table.Version, packPath, definition, table.Rows);
            case ProjectFileKind.Loc when entry.Loc is { Status: LocDecodeStatus.Ok } loc:
                return RowsDocument.ForLoc(packPath, loc.Rows);
            default:
                return null;
        }
    }

    // ---- undo and redo ---------------------------------------------------------------------------------------------------------------

    private bool CanStep(bool undo) => !_runner.IsRunning && (undo ? _history.CanUndo : _history.CanRedo);

    private void Step(bool undo)
    {
        // Also guarded here: a direct call must not reach the history with an empty stack (that throws) or during an operation.
        if (!CanStep(undo))
        {
            return;
        }

        // A value that is typed into a cell is committed first: it becomes the latest step, so an undo takes back what the user just typed,
        // never an older change under it. A value that cannot be committed stops the step, and nothing is dropped. The commit changes the
        // history, so the step is asked for again.
        if (!CommitPendingEdits(EditableTabs) || !CanStep(undo))
        {
            return;
        }

        var next = undo ? _history.NextUndo : _history.NextRedo;

        // A file step that would take away the file of a tab with unsaved work does not run and stays in the history.
        FileStepPlan? plan = null;
        if (next is FileStep fileStep && (plan = PlanFileStep(fileStep, undo)) is null)
        {
            return;
        }

        IUndoableAction step;
        try
        {
            step = undo ? _history.Undo() : _history.Redo();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // ActionHistory has dropped the step and raised Changed; what it left stays as it is.
            var what = undo ? "Undo" : "Redo";
            _log.Error($"{what} of \"{DisplayText.Safe(next?.Description ?? string.Empty)}\" failed.", ex);
            var reason = DisplayText.Safe(ex.Message, MaxDetailChars, " (" + _texts.TextShortened + ")");

            // A file step that was dropped while it holds a file in the trash: nothing will bring that file back, so the log says where it is.
            if (next is FileStep { InTrash: { } trashed })
            {
                _log.Warn($"The file \"{DisplayText.Safe(trashed.RelativeFile)}\" stays in the trash, the step that brings it back was removed: {DisplayText.Safe(trashed.TrashFile)}");
            }

            // The disk is not what the steps of that file describe any more: the other steps of the file go too, and the message says so once.
            var droppedStep = next as FileStep;
            var furtherRemoved = droppedStep is not null && DropRelatedFileSteps(droppedStep);

            // A step that refused to trash a file it did not leave says that the file was left as it is; the generic text would not.
            var message = ex is FileStepChangedException changed
                ? _texts.FileStepFileChanged(Shown(changed.RelativeFile))
                : undo ? _texts.EditUndoFailedMessage(reason) : _texts.EditRedoFailedMessage(reason);
            if (furtherRemoved && WorkingFileOf(droppedStep!, undo) is { } workingFile)
            {
                message += " " + _texts.FileFurtherStepsRemoved(Shown(workingFile));
            }
            _dialogs.ShowMessage(undo ? _texts.EditUndoFailedTitle : _texts.EditRedoFailedTitle, message);
            return;
        }

        // Outside the try: the step has worked, so a failure to select its tab is no failed step.
        if (plan is not null)
        {
            ApplyFileStep(plan);
        }

        SelectTabOf(step);
    }

    // After the step ran: the tab of its document is shown, so the user sees what changed. A document without a tab (closed) selects nothing.
    private void SelectTabOf(IUndoableAction step)
    {
        if (step is DocumentEdit edit && _tabs.Tabs.OfType<EditableRowsTabViewModel>().FirstOrDefault(tab => ReferenceEquals(tab.Document, edit.Document)) is { } tab)
        {
            _tabs.SelectedTab = tab;
        }
    }

    private void RaiseCommandsChanged()
    {
        _undoCommand.RaiseCanExecuteChanged();
        _redoCommand.RaiseCanExecuteChanged();
        RaiseSaveCommandsChanged();
    }

    // ---- tabs and modified state -----------------------------------------------------------------------------------------------------

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncTabs();

    // Compares the editable tabs of the list with the ones listened to, so that every kind of change of the list (a Reset has no old items)
    // is handled the same way.
    private void SyncTabs()
    {
        var current = _tabs.Tabs.OfType<EditableRowsTabViewModel>().ToList();
        foreach (var gone in _tracked.Where(tab => !current.Contains(tab)).ToList())
        {
            _tracked.Remove(gone);
            gone.PropertyChanged -= OnTabPropertyChanged;

            // A closed document no longer exists, so its steps are gone, whichever way the tab was closed.
            var document = gone.Document;
            _history.RemoveWhere(step => step is DocumentEdit edit && ReferenceEquals(edit.Document, document));
        }

        foreach (var added in current.Where(tab => !_tracked.Contains(tab)))
        {
            _tracked.Add(added);
            added.PropertyChanged += OnTabPropertyChanged;
        }

        SyncFingerprints();
        UpdateModified();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditableRowsTabViewModel.IsModified) or null)
        {
            UpdateModified();
        }

        // A cell in edit mode enables saving (the save commits it first), so the commands ask again when one starts or ends.
        if (e.PropertyName is nameof(EditableRowsTabViewModel.HasPendingEdit) or null)
        {
            RaiseSaveCommandsChanged();
            PendingEditChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateModified()
    {
        var modified = _tracked.Where(tab => tab.IsModified).ToHashSet();
        if (!modified.SetEquals(_modified))
        {
            _modified = modified;
            RaiseSaveCommandsChanged();
            ModifiedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
