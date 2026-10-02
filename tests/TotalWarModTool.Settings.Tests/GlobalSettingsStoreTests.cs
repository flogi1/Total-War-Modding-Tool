using TotalWarModTool.Storage;

namespace TotalWarModTool.Settings.Tests;

public class GlobalSettingsStoreTests
{
    [Fact] public void Save_then_Load_round_trips()
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        store.Save(new GlobalSettings { Language = "de", Theme = ThemeChoice.Dark, MainWindow = new(10, 20, 1200, 800, true) });
        var r = store.Load();
        Assert.Equal(LoadStatus.Loaded, r.Status);
        Assert.Equal(("de", ThemeChoice.Dark, new WindowPlacement(10, 20, 1200, 800, true)), (r.Settings.Language, r.Settings.Theme, r.Settings.MainWindow));
        Assert.Equal(Path.Combine(dir.Path, "settings.json"), store.FilePath);
    }

    [Fact] public void Load_repairs_unknown_theme_blank_language_and_invalid_placement()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("settings.json"),
            "{\"schemaVersion\":1,\"language\":\" \",\"theme\":\"Neon\",\"mainWindow\":{\"left\":0,\"top\":0,\"width\":-5,\"height\":600,\"maximized\":false}}");
        var r = new GlobalSettingsStore(dir.Path).Load();
        Assert.Equal(LoadStatus.Loaded, r.Status);
        Assert.Equal(("en", ThemeChoice.System, (WindowPlacement?)null), (r.Settings.Language, r.Settings.Theme, r.Settings.MainWindow));
    }

    [Fact] public void Load_corrupt_file_returns_defaults_and_bad_copy()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("settings.json"), "garbage");
        var r = new GlobalSettingsStore(dir.Path).Load();
        Assert.Equal((LoadStatus.Corrupt, "en"), (r.Status, r.Settings.Language));
        Assert.True(File.Exists(dir.File("settings.json.bad")));
    }

    [Fact] public void Load_without_file_returns_defaults() { using var dir = new TempDir(); Assert.Equal(LoadStatus.Missing, new GlobalSettingsStore(dir.Path).Load().Status); }

    // System.Text.Json does not enforce nullable annotations: JSON null and missing properties must be repaired, not crash.
    [Theory]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"language\":null,\"theme\":null,\"mainWindow\":null}")]
    [InlineData("{\"schemaVersion\":1,\"mainWindow\":{}}")]
    public void Load_repairs_null_and_missing_members(string json)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("settings.json"), json);
        var r = new GlobalSettingsStore(dir.Path).Load();
        Assert.Equal(LoadStatus.Loaded, r.Status);
        Assert.Equal(("en", ThemeChoice.System, (WindowPlacement?)null), (r.Settings.Language, r.Settings.Theme, r.Settings.MainWindow));
    }

    [Fact] public void Save_never_writes_a_non_finite_placement()
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        store.Save(new GlobalSettings { Language = "de", MainWindow = new(double.NaN, 0, 800, 600, false) });
        var r = store.Load();
        Assert.Equal(LoadStatus.Loaded, r.Status);
        Assert.Equal(("de", (WindowPlacement?)null), (r.Settings.Language, r.Settings.MainWindow));
    }
}