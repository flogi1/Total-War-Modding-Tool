namespace TotalWarModTool.Settings.Tests;

// Spec 4.8: every setting belongs to exactly one of the two settings files.
public class SettingsFileMapTests
{
    private static string[] Names(Type t) => t.GetProperties().Where(p => p.CanWrite).Select(p => p.Name).Order().ToArray();

    private static object Sample(Type t) =>
        t == typeof(string) ? "x"
        : t == typeof(ThemeChoice) ? ThemeChoice.Dark
        : t == typeof(WindowPlacement) ? new WindowPlacement(1, 2, 3, 4, true)
        : throw new InvalidOperationException($"No sample value for {t}: extend Sample when a setting has a new type.");

    [Fact] public void No_setting_exists_in_both_files() =>
        Assert.Empty(Names(typeof(GlobalSettings)).Intersect(Names(typeof(GameSettings))));

    [Fact] public void Every_global_setting_survives_a_round_trip()
    {
        using var dir = new TempDir();
        var settings = new GlobalSettings();
        foreach (var p in typeof(GlobalSettings).GetProperties().Where(p => p.CanWrite))
            p.SetValue(settings, Sample(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));
        var store = new GlobalSettingsStore(dir.Path);
        store.Save(settings);
        var loaded = store.Load().Settings;
        Assert.All(typeof(GlobalSettings).GetProperties().Where(p => p.CanWrite), p => Assert.Equal(p.GetValue(settings), p.GetValue(loaded)));
    }

    [Fact] public void Every_game_setting_survives_a_round_trip()
    {
        using var dir = new TempDir();
        var settings = new GameSettings();
        foreach (var p in typeof(GameSettings).GetProperties().Where(p => p.CanWrite))
            p.SetValue(settings, Sample(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));
        var store = new GameSettingsStore(dir.Path, "testgame");
        store.Save(settings);
        var loaded = store.Load().Settings;
        Assert.All(typeof(GameSettings).GetProperties().Where(p => p.CanWrite), p => Assert.Equal(p.GetValue(settings), p.GetValue(loaded)));
    }
}
