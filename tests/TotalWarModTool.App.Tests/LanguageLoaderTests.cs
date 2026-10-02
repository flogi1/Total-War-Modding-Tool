using TotalWarModTool.Localization;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class LanguageLoaderTests
{
    private static readonly UiLanguageOption[] Available = [new("en", "English"), new("de", "Deutsch")];

    private const string ValidFile = "{\"app.title\":\"Titel\"}";

    // The error is in line 3: the colon after "menu.tools" is missing.
    private const string BrokenFile = "{\n  \"app.title\": \"Titel\",\n  \"menu.tools\" \"Werkzeuge\"\n}";

    [Fact] public void Startup_with_a_broken_language_file_logs_one_warning_with_code_line_and_position()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("en.json"), ValidFile);
        File.WriteAllText(dir.File("de.json"), BrokenFile);
        var settings = new GlobalSettings { Language = "de" }; var ui = new UiText(dir.Path); var log = new FakeAppLog();

        LanguageLoader.LoadAtStartup(settings, Available, ui, log);

        var warning = Assert.Single(log.Warnings);
        Assert.Contains("\"de\"", warning); Assert.Contains("line 3", warning); Assert.Contains("position", warning);
        Assert.Empty(log.Infos); Assert.Empty(log.Errors);
        Assert.Equal(("de", "Titel"), (ui.Language, ui.Get("app.title", "fallback")));      // the English file still serves the texts
    }

    [Fact] public void Startup_with_an_unknown_language_logs_old_and_new_code_and_loads_English()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("en.json"), ValidFile);
        var settings = new GlobalSettings { Language = "fr" }; var ui = new UiText(dir.Path); var log = new FakeAppLog();

        LanguageLoader.LoadAtStartup(settings, Available, ui, log);

        var info = Assert.Single(log.Infos);
        Assert.Contains("\"fr\"", info); Assert.Contains("\"en\"", info);
        Assert.Equal(("en", "en"), (settings.Language, ui.Language));
        Assert.Empty(log.Warnings); Assert.Empty(log.Errors);
    }

    [Fact] public void Startup_with_a_known_valid_language_logs_nothing()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("en.json"), ValidFile);
        File.WriteAllText(dir.File("de.json"), ValidFile);
        var settings = new GlobalSettings { Language = "de" }; var ui = new UiText(dir.Path); var log = new FakeAppLog();

        LanguageLoader.LoadAtStartup(settings, Available, ui, log);

        Assert.Equal("de", ui.Language);
        Assert.Empty(log.Infos); Assert.Empty(log.Warnings); Assert.Empty(log.Errors);
    }

    // The code in the log is the one that was loaded: UiText trims and lower-cases what it is given.
    [Fact] public void Load_names_the_normalized_language_code()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("de.json"), BrokenFile);
        var ui = new UiText(dir.Path); var log = new FakeAppLog();

        LanguageLoader.Load(ui, " DE ", log);

        Assert.Contains("\"de\"", Assert.Single(log.Warnings));
    }
}
