using TotalWarModTool.Localization;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class StartupSanitizerTests
{
    private static readonly UiLanguageOption[] Available = [new("en", "English"), new("de", "Deutsch")];

    [Fact] public void FixLanguage_resets_unknown_language_to_en()
    {
        var s = new GlobalSettings { Language = "fr" };
        Assert.True(StartupSanitizer.FixLanguage(s, Available));
        Assert.Equal("en", s.Language);
    }

    [Fact] public void FixLanguage_keeps_known_language()
    {
        var s = new GlobalSettings { Language = "de" };
        Assert.False(StartupSanitizer.FixLanguage(s, Available));
        Assert.Equal("de", s.Language);
    }
}
