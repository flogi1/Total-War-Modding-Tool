using System.IO;
using System.Threading;
using System.Windows.Input;
using TotalWarModTool.Operations;
using TotalWarModTool.Schema;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>View model of the main window.</summary>
public sealed class MainViewModel : ObservableObject
{
    private const int SlowStepCount = 100;
    private static readonly TimeSpan SlowStepDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan FailDelay = TimeSpan.FromSeconds(1);

    private readonly WorkspaceController _workspace;
    private readonly ProjectsController? _projects;
    private readonly ProjectFilesViewModel? _projectFiles;
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _newTableCommand;
    private readonly RelayCommand _newLocCommand;
    private ResolvedSchema? _seenSchema;

    // Which of the two view models that report to the project status item said something last (the one of the file commands, else the one of the project commands).
    private bool _fileStatusIsNewer;

    /// <param name="openFolder">Opens a folder in the file explorer. Passed in so the view model stays free of processes and tests need no explorer.</param>
    /// <param name="progress">The runner every user-started operation goes through. The workspace and the debug commands use it.</param>
    /// <param name="openSettings">Opens the settings window (or activates it if it is open). Passed in so the view model knows no windows.</param>
    /// <param name="workspace">The state of the game. The explorer, the tabs and the status line are built on it.</param>
    /// <param name="openGameSettings">Opens the settings window on the page "Game". The explorer uses it for its button; null: the explorer uses <paramref name="openSettings"/>.</param>
    /// <param name="projects">The mod projects of the game; null: the app has no projects (no menu, no group in the explorer).</param>
    /// <param name="projectCommands">The project commands the menu, the hotkeys and the context menus use; null without projects.</param>
    /// <param name="documents">The editable documents, undo and redo; null: no menu "Edit". The window shows its tab list (<see cref="DocumentsController.Tabs"/>).</param>
    /// <param name="projectFiles">The file commands of the projects; null: no menu entries for new files, no context menu entries for files. Their results go to the project status item.</param>
    /// <param name="projectFilesController">Passed on to the explorer, which follows the changes of the files with it.</param>
    public MainViewModel(AppTexts texts, AppPaths paths, Action<string> openFolder, IOperationRunner progress, Action openSettings,
        WorkspaceController workspace, Action? openGameSettings = null, ProjectsController? projects = null, ProjectsViewModel? projectCommands = null,
        DocumentsController? documents = null, ProjectFilesViewModel? projectFiles = null, ProjectFilesController? projectFilesController = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Texts = texts;
        _workspace = workspace;
        _projects = projects;
        _projectFiles = projectFiles;
        Projects = projectCommands;
        Documents = documents;
        OpenSettingsCommand = new RelayCommand(openSettings);
        OpenLogFolderCommand = new RelayCommand(() => openFolder(paths.LogDirectory));

        // async void on purpose, as for every command that starts an operation: the view model of the files handles its failures.
        _newTableCommand = new RelayCommand(async () =>
        {
            if (_projects?.ActiveId is { } id)
            {
                await _projectFiles!.NewTableAsync(id);
            }
        }, () => _projectFiles is not null && ProjectNewFileToolTip is null);
        _newLocCommand = new RelayCommand(async () =>
        {
            if (_projects?.ActiveId is { } id)
            {
                await _projectFiles!.NewLocAsync(id);
            }
        }, () => _projectFiles is not null && ProjectNewFileToolTip is null);

        // The controller works on the tab list, so the window must show that very list.
        Tabs = documents?.Tabs ?? new TabsViewModel(texts);
        Explorer = new ExplorerViewModel(workspace, texts, OpenEntryAsync, openGameSettings ?? openSettings, projects, projectCommands, OpenProjectFileAsync,
            projectFiles, projectFilesController);
        RowCommands = new RowCommandsViewModel(Tabs, texts);

        // async void on purpose, as for every command that starts an operation: the controller handles its failures, a surprise reaches the global handler.
        // After the packs, the projects are read again (their files may have changed outside), and the open ones are listed again silently.
        _refreshCommand = new RelayCommand(async () =>
        {
            await workspace.RefreshAsync();
            if (projects is not null)
            {
                projects.Reload();
                await Explorer.RefreshProjectFilesAsync();
            }

            // Also the open project files: a file that another program changed does not change the state of its project, so
            // ProjectsController.Changed is not raised for it. Silent, like the check when the window is activated.
            if (documents is not null)
            {
                await documents.CheckExternalChangesAsync();
            }
        }, () => workspace.CanStartOperation && workspace.Profile is not null);
        workspace.PackChanged += (_, pack) => Tabs.MarkStale(pack.Path);
        _seenSchema = workspace.Schema;
        workspace.Changed += (_, _) =>
        {
            // Another schema than the one seen at the last change: what the open tables show was decoded with the old one.
            var schema = workspace.Schema;
            if (_seenSchema is not null && SchemaDiffers(_seenSchema, schema))
            {
                Tabs.MarkSchemaStale();
            }

            _seenSchema = schema;
            _refreshCommand.RaiseCanExecuteChanged();
            RaiseStatusChanged();
        };

        // The status line is built from texts: a language switch changes all of it.
        texts.PropertyChanged += (_, _) =>
        {
            RaiseStatusChanged();
            RaiseProjectReasonsChanged();
        };

        if (projects is not null)
        {
            projects.Changed += (_, _) => CloseTabsOfRemovedProjects();
        }

        // The menu of a project says why it cannot be removed while a tab of it has unsaved changes: that reason changes with them, and with a
        // value that is typed into a cell and not committed yet.
        if (documents is not null)
        {
            documents.ModifiedChanged += (_, _) => Explorer.RefreshProjectActions();
            documents.PendingEditChanged += (_, _) => Explorer.RefreshProjectActions();
        }

        if (projects is not null && projectCommands is not null)
        {
            // The status bar shows the result of the last project command; the menu says why Build is disabled.
            projectCommands.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ProjectsViewModel.StatusMessage) or null)
                {
                    _fileStatusIsNewer = false;
                    RaiseProjectStatusChanged();
                }
            };
            projects.Changed += (_, _) => RaiseProjectReasonsChanged();
            projects.OperationStateChanged += (_, _) => RaiseProjectReasonsChanged();

            // The result of a file command is shown in the same place. The one that changed last wins, also when it changed to nothing: a command that
            // starts clears its message, and an older message of the other kind must not come back in its place.
            if (projectFiles is not null)
            {
                projectFiles.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(ProjectFilesViewModel.StatusMessage) or null)
                    {
                        _fileStatusIsNewer = true;
                        RaiseProjectStatusChanged();
                    }
                };
            }
        }

        // The debug commands exist in every build; only the menu that offers them is limited to the debug build (IsDebugBuild).
        DebugThrowCommand = new RelayCommand(() => throw new InvalidOperationException("Debug test exception"));
        DebugSlowCommand = new RelayCommand(async () => await progress.RunAsync(texts.MenuDebugSlow, RunSlowWork));
        DebugFailCommand = new RelayCommand(async () => await progress.RunAsync<int>(texts.MenuDebugFail, RunFailingWork));
    }

    public AppTexts Texts { get; }

    public ExplorerViewModel Explorer { get; }

    public TabsViewModel Tabs { get; }

    /// <summary>The row commands of the toolbar; they act on the selected tab and say why they are disabled.</summary>
    public RowCommandsViewModel RowCommands { get; }

    /// <summary>The project commands; null when the app has no projects.</summary>
    public ProjectsViewModel? Projects { get; }

    /// <summary>The menu "Project" is shown.</summary>
    public bool HasProjects => Projects is not null;

    /// <summary>The editable documents with undo and redo; null when the app has no projects.</summary>
    public DocumentsController? Documents { get; }

    /// <summary>The menu "Edit" is shown.</summary>
    public bool HasDocuments => Documents is not null;

    /// <summary>The first line of the result of the last project command, for the status bar; null when there is none.</summary>
    public string? ProjectStatusLine
    {
        get
        {
            var message = ProjectStatusMessage;
            if (string.IsNullOrEmpty(message))
            {
                return null;
            }

            var end = message.IndexOfAny(['\r', '\n']);
            return end < 0 ? message : message[..end];
        }
    }

    /// <summary>The whole result of the last project command: it can have several lines. Null when there is none.</summary>
    public string? ProjectStatusToolTip => string.IsNullOrEmpty(ProjectStatusMessage) ? null : ProjectStatusMessage;

    // The message of the project commands or of the file commands, whichever spoke last.
    private string? ProjectStatusMessage => _fileStatusIsNewer ? _projectFiles?.StatusMessage : Projects?.StatusMessage;

    public bool HasProjectStatus => ProjectStatusLine is not null;

    /// <summary>Why "Build" of the menu is disabled (no game profile, no active project, an unusable project, no schema, or another operation runs); null when it can run.</summary>
    public string? ProjectBuildToolTip => ProjectReason(Projects?.BuildDisabledReason(_projects?.ActiveId));

    /// <summary>Why "Build and install" of the menu is disabled (as for Build, and no game folder or a project list that cannot be saved); null when it can run.</summary>
    public string? ProjectBuildAndInstallToolTip => ProjectReason(Projects?.InstallDisabledReason(_projects?.ActiveId));

    /// <summary>Why "Import pack" of the menu is disabled (no game profile, or another operation runs); null when it can run.</summary>
    public string? ProjectImportToolTip => ProjectReason(null);

    /// <summary>
    /// The tool tip of "Save" and "Save all" of the menu: "busy" while another operation runs, else null. Having nothing to save needs no
    /// explanation, and the busy test is the one the commands use, so the tool tip and the state of the entry agree.
    /// </summary>
    public string? ProjectSaveToolTip => Documents is null || _projects is null || _projects.CanStartOperation ? null : Texts.ProjectBusy;

    /// <summary>The menu entries "New table" and "New loc file" are shown.</summary>
    public bool HasProjectFiles => _projectFiles is not null;

    /// <summary>Asks for a table and a name and creates the table in the active project. Disabled with <see cref="ProjectNewFileToolTip"/>.</summary>
    public ICommand NewTableCommand => _newTableCommand;

    /// <summary>Asks for a name and creates a loc file in the active project. Disabled with <see cref="ProjectNewFileToolTip"/>.</summary>
    public ICommand NewLocCommand => _newLocCommand;

    /// <summary>
    /// Why "New table" and "New loc file" of the menu are disabled (no game profile, no active project, an unusable project, or another operation
    /// runs); null when they can run.
    /// </summary>
    public string? ProjectNewFileToolTip => ProjectReason(_projects?.ActiveId is { } id ? _projectFiles?.NewFileDisabledReason(id) : Texts.ProjectNoneActive);

    /// <summary>Scans the packs and loads the schema again. Enabled when a game profile exists and no operation runs.</summary>
    public ICommand RefreshCommand => _refreshCommand;

    /// <summary>The name of the game, and "not found" or "loading" where that applies.</summary>
    public string StatusGame
    {
        get
        {
            var profile = _workspace.Profile;
            if (profile is null)
            {
                return Texts.StatusGameNone;
            }

            var text = _workspace.Status == GameStatus.NotFound ? Texts.StatusGameNotFound(profile.DisplayName) : Texts.StatusGame(profile.DisplayName);
            return _workspace.IsBusy ? text + " – " + Texts.StatusLoading : text;
        }
    }

    /// <summary>The install folder, null when the game was not found.</summary>
    public string? StatusGameToolTip => _workspace.Location?.InstallPath;

    public string StatusSchema => Texts.StatusSchema(Texts.SchemaSourceText(_workspace.Schema));

    /// <summary>The first thing the user should know about the data that is shown, null when there is nothing.</summary>
    public string? StatusHint
    {
        get
        {
            if (_workspace.Schema is not { } schema)
            {
                return null;
            }

            if (schema.Origin == SchemaOrigin.None)
            {
                return Texts.StatusHintNoSchema(Texts.SchemaProblemText(schema.ShippedAttempt?.Problem ?? SchemaProblem.None));
            }

            if (schema.OverrideAttempt is { Problem: not SchemaProblem.None } failed && _workspace.Profile is { } profile)
            {
                return Texts.StatusHintSchemaOverrideFailed(profile.SchemaFile, Texts.SchemaProblemText(failed.Problem));
            }

            return _workspace.Catalog?.FolderProblems.FirstOrDefault() is { } folder ? Texts.StatusHintFolderProblem(folder.Path) : null;
        }
    }

    public bool HasStatusHint => StatusHint is not null;

    /// <summary>Opens the entry in a tab. An entry that is already open only gets its tab selected; nothing is read then.</summary>
    public async Task OpenEntryAsync(PackSource pack, string entryPath)
    {
        if (Tabs.TryActivate(pack.Path, entryPath))
        {
            return;
        }

        if (await _workspace.OpenEntryAsync(pack, entryPath) is { } opened)
        {
            // A tab for the entry may have come meanwhile (a session restore): then that one is shown and what was read is dropped.
            if (!Tabs.TryActivate(pack.Path, entryPath))
            {
                Tabs.Add(pack, opened, _workspace.Schema);
            }
        }
    }

    /// <summary>
    /// Opens a file of a project in a tab. <paramref name="relativeFile"/> is the path below the pack folder (the explorer gives it with '\'); the
    /// tab keeps it with '/'. A file that is already open only gets its tab selected; nothing is read then.
    /// </summary>
    public async Task OpenProjectFileAsync(Guid projectId, string relativeFile)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);
        if (_projects is null || _projects.Projects.FirstOrDefault(state => state.Id == projectId) is not { } state)
        {
            return;
        }

        var source = TabSource.ForProject(projectId, MessageText.EscapeControlCharacters(state.DisplayName));
        if (Tabs.TryActivate(source.Key, relativeFile.Replace('\\', '/')))
        {
            return;
        }

        if (await _projects.OpenFileAsync(projectId, relativeFile) is { } opened)
        {
            // A tab for the file may have come meanwhile (a session restore): two editable tabs of one file would hold separate documents.
            if (!Tabs.TryActivate(source.Key, relativeFile.Replace('\\', '/')))
            {
                Tabs.Add(source, opened, _workspace.Schema);
            }
        }
    }

    // The reason order of every entry that needs a free runner: no game profile, then what is wrong with the project (no active project,
    // unusable, no game folder), then "another operation is running". Null when the entry can run; no entry is disabled without a reason.
    private string? ProjectReason(string? ofTheProject)
    {
        if (Projects is null || _projects is null)
        {
            return null;
        }

        return Projects.ProjectsDisabledReason ?? ofTheProject ?? (_projects.CanStartOperation ? null : Texts.ProjectBusy);
    }

    private void RaiseProjectReasonsChanged()
    {
        OnPropertyChanged(nameof(ProjectBuildToolTip));
        OnPropertyChanged(nameof(ProjectBuildAndInstallToolTip));
        OnPropertyChanged(nameof(ProjectImportToolTip));
        OnPropertyChanged(nameof(ProjectSaveToolTip));
        OnPropertyChanged(nameof(ProjectNewFileToolTip));
        _newTableCommand.RaiseCanExecuteChanged();
        _newLocCommand.RaiseCanExecuteChanged();
    }

    private void RaiseProjectStatusChanged()
    {
        OnPropertyChanged(nameof(ProjectStatusLine));
        OnPropertyChanged(nameof(ProjectStatusToolTip));
        OnPropertyChanged(nameof(HasProjectStatus));
    }

    // A project that left the list takes its tabs with it: they would show files of a project the app no longer knows.
    private void CloseTabsOfRemovedProjects()
    {
        var listed = _projects!.Projects.Select(state => state.Id).ToHashSet();
        foreach (var tab in Tabs.Tabs.Where(tab => TabSource.TryGetProjectId(tab.PackPath, out var id) && !listed.Contains(id)).ToList())
        {
            Tabs.CloseWithoutAsking(tab);
        }
    }

    // A reload that resolves the same file with the same date is no change: a refresh alone must not mark any tab.
    private static bool SchemaDiffers(ResolvedSchema before, ResolvedSchema? after) =>
        after is null
        || before.Origin != after.Origin
        || !string.Equals(before.Path, after.Path, StringComparison.OrdinalIgnoreCase)
        || before.FileDateUtc != after.FileDateUtc
        || (before.Schema is null) != (after.Schema is null);

    private void RaiseStatusChanged()
    {
        OnPropertyChanged(nameof(StatusGame));
        OnPropertyChanged(nameof(StatusGameToolTip));
        OnPropertyChanged(nameof(StatusSchema));
        OnPropertyChanged(nameof(StatusHint));
        OnPropertyChanged(nameof(HasStatusHint));
    }

    /// <summary>True in the debug build only. The Debug menu binds its visibility to it.</summary>
    public bool IsDebugBuild { get; } = ReadIsDebugBuild();

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenLogFolderCommand { get; }

    /// <summary>Throws on the UI thread, to try out the global exception handling and the crash dialog.</summary>
    public ICommand DebugThrowCommand { get; }

    /// <summary>A five-second operation with a progress count, to try out the progress window and Cancel.</summary>
    public ICommand DebugSlowCommand { get; }

    /// <summary>An operation that fails after one second, to try out the error report of the progress runner.</summary>
    public ICommand DebugFailCommand { get; }

    private static bool ReadIsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    // Waits on the token instead of sleeping, so Cancel ends the run at once. Returns the steps done, which is also the partial result after a cancel.
    private static int RunSlowWork(IProgress<OperationProgress> progress, CancellationToken cancellation)
    {
        var done = 0;
        while (done < SlowStepCount && !cancellation.WaitHandle.WaitOne(SlowStepDelay))
        {
            done++;
            progress.Report(new OperationProgress(done, SlowStepCount, $"step {done}"));
        }

        return done;
    }

    private static int RunFailingWork(IProgress<OperationProgress> progress, CancellationToken cancellation)
    {
        Thread.Sleep(FailDelay);
        throw new IOException("Debug failure");
    }
}
