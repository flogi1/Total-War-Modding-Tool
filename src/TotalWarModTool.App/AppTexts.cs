using TotalWarModTool.Localization;

namespace TotalWarModTool.App;

/// <summary>The UI texts of the app shell: one property per text, each with its English inline fallback.</summary>
public sealed class AppTexts(UiText ui) : TextsBase(ui)
{
    /// <summary>Product name. Not translated.</summary>
    public const string AppName = "Total War Modding Tool";

    public string AppTitle => T("app.title", AppName);

    public string MenuTools => T("menu.tools", "Tools");

    public string MenuSettings => T("menu.tools.settings", "Settings");

    public string MenuOpenLogFolder => T("menu.tools.openLogFolder", "Open log folder");

    public string ProgressCancel => T("progress.cancel", "Cancel");

    public string ProgressCancelling => T("progress.cancelling", "Cancelling");

    /// <summary>Count line of the progress window, e.g. "120 / 4000".</summary>
    public string ProgressCount(long done, long total) => F("progress.count", "{0} / {1}", done, total);

    /// <summary>Percent line of the progress window, e.g. "3 %".</summary>
    public string ProgressPercent(int percent) => F("progress.percent", "{0} %", percent);

    public string OperationFailedTitle => T("error.operationFailed.title", "Operation failed");

    /// <param name="operation">Title of the operation that failed.</param>
    /// <param name="reason">Message of the exception.</param>
    public string OperationFailedMessage(string operation, string reason) =>
        F("error.operationFailed.message", "\"{0}\" could not be completed: {1} The log contains details (Tools, Open log folder).", operation, reason);

    public string CrashTitle => T("crash.title", "Unexpected error");

    public string CrashMessage => T("crash.message",
        "Total War Modding Tool ran into an unexpected error. Your settings are safe. You can keep working, but restarting the app is safer.");

    /// <summary>Shown instead of <see cref="CrashMessage"/> when the app cannot go on: a failure during start-up or a terminating exception.</summary>
    public string CrashFatalMessage => T("crash.fatalMessage",
        "Total War Modding Tool ran into an unexpected error and has to close. The log contains details.");

    public string CrashOpenLog => T("crash.openLog", "Open log");

    public string CrashCopyDetails => T("crash.copyDetails", "Copy details");

    public string CrashClose => T("crash.close", "Close");

    public string MenuDebug => T("menu.debug", "Debug");

    public string MenuDebugThrow => T("menu.debug.throw", "Throw test exception");

    public string MenuDebugSlow => T("menu.debug.slow", "Run slow operation");

    public string MenuDebugFail => T("menu.debug.fail", "Run failing operation");

    public string SettingsTitle => T("settings.title", "Settings");

    public string SettingsCategoryGeneral => T("settings.category.general", "General");

    public string SettingsLanguage => T("settings.language", "Language");

    public string SettingsTheme => T("settings.theme", "Theme");

    public string SettingsThemeSystem => T("settings.theme.system", "Same as Windows");

    public string SettingsThemeLight => T("settings.theme.light", "Light");

    public string SettingsThemeDark => T("settings.theme.dark", "Dark");
}
