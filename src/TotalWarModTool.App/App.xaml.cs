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

        var log = new FileLog(paths.LogDirectory, TimeProvider.System, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        log.Info($"{AppTexts.AppName} {typeof(App).Assembly.GetName().Version?.ToString(3)} started");

        // The texts exist from here on. They return the English inline fallbacks until ui.Load runs below.
        var ui = new UiText(paths.LocalesDirectory);
        var texts = new AppTexts(ui);

        var store = new GlobalSettingsStore(paths.RoamingRoot);
        var loaded = store.Load();
        if (loaded.Status is LoadStatus.Corrupt or LoadStatus.Unreadable or LoadStatus.NewerVersion)
        {
            var badCopy = loaded.BadCopyPath is null ? string.Empty : $" The broken file was copied to {loaded.BadCopyPath}.";
            log.Warn($"Settings file {store.FilePath}: {loaded.Status}.{badCopy}");
        }

        var settings = loaded.Settings;
        StartupSanitizer.FixLanguage(settings, LanguageCatalog.Find(paths.LocalesDirectory));
        ui.Load(settings.Language);

        // Before the first window exists, so nothing is shown in the wrong colours.
        new ThemeManager(Resources, new RegistrySystemThemeSource()).Apply(settings.Theme);

        _autoSaver = new SettingsAutoSaver(settings, store, log);

        var window = new MainWindow(new MainViewModel(texts, paths, OpenFolder));
        _ = new WindowPlacementTracker(window, settings);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _autoSaver?.Flush();
        base.OnExit(e);
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }
}
