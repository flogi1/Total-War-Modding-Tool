using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class ThemeTests
{
    [Fact] public void Palette_has_15_tokens_with_valid_hex_values()
    {
        Assert.Equal(15, ThemePalette.All.Count);
        Assert.All(ThemePalette.All, t => { Assert.Matches("^#[0-9A-F]{6}$", t.Light); Assert.Matches("^#[0-9A-F]{6}$", t.Dark); });
        Assert.Equal(ThemePalette.All.Count, ThemePalette.All.Select(t => t.Key).Distinct().Count());
    }

    [Fact] public void Controls_xaml_references_only_known_tokens()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml"));
        var used = Regex.Matches(xaml, @"Theme\.([A-Za-z]+)").Select(m => m.Groups[1].Value).Distinct();
        Assert.Empty(used.Except(ThemePalette.All.Select(t => t.Key)));
    }

    private sealed class FakeSystemTheme(bool dark) : ISystemThemeSource { public bool IsDark() => dark; }

    [Fact] public void Apply_writes_brushes_and_system_colors() => Sta.Run(() =>
    {
        var res = new ResourceDictionary();
        var manager = new ThemeManager(res, new FakeSystemTheme(false));
        manager.Apply(ThemeChoice.Dark);
        Assert.True(manager.IsDark);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#1E1F22"), ((SolidColorBrush)res["Theme.WindowBackground"]).Color);
        Assert.True(((SolidColorBrush)res["Theme.Text"]).IsFrozen);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#2B2D30"), ((SolidColorBrush)res[SystemColors.WindowBrushKey]).Color);
        manager.Apply(ThemeChoice.Light);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#F0F0F0"), ((SolidColorBrush)res["Theme.WindowBackground"]).Color);
    });

    [Theory]
    [InlineData(true, "#1E1F22")]
    [InlineData(false, "#F0F0F0")]
    public void Apply_System_follows_system_source(bool systemDark, string expected) => Sta.Run(() =>
    {
        var res = new ResourceDictionary();
        new ThemeManager(res, new FakeSystemTheme(systemDark)).Apply(ThemeChoice.System);
        Assert.Equal((Color)ColorConverter.ConvertFromString(expected), ((SolidColorBrush)res["Theme.WindowBackground"]).Color);
    });
}
