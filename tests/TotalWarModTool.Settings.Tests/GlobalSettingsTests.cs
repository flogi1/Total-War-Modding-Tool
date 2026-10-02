namespace TotalWarModTool.Settings.Tests;

public class GlobalSettingsTests
{
    [Fact] public void Defaults()
    {
        var s = new GlobalSettings();
        Assert.Equal(("en", ThemeChoice.System, (WindowPlacement?)null), (s.Language, s.Theme, s.MainWindow));
    }

    [Fact] public void Changed_is_raised_with_property_name_only_on_real_change()
    {
        var s = new GlobalSettings(); var names = new List<string>(); s.Changed += (_, n) => names.Add(n);
        s.Language = "en"; s.Language = " DE "; s.Language = "de";
        s.Theme = ThemeChoice.System; s.Theme = ThemeChoice.Dark;
        s.MainWindow = new(1, 2, 800, 600, false); s.MainWindow = new(1, 2, 800, 600, false);
        Assert.Equal(["Language", "Theme", "MainWindow"], names);
        Assert.Equal("de", s.Language);
    }

    [Fact] public void Blank_language_becomes_en() { var s = new GlobalSettings { Language = "de" }; s.Language = "  "; Assert.Equal("en", s.Language); }
}