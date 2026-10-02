using System.Windows;
using System.Windows.Media;
using TotalWarModTool.Localization;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class SettingsApplierTests
{
    private sealed class FakeSystemTheme : ISystemThemeSource { public bool IsDark() => false; }

    [Fact] public void Language_and_theme_changes_apply_at_once() => Sta.Run(() =>
    {
        var settings = new GlobalSettings { Theme = ThemeChoice.Light };
        var ui = new UiText(Path.Combine(RepoRoot.Find(), "locales"));
        var res = new ResourceDictionary(); var theme = new ThemeManager(res, new FakeSystemTheme()); theme.Apply(settings.Theme);
        _ = new SettingsApplier(settings, ui, theme, new FakeAppLog());
        settings.Language = "de"; settings.Theme = ThemeChoice.Dark;
        Assert.Equal("de", ui.Language);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#1E1F22"), ((SolidColorBrush)res["Theme.WindowBackground"]).Color);
    });

    private static (GlobalSettings Settings, UiText Ui, FakeAppLog Log) CreateWithLocales(TempDir dir)
    {
        var settings = new GlobalSettings { Theme = ThemeChoice.Light };
        var ui = new UiText(dir.Path);
        var log = new FakeAppLog();
        var theme = new ThemeManager(new ResourceDictionary(), new FakeSystemTheme()); theme.Apply(settings.Theme);
        _ = new SettingsApplier(settings, ui, theme, log);
        return (settings, ui, log);
    }

    // A language file that is no valid JSON must leave a trace: the settings window then silently shows English.
    [Fact] public void Switching_to_a_language_with_an_invalid_file_logs_one_warning_with_code_and_line() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("en.json"), "{\"app.title\":\"Title\"}");
        File.WriteAllText(dir.File("fr.json"), "{\n  \"app.title\": \"Titre\",\n  \"menu.tools\" \"Outils\"\n}");     // the colon in line 3 is missing
        var (settings, ui, log) = CreateWithLocales(dir);

        settings.Language = "fr";

        var warning = Assert.Single(log.Warnings);
        Assert.Contains("\"fr\"", warning); Assert.Contains("line 3", warning);
        Assert.Empty(log.Infos); Assert.Empty(log.Errors);
        Assert.Equal("fr", ui.Language);
    });

    [Fact] public void Switching_to_a_language_with_a_valid_file_logs_nothing() => Sta.Run(() =>
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("en.json"), "{\"app.title\":\"Title\"}");
        File.WriteAllText(dir.File("de.json"), "{\"app.title\":\"Titel\"}");
        var (settings, ui, log) = CreateWithLocales(dir);

        settings.Language = "de";

        Assert.Equal("de", ui.Language);
        Assert.Empty(log.Infos); Assert.Empty(log.Warnings); Assert.Empty(log.Errors);
    });

    // Only the language reloads the texts and only the theme repaints; other settings (the window position) touch neither.
    [Fact] public void Other_settings_change_neither_language_nor_theme() => Sta.Run(() =>
    {
        var settings = new GlobalSettings { Theme = ThemeChoice.Light };
        var ui = new UiText(Path.Combine(RepoRoot.Find(), "locales"));
        var reloads = 0; ui.Changed += (_, _) => reloads++;
        var res = new ResourceDictionary(); var theme = new ThemeManager(res, new FakeSystemTheme()); theme.Apply(settings.Theme);
        _ = new SettingsApplier(settings, ui, theme, new FakeAppLog());

        settings.MainWindow = new WindowPlacement(1, 2, 300, 400, false);

        Assert.Equal(0, reloads);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#F0F0F0"), ((SolidColorBrush)res["Theme.WindowBackground"]).Color);
    });
}
