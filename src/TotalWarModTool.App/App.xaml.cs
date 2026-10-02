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
        base.OnStartup(e);

        var paths = AppPaths.Default();

        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        var log = new FileLog(paths.LogDirectory, TimeProvider.System, userProfilePath);
        log.Info($"{AppTexts.AppName} {appVersion} started");

        // The texts exist from here on. They return the English inline fallbacks until ui.Load runs below.
        var ui = new UiText(paths.LocalesDirectory);
        var texts = new AppTexts(ui);

        // As early as it can work: the dialog needs the texts. A crash from here on is logged and shown, also while the rest of the start-up runs.
        new CrashHandler(log, TimeProvider.System, appVersion, userProfilePath, details => ShowCrashDialog(texts, paths, details)).Install(this);

        var store = new GlobalSettingsStore(paths.RoamingRoot);
        var loaded = store.Load();
        if (loaded.Status is LoadStatus.Corrupt or LoadStatus.Unreadable or LoadStatus.NewerVersion)
        {
            var badCopy = loaded.BadCopyPath is null ? string.Empty : $" The broken file was copied to {loaded.BadCopyPath}.";
            log.Warn($"Settings file {store.FilePath}: {loaded.Status}.{badCopy}");
        }

        // Created before the sanitizer, so that its language repair arrives as a normal change and is saved.
        var settings = loaded.Settings;
        _autoSaver = new SettingsAutoSaver(settings, store, log, loaded.Status);

        // Read once: the sanitizer and the settings window offer the same languages. A language file added later shows up after a restart.
        var languages = LanguageCatalog.Find(paths.LocalesDirectory);
        StartupSanitizer.FixLanguage(settings, languages);
        ui.Load(settings.Language);

        // Before the first window exists, so nothing is shown in the wrong colours. Kept: the settings applier switches the theme through it.
        var theme = new ThemeManager(Resources, new RegistrySystemThemeSource());
        theme.Apply(settings.Theme);

        // After the start values are applied: from here on a change of language or theme in the settings takes effect at once.
        _ = new SettingsApplier(settings, ui, theme);

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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _autoSaver?.Flush();
        base.OnExit(e);
    }

    /// <summary>
    /// The <c>showDialog</c> of the crash handler. Called from any thread: switches to the UI thread and shows the dialog modal, owned by the
    /// active window if there is one. Returns when the dialog is closed.
    /// </summary>
    private void ShowCrashDialog(AppTexts texts, AppPaths paths, string details) =>
        Dispatcher.Invoke(() =>
        {
            var owner = Windows.Cast<Window>().FirstOrDefault(window => window.IsActive);
            new CrashDialog(texts, details, () => OpenFolder(paths.LogDirectory))
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
