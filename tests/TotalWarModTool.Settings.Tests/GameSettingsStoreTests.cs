using TotalWarModTool.Storage;

namespace TotalWarModTool.Settings.Tests;

public class GameSettingsStoreTests
{
    [Fact] public void Round_trip_writes_below_the_game_folder()
    {
        using var dir = new TempDir();
        var store = new GameSettingsStore(dir.Path, "testgame");
        Assert.Equal(Path.Combine(dir.Path, "games", "testgame", "game-settings.json"), store.FilePath);
        store.Save(new GameSettings { InstallPath = @"D:\Games\Test" });
        var loaded = store.Load();
        Assert.Equal((LoadStatus.Loaded, @"D:\Games\Test"), (loaded.Status, loaded.Settings.InstallPath));
    }

    [Fact] public void Missing_file_gives_defaults()
    {
        using var dir = new TempDir();
        var loaded = new GameSettingsStore(dir.Path, "testgame").Load();
        Assert.Equal((LoadStatus.Missing, (string?)null), (loaded.Status, loaded.Settings.InstallPath));
    }

    [Fact] public void Corrupt_file_is_copied_to_bad_and_defaults_are_used()
    {
        using var dir = new TempDir();
        var store = new GameSettingsStore(dir.Path, "testgame");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "{ broken");
        var loaded = store.Load();
        Assert.Equal(LoadStatus.Corrupt, loaded.Status);
        Assert.True(File.Exists(loaded.BadCopyPath));
        Assert.Null(loaded.Settings.InstallPath);
    }

    [Fact] public void InstallPath_raises_Changed_once_and_blank_becomes_null()
    {
        var s = new GameSettings(); var changes = new List<string>();
        s.Changed += (_, name) => changes.Add(name);
        s.InstallPath = @"D:\x"; s.InstallPath = @"D:\x "; s.InstallPath = "  ";
        Assert.Equal(["InstallPath", "InstallPath"], changes);
        Assert.Null(s.InstallPath);
    }
}
