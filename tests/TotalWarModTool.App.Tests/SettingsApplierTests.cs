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
        _ = new SettingsApplier(settings, ui, theme);
        settings.Language = "de"; settings.Theme = ThemeChoice.Dark;
        Assert.Equal("de", ui.Language);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#1E1F22"), ((SolidColorBrush)res["Theme.WindowBackground"]).Color);
    });

    // Only the language reloads the texts and only the theme repaints; other settings (the window position) touch neither.
    [Fact] public void Other_settings_change_neither_language_nor_theme() => Sta.Run(() =>
    {
        var settings = new GlobalSettings { Theme = ThemeChoice.Light };
        var ui = new UiText(Path.Combine(RepoRoot.Find(), "locales"));
        var reloads = 0; ui.Changed += (_, _) => reloads++;
        var res = new ResourceDictionary(); var theme = new ThemeManager(res, new FakeSystemTheme()); theme.Apply(settings.Theme);
        _ = new SettingsApplier(settings, ui, theme);

        settings.MainWindow = new WindowPlacement(1, 2, 300, 400, false);

        Assert.Equal(0, reloads);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#F0F0F0"), ((SolidColorBrush)res["Theme.WindowBackground"]).Color);
    });
}
