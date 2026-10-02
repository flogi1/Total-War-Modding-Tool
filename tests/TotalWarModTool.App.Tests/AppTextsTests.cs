using TotalWarModTool.Localization;

namespace TotalWarModTool.App.Tests;

public class AppTextsTests
{
    private static string Locales => Path.Combine(RepoRoot.Find(), "locales");

    [Fact] public void Locale_files_are_complete() =>
        Assert.Empty(LocaleCheck.FindProblems([new AppTexts(new UiText(Locales))], Locales, ["en", "de"]));

    [Fact] public void German_texts_are_used_after_Load()
    {
        var ui = new UiText(Locales); var texts = new AppTexts(ui);
        ui.Load("de");
        Assert.Equal("Werkzeuge", texts.MenuTools);
    }
}
