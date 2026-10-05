using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TotalWarModTool.GameProfiles;
using TotalWarModTool.Localization;
using TotalWarModTool.Logging;
using TotalWarModTool.Projects;
using TotalWarModTool.Session;
using TotalWarModTool.Settings;
using TotalWarModTool.Storage;
using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.App;

/// <summary>The composition root: the only place that creates the services and passes them on.</summary>
public partial class App : Application
{
    // How long the window must stay activated before the open project files are checked for outside changes.
    private static readonly TimeSpan ActivationCheckDelay = TimeSpan.FromMilliseconds(300);

    private SettingsAutoSaver? _autoSaver;
    private DebouncedSaver? _gameSettingsSaver;
    private DebouncedSaver? _sessionSaver;
    private IAppLog? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        // During the start-up only an explicit Shutdown ends the app. With the default mode (OnLastWindowClose), closing the crash dialog of a
        // failed start-up would be the last window closing, WPF would start its own shutdown with exit code 0, and the Shutdown(1) of the
        // guard would be ignored. Compose sets the default back once the main window is shown.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        base.OnStartup(e);

        var paths = AppPaths.Default();

        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        var log = new FileLog(paths.LogDirectory, TimeProvider.System, userProfilePath);
        _log = log;
        log.Info($"{AppTexts.AppName} {appVersion} started");

        // The texts exist from here on. They return the English inline fallbacks until the language is loaded in Compose.
        var ui = new UiText(paths.LocalesDirectory);
        var texts = new AppTexts(ui);

        // As early as it can work: the dialog needs the texts. A crash from here on is logged and shown, also while the rest of the start-up runs.
        var crashHandler = new CrashHandler(log, TimeProvider.System, appVersion, userProfilePath, (details, fatal) => ShowCrashDialog(texts, paths, details, fatal));
        crashHandler.Install(this);

        // A step that throws before the main window exists would leave a process without a window: the guard shows the failure as fatal and shuts down.
        // A failure before this point (paths, log, texts, crash handler) has nothing to show it with and propagates.
        StartupGuard.Run(() => Compose(paths, log, ui, texts, crashHandler), crashHandler, Shutdown);
    }

    /// <summary>Everything after the log and the crash handler exist, up to the visible main window. Restores the normal shutdown mode at the end.</summary>
    private void Compose(AppPaths paths, IAppLog log, UiText ui, AppTexts texts, CrashHandler crashHandler)
    {
        var store = new GlobalSettingsStore(paths.RoamingRoot);
        var loaded = store.Load();
        if (SettingsLoadMessage.Build(store.FilePath, loaded.Status, loaded.BadCopyPath) is { } settingsProblem)
        {
            log.Warn(settingsProblem);
        }

        // Created before the sanitizer, so that its language repair arrives as a normal change and is saved.
        var settings = loaded.Settings;
        _autoSaver = new SettingsAutoSaver(settings, store, log, loaded.Status);

        // Read once: the sanitizer and the settings window offer the same languages. A language file added later shows up after a restart.
        var languages = LanguageCatalog.Find(paths.LocalesDirectory);
        LanguageLoader.LoadAtStartup(settings, languages, ui, log);

        // Before the first window exists, so nothing is shown in the wrong colours. Kept: the settings applier switches the theme through it.
        var theme = new ThemeManager(Resources, new RegistrySystemThemeSource());
        theme.Apply(settings.Theme);

        // After the start values are applied: from here on a change of language or theme in the settings takes effect at once.
        _ = new SettingsApplier(settings, ui, theme, log);

        // Every user-started operation runs through this one runner.
        var progressRunner = new ProgressRunner(
            () => Windows.Cast<Window>().ToList(),
            texts,
            log,
            TimeProvider.System,
            (operation, error) => MessageBox.Show(
                texts.OperationFailedMessage(operation, error.Message),
                texts.OperationFailedTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error));

        // The game: the profile the settings name, else the first one. Everything stored per game is named after the id of that profile.
        var profiles = GameProfileLoader.LoadAll(paths.ProfilesDirectory);
        foreach (var problem in profiles.Problems)
        {
            log.Warn($"Game profile {problem.FileName} was skipped: {problem.Detail}");
        }

        var profile = ChooseProfile(profiles.Profiles, settings.SelectedGame);
        if (profile is not null && profile.Id != settings.SelectedGame)
        {
            settings.SelectedGame = profile.Id;
        }

        var gameSettings = new GameSettings();
        SessionStore? sessionStore = null;
        var sessionLoaded = new SessionLoadResult(SessionState.Empty, LoadStatus.Missing, null);
        ProjectListStore? projectListStore = null;
        var projectListLoaded = new ProjectListLoadResult(ProjectList.Empty, LoadStatus.Missing, null);
        if (profile is not null)
        {
            var gameSettingsStore = new GameSettingsStore(paths.RoamingRoot, profile.Id);
            var gameSettingsLoaded = gameSettingsStore.Load();
            gameSettings = gameSettingsLoaded.Settings;
            if (SettingsLoadMessage.Build(gameSettingsStore.FilePath, gameSettingsLoaded.Status, gameSettingsLoaded.BadCopyPath, "Game settings file") is { } gameProblem)
            {
                log.Warn(gameProblem);
            }

            if (gameSettingsLoaded.Status is LoadStatus.NewerVersion or LoadStatus.Unreadable)
            {
                // Never subscribed, so nothing is ever written: a newer file would lose fields, an unreadable one is only defaults here.
                log.Warn($"Game settings will not be saved in this session: {gameSettingsStore.FilePath} was not loaded as it is.");
            }
            else
            {
                var saver = new DebouncedSaver(() => gameSettingsStore.Save(gameSettingsLoaded.Settings), log, "The game settings");
                gameSettingsLoaded.Settings.Changed += (_, _) => saver.MarkDirty();
                _gameSettingsSaver = saver;
            }

            sessionStore = new SessionStore(paths.RoamingRoot, profile.Id);
            sessionLoaded = sessionStore.Load();
            if (SettingsLoadMessage.Build(sessionStore.FilePath, sessionLoaded.Status, sessionLoaded.BadCopyPath, "Session file") is { } sessionProblem)
            {
                log.Warn(sessionProblem);
            }

            // The controller decides what a list that was not loaded as it is means for saving (see ProjectsController).
            projectListStore = new ProjectListStore(paths.RoamingRoot, profile.Id);
            projectListLoaded = projectListStore.Load();
            if (SettingsLoadMessage.Build(projectListStore.FilePath, projectListLoaded.Status, projectListLoaded.BadCopyPath, "Project list") is { } projectListProblem)
            {
                log.Warn(projectListProblem);
            }
        }

        var workspace = new WorkspaceController(profile, gameSettings, settings, paths, new RegistrySteamLocationSource(), progressRunner, log, texts);

        // One view model for the whole run: apart from what is typed into its boxes it keeps no state, so every settings window can use it.
        var settingsViewModel = new SettingsViewModel(settings, texts, languages, gameSettings, workspace, PickFolder);

        // The main view model needs the way to open the settings window, and the settings window needs the main window as its owner.
        // The owner is only read when the window is opened, which is after mainWindow is set below.
        Window? mainWindow = null;
        var settingsWindows = new SettingsWindowService(() =>
        {
            // A window that is opened again shows the stored values, not what was typed into the one before.
            settingsViewModel.ResetInputs();
            return new SettingsWindow(settingsViewModel) { Owner = mainWindow };
        });

        // The mod projects of the game. Without a game profile the store is null and the controller is unavailable: the menu shows, but says why it is disabled.
        // The dialogs are owned by the main window, which is read only when a dialog opens.
        var projects = new ProjectsController(projectListStore, projectListLoaded, workspace, progressRunner, log, texts, TimeProvider.System);
        var projectCommands = new ProjectsViewModel(projects, new ProjectDialogs(texts, () => mainWindow), texts);

        // The editable tabs of the project files and the global undo history. The controller owns the list of tabs the main view model shows.
        var documents = new DocumentsController(new TabsViewModel(texts) { Clipboard = new WpfClipboard() }, projects, new ActionHistory(), progressRunner,
            new EditDialogs(texts, () => mainWindow), log, texts);

        // A project with unsaved changes cannot leave the list, and a build asks to save them first.
        projectCommands.UnsavedReason = documents.UnsavedReason;
        projectCommands.BeforeBuild = documents.ConfirmSaveBeforeBuildAsync;

        // The button of the explorer ("game not found") leads to the page "Game"; the menu entry keeps the page that was shown last.
        var viewModel = new MainViewModel(texts, paths, OpenFolder, progressRunner, settingsWindows.ShowOrActivate, workspace, () =>
        {
            settingsViewModel.SelectedCategory = SettingsViewModel.GameCategory;
            settingsWindows.ShowOrActivate();
        }, projects, projectCommands, documents);

        SessionCoordinator? session = null;
        if (sessionStore is not null)
        {
            session = new SessionCoordinator(sessionStore, sessionLoaded, workspace, viewModel.Explorer, viewModel.Tabs, log, projects);
            var saver = new DebouncedSaver(session.Save, log, "The session");
            session.Dirty += (_, _) => saver.MarkDirty();
            _sessionSaver = saver;
        }

        var window = new MainWindow(viewModel);
        mainWindow = window;

        // Unsaved changes: a close that the controller does not allow now is cancelled; the controller asks and, when the answer is "go on", has the
        // window closed again. That close is posted, because a window cannot be closed from inside its own Closing event.
        window.Closing += (_, closing) =>
        {
            if (!documents.RequestAppClose(() => window.Dispatcher.BeginInvoke(window.Close)))
            {
                closing.Cancel = true;
            }
        };
        // Another program may have changed the project files that are open: looked at when the window is activated again. The timer waits a
        // moment and restarts on every activation, so a burst of them (a dialog closing, a click into the window) checks once. The decision
        // what to check stays in the controller, which also skips the round while an operation runs. An async handler on purpose: the
        // check never throws, a surprise reaches the global handler.
        var activationTimer = new DispatcherTimer { Interval = ActivationCheckDelay };
        activationTimer.Tick += async (_, _) =>
        {
            activationTimer.Stop();
            await documents.CheckExternalChangesAsync();
        };
        window.Activated += (_, _) =>
        {
            activationTimer.Stop();
            activationTimer.Start();
        };

        // No check after the window is gone.
        window.Closed += (_, _) => activationTimer.Stop();

        _ = new WindowPlacementTracker(window, settings);
        window.Show();

        // The start-up worked: from here on the app ends when its last window is closed, as usual.
        ShutdownMode = ShutdownMode.OnLastWindowClose;

        // Not awaited: the window is there and shows "loading" while the packs are scanned. A surprise from the chain goes to the crash handler.
        _ = LoadGameAsync(workspace, session, crashHandler);
    }

    // The profile of the stored id (ignoring case), else the first one; null without profiles.
    private static GameProfile? ChooseProfile(IReadOnlyList<GameProfile> profiles, string? selectedId) =>
        profiles.FirstOrDefault(profile => string.Equals(profile.Id, selectedId, StringComparison.OrdinalIgnoreCase)) ?? profiles.FirstOrDefault();

    /// <summary>
    /// The folder dialog of the settings: starts at <paramref name="startFolder"/> when that exists, is owned by the open settings window
    /// (the main window when none is open) and returns the chosen folder, or null when the user cancelled.
    /// </summary>
    private string? PickFolder(string? startFolder)
    {
        var dialog = new OpenFolderDialog { Multiselect = false };
        if (!string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder))
        {
            dialog.InitialDirectory = startFolder;
        }

        var owner = Windows.OfType<SettingsWindow>().FirstOrDefault() ?? MainWindow;
        var chosen = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return chosen == true ? dialog.FolderName : null;
    }

    private static async Task LoadGameAsync(WorkspaceController workspace, SessionCoordinator? session, CrashHandler crashHandler)
    {
        try
        {
            await workspace.LoadAsync();
            if (session is not null)
            {
                await session.RestoreAsync();
            }
        }
        catch (Exception ex)
        {
            // Not fatal: the app runs on without what could not be loaded.
            crashHandler.Handle(ex, CrashHandler.SourceTask);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // A failed save stays pending without a timer, so every saver is flushed once more here. One that fails must not skip the others.
        FlushSafely("settings", _autoSaver is { } autoSaver ? autoSaver.Flush : null);
        FlushSafely("game settings", _gameSettingsSaver is { } gameSettingsSaver ? gameSettingsSaver.Flush : null);
        FlushSafely("session", _sessionSaver is { } sessionSaver ? sessionSaver.Flush : null);
        base.OnExit(e);
    }

    private void FlushSafely(string what, Action? flush)
    {
        try
        {
            flush?.Invoke();
        }
        catch (Exception ex)
        {
            _log?.Error($"Saving the {what} at exit failed.", ex);
        }
    }

    /// <summary>
    /// The <c>showDialog</c> of the crash handler. Called from any thread: switches to the UI thread and shows the dialog modal, owned by the
    /// active window or the main window (never by a progress window, see <see cref="CrashDialogOwner"/>). Returns when the dialog is closed.
    /// </summary>
    private void ShowCrashDialog(AppTexts texts, AppPaths paths, string details, bool fatal) =>
        Dispatcher.Invoke(() =>
        {
            var owner = CrashDialogOwner.Choose(Windows.Cast<Window>().FirstOrDefault(window => window.IsActive), MainWindow);
            new CrashDialog(texts, details, () => OpenFolder(paths.LogDirectory), fatal)
            {
                Owner = owner,
                WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            }.ShowDialog();
        });

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }
}
