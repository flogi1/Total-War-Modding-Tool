using TotalWarModTool.Localization;
using TotalWarModTool.Logging;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

/// <summary>
/// The one place that loads a language and leaves a trace of what went wrong in the log. A broken language file is not an error for the
/// user (the texts fall back to English), so without these lines nobody could find out why a translation does not show.
/// </summary>
public static class LanguageLoader
{
    /// <summary>
    /// Loads <paramref name="language"/>. If its file is not valid (invalid JSON, not UTF-8), one warning with the language code, line,
    /// position and message goes to the log; the texts of the language then come from English.
    /// </summary>
    public static void Load(UiText ui, string? language, IAppLog log)
    {
        ui.Load(language);
        if (ui.LoadError is { } error)
        {
            // ui.Language, not the argument: Load trims and lower-cases the code, and the file that was read is named after that.
            log.Warn($"The language file of \"{ui.Language}\" is not valid, English texts are used instead (line {error.Line}, position {error.Position}): {error.Message}");
        }
    }

    /// <summary>
    /// Start-up: repairs a language of the settings that has no language file, then loads the language. The repair is logged with the old and
    /// the new code.
    /// </summary>
    public static void LoadAtStartup(GlobalSettings settings, IReadOnlyList<UiLanguageOption> available, UiText ui, IAppLog log)
    {
        var requested = settings.Language;
        if (StartupSanitizer.FixLanguage(settings, available))
        {
            log.Info($"The language \"{requested}\" of the settings is not available, it was changed to \"{settings.Language}\".");
        }

        Load(ui, settings.Language, log);
    }
}
