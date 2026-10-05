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
    private readonly RelayCommand _refreshCommand;
    private ResolvedSchema? _seenSchema;

    /// <param name="openFolder">Opens a folder in the file explorer. Passed in so the view model stays free of processes and tests need no explorer.</param>
    /// <param name="progress">The runner every user-started operation goes through. The workspace and the debug commands use it.</param>
    /// <param name="openSettings">Opens the settings window (or activates it if it is open). Passed in so the view model knows no windows.</param>
    /// <param name="workspace">The state of the game. The explorer, the tabs and the status line are built on it.</param>
    /// <param name="openGameSettings">Opens the settings window on the page "Game". The explorer uses it for its button; null: the explorer uses <paramref name="openSettings"/>.</param>
    /// <param name="projects">The mod projects of the game; null: the app has no projects (no menu, no group in the explorer).</param>
    /// <param name="projectCommands">The project commands the menu, the hotkeys and the context menus use; null without projects.</param>
    /// <param name="documents">The editable documents, undo and redo; null: no menu "Edit". The window shows its tab list (<see cref="DocumentsController.Tabs"/>).</param>
    public MainViewModel(AppTexts texts, AppPaths paths, Action<string> openFolder, IOperationRunner progress, Action openSettings,
        WorkspaceController workspace, Action? openGameSettings = null, ProjectsController? projects = null, ProjectsViewModel? projectCommands = null,
        DocumentsController? documents = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Texts = texts;
        _workspace = workspace;
        _projects = projects;
        Projects = projectCommands;
        Documents = documents;
        OpenSettingsCommand = new RelayCommand(openSettings);
        OpenLogFolderCommand = new RelayCommand(() => openFolder(paths.LogDirectory));

        // The controller works on the tab list, so the window must show that very list.
        Tabs = documents?.Tabs ?? new TabsViewModel(texts);
        Explorer = new ExplorerViewModel(workspace, texts, OpenEntryAsync, openGameSettings ?? openSettings, projects, projectCommands, OpenProjectFileAsync);

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

        if (projects is not null && projectCommands is not null)
        {
            // The status bar shows the result of the last project command; the menu says why Build is disabled.
            projectCommands.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ProjectsViewModel.StatusMessage) or null)
                {
                    OnPropertyChanged(nameof(ProjectStatusLine));
                    OnPropertyChanged(nameof(ProjectStatusToolTip));
                    OnPropertyChanged(nameof(HasProjectStatus));
                }
            };
            projects.Changed += (_, _) => RaiseProjectReasonsChanged();
            projects.OperationStateChanged += (_, _) => RaiseProjectReasonsChanged();
        }

        // The debug commands exist in every build; only the menu that offers them is limited to the debug build (IsDebugBuild).
        DebugThrowCommand = new RelayCommand(() => throw new InvalidOperationException("Debug test exception"));
        DebugSlowCommand = new RelayCommand(async () => await progress.RunAsync(texts.MenuDebugSlow, RunSlowWork));
        DebugFailCommand = new RelayCommand(async () => await progress.RunAsync<int>(texts.MenuDebugFail, RunFailingWork));
    }

    public AppTexts Texts { get; }

    public ExplorerViewModel Explorer { get; }

    public TabsViewModel Tabs { get; }

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
            var message = Projects?.StatusMessage;
            if (string.IsNullOrEmpty(message))
            {
                return null;
            }

            var end = message.IndexOfAny(['\r', '\n']);
            return end < 0 ? message : message[..end];
        }
    }

    /// <summary>The whole result of the last project command: it can have several lines. Null when there is none.</summary>
    public string? ProjectStatusToolTip => string.IsNullOrEmpty(Projects?.StatusMessage) ? null : Projects.StatusMessage;

    public bool HasProjectStatus => ProjectStatusLine is not null;

    /// <summary>Why "Build" of the menu is disabled (no game profile, no active project, an unusable project, no schema, or another operation runs); null when it can run.</summary>
    public string? ProjectBuildToolTip => ProjectReason(Projects?.BuildDisabledReason(_projects?.ActiveId));

    /// <summary>Why "Build and install" of the menu is disabled (as for Build, and no game folder or a project list that cannot be saved); null when it can run.</summary>
    public string? ProjectBuildAndInstallToolTip => ProjectReason(Projects?.InstallDisabledReason(_projects?.ActiveId));

    /// <summary>Why "Import pack" of the menu is disabled (no game profile, or another operation runs); null when it can run.</summary>
    public string? ProjectImportToolTip => ProjectReason(null);

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
            Tabs.Add(pack, opened, _workspace.Schema);
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
            Tabs.Add(source, opened, _workspace.Schema);
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
