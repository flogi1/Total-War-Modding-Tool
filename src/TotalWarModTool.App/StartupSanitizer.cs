using TotalWarModTool.Localization;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

/// <summary>Repairs loaded settings that cannot be used, silently, before anything depends on them.</summary>
public static class StartupSanitizer
{
    /// <summary>
    /// Sets <see cref="GlobalSettings.Language"/> to English when no available option has that code. The language comes from a
    /// hand-editable file and later becomes a file name, so only codes of <paramref name="available"/> may pass.
    /// </summary>
    /// <returns>True when the language was changed.</returns>
    public static bool FixLanguage(GlobalSettings settings, IReadOnlyList<UiLanguageOption> available)
    {
        if (available.Any(option => option.Code == settings.Language))
        {
            return false;
        }

        settings.Language = UiText.ReferenceLanguage;
        return true;
    }
}
