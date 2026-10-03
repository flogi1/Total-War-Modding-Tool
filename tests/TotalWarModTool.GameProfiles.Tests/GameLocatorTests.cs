namespace TotalWarModTool.GameProfiles.Tests;

public class GameLocatorTests
{
    private static readonly GameProfile TestGame = new("testgame", "Test Game", 42, "Test Game", "packs", "PFH5", "schema_test.ron", ["release"], null);

    private sealed class FakeSteam(string? path) : ISteamLocationSource
    {
        public string? GetSteamPath() => path;
    }

    // Creates <root>\steamapps\common\Test Game\packs and returns the install path.
    private static string Install(string libraryRoot, bool withWorkshop)
    {
        var install = Path.Combine(libraryRoot, "steamapps", "common", "Test Game");
        Directory.CreateDirectory(Path.Combine(install, "packs"));
        if (withWorkshop)
        {
            Directory.CreateDirectory(Path.Combine(libraryRoot, "steamapps", "workshop", "content", "42"));
        }

        return install;
    }

    [Fact]
    public void ReadLibraryFolders_unescapes_paths()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "steamapps"));
        File.WriteAllText(Path.Combine(dir.Path, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + dir.Path.Replace(@"\", @"\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\Steam Library\"\n\t}\n}\n");
        Assert.Equal([dir.Path, @"D:\Steam Library"], GameLocator.ReadLibraryFolders(dir.Path));
    }

    [Fact]
    public void ReadLibraryFolders_without_file_returns_only_the_steam_path()
    {
        using var dir = new TempDir();
        Assert.Equal([dir.Path], GameLocator.ReadLibraryFolders(dir.Path));
    }

    [Fact]
    public void Find_locates_the_game_in_a_second_library_with_workshop()
    {
        using var steam = new TempDir();
        using var library = new TempDir();
        Directory.CreateDirectory(Path.Combine(steam.Path, "steamapps"));
        File.WriteAllText(Path.Combine(steam.Path, "steamapps", "libraryfolders.vdf"),
            "\"path\" \"" + library.Path.Replace(@"\", @"\\") + "\"");
        var install = Install(library.Path, withWorkshop: true);
        var location = GameLocator.Find(TestGame, new FakeSteam(steam.Path));
        Assert.Equal(new GameLocation(install, Path.Combine(install, "packs"),
            Path.Combine(library.Path, "steamapps", "workshop", "content", "42")), location);
    }

    [Fact]
    public void Find_skips_missing_libraries()
    {
        using var steam = new TempDir();
        Directory.CreateDirectory(Path.Combine(steam.Path, "steamapps"));
        File.WriteAllText(Path.Combine(steam.Path, "steamapps", "libraryfolders.vdf"), "\"path\" \"Q:\\\\Unplugged\"");
        var install = Install(steam.Path, withWorkshop: false);
        var location = GameLocator.Find(TestGame, new FakeSteam(steam.Path));
        Assert.Equal((install, (string?)null), (location!.InstallPath, location.WorkshopPath));
    }

    [Fact]
    public void Steam_path_with_forward_slashes_is_normalized_and_not_duplicated()
    {
        using var steam = new TempDir();
        Directory.CreateDirectory(Path.Combine(steam.Path, "steamapps"));
        File.WriteAllText(Path.Combine(steam.Path, "steamapps", "libraryfolders.vdf"),
            "\"path\" \"" + steam.Path.Replace(@"\", @"\\") + "\"");
        var install = Install(steam.Path, withWorkshop: true);
        var slashes = steam.Path.Replace('\\', '/');

        Assert.Equal([steam.Path], GameLocator.ReadLibraryFolders(slashes));
        var location = GameLocator.Find(TestGame, new FakeSteam(slashes));
        Assert.Equal(install, location!.InstallPath);
        Assert.DoesNotContain('/', location.InstallPath + location.DataPath + location.WorkshopPath);
    }

    [Fact]
    public void Invalid_steam_and_library_paths_are_skipped()
    {
        using var steam = new TempDir();
        Directory.CreateDirectory(Path.Combine(steam.Path, "steamapps"));
        File.WriteAllText(Path.Combine(steam.Path, "steamapps", "libraryfolders.vdf"), "\"path\" \"Z:\\\\bad\0name\"");
        Assert.Equal([steam.Path], GameLocator.ReadLibraryFolders(steam.Path));
        Assert.Empty(GameLocator.ReadLibraryFolders("bad\0path"));
        Assert.Null(GameLocator.Find(TestGame, new FakeSteam("bad\0path")));
    }

    [Theory]
    [InlineData(false)]   // no steam at all
    [InlineData(true)]    // steam present, game not installed
    public void Find_returns_null_when_the_game_is_not_there(bool steamExists)
    {
        using var steam = new TempDir();
        Assert.Null(GameLocator.Find(TestGame, new FakeSteam(steamExists ? steam.Path : null)));
    }

    [Fact]
    public void FromInstallPath_rejects_a_folder_without_data()
    {
        using var dir = new TempDir();
        Assert.Null(GameLocator.FromInstallPath(TestGame, dir.Path));
    }

    [Fact]
    public void FromInstallPath_outside_a_steam_library_has_no_workshop()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "packs"));
        Assert.Equal(new GameLocation(dir.Path, Path.Combine(dir.Path, "packs"), null), GameLocator.FromInstallPath(TestGame, dir.Path));
    }
}
