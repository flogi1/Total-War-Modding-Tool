using TotalWarModTool.Storage;

namespace TotalWarModTool.Settings.Tests;

public class GlobalSettingsNewFieldsTests
{
    [Fact] public void New_fields_round_trip_through_the_store()
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        store.Save(new GlobalSettings { SelectedGame = "testgame", SchemaFolder = @"D:\schemas" });
        var loaded = store.Load().Settings;
        Assert.Equal(("testgame", @"D:\schemas"), (loaded.SelectedGame, loaded.SchemaFolder));
    }

    [Fact] public void Old_file_without_the_new_fields_loads_with_nulls()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("settings.json"), """{"schemaVersion":1,"language":"de","theme":"Dark"}""");
        var loaded = new GlobalSettingsStore(dir.Path).Load();
        Assert.Equal((LoadStatus.Loaded, "de", (string?)null, (string?)null),
            (loaded.Status, loaded.Settings.Language, loaded.Settings.SelectedGame, loaded.Settings.SchemaFolder));
    }

    [Theory]
    [InlineData("  ", null)]
    [InlineData("", null)]
    [InlineData("  x  ", "x")]
    public void Blank_values_become_null_and_text_is_trimmed(string input, string? expected)
    {
        var s = new GlobalSettings { SelectedGame = input, SchemaFolder = input };
        Assert.Equal((expected, expected), (s.SelectedGame, s.SchemaFolder));
    }

    [Fact] public void Changed_is_raised_only_for_a_real_change()
    {
        var s = new GlobalSettings(); var changes = new List<string>();
        s.Changed += (_, name) => changes.Add(name);
        s.SchemaFolder = @"D:\a"; s.SchemaFolder = @" D:\a "; s.SelectedGame = "g"; s.SelectedGame = "g";
        Assert.Equal(["SchemaFolder", "SelectedGame"], changes);
    }
}
