using System.Diagnostics;
using System.IO;
using System.Windows;
using TotalWarModTool.Localization;
using TotalWarModTool.Logging;
using TotalWarModTool.Settings;
using TotalWarModTool.Storage;

namespace TotalWarModTool.App;

/// <summary>The composition root: the only place that creates the services and passes them on.</summary>
public partial class App : Application
{
    private SettingsAutoSaver? _autoSaver;

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
        log.Info($"{AppTexts.AppName} {appVersion} started");

        // The texts exist from here on. They return the English inline fallbacks until the language is loaded in Compose.
        var ui = new UiText(paths.LocalesDirectory);
        var texts = new AppTexts(ui);

        // As early as it can work: the dialog needs the texts. A crash from here on is logged and shown, also while the rest of the start-up runs.
        var crashHandler = new CrashHandler(log, TimeProvider.System, appVersion, userProfilePath, (details, fatal) => ShowCrashDialog(texts, paths, details, fatal));
        crashHandler.Install(this);

        // A step that throws before the main window exists would leave a process without a window: the guard shows the failure as fatal and shuts down.
        // A failure before this point (paths, log, texts, crash handler) has nothing to show it with and propagates.
        StartupGuard.Run(() => Compose(paths, log, ui, texts), crashHandler, Shutdown);
    }

    /// <summary>Everything after the log and the crash handler exist, up to the visible main window. Restores the normal shutdown mode at the end.</summary>
    private void Compose(AppPaths paths, IAppLog log, UiText ui, AppTexts texts)
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

        // One view model for the whole run: it keeps no state of its own, so every settings window can use it.
        var settingsViewModel = new SettingsViewModel(settings, texts, languages);

        // The main view model needs the way to open the settings window, and the settings window needs the main window as its owner.
        // The owner is only read when the window is opened, which is after mainWindow is set below.
        Window? mainWindow = null;
        var settingsWindows = new SettingsWindowService(() => new SettingsWindow(settingsViewModel) { Owner = mainWindow });

        var window = new MainWindow(new MainViewModel(texts, paths, OpenFolder, progressRunner, settingsWindows.ShowOrActivate));
        mainWindow = window;
        _ = new WindowPlacementTracker(window, settings);
        window.Show();

        // The start-up worked: from here on the app ends when its last window is closed, as usual.
        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _autoSaver?.Flush();
        base.OnExit(e);
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
