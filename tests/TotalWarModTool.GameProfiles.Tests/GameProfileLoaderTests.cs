namespace TotalWarModTool.GameProfiles.Tests;

public class GameProfileLoaderTests
{
    private const string ValidJson = """
        {"schemaVersion":1,"id":"testgame","displayName":"Test Game","steamAppId":42,"installFolderName":"Test Game",
         "dataFolder":"packs","packFormat":"PFH5","schemaFile":"schema_test.ron","vanillaPackTypes":["release"]}
        """;

    [Fact]
    public void Loads_a_valid_profile_without_manifest()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("testgame.json"), ValidJson);
        var result = GameProfileLoader.LoadAll(dir.Path);
        Assert.Empty(result.Problems);
        var p = Assert.Single(result.Profiles);
        Assert.Equal(("testgame", "Test Game", 42, "Test Game", "packs", "PFH5", "schema_test.ron"),
            (p.Id, p.DisplayName, p.SteamAppId, p.InstallFolderName, p.DataFolder, p.PackFormat, p.SchemaFile));
        Assert.Equal(["release"], p.VanillaPackTypes);
        Assert.Null(p.VanillaManifestFile);
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("[1]")]
    [InlineData("""{"id":"x"}""")]                                                     // schemaVersion missing
    [InlineData("""{"schemaVersion":2,"id":"x","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]
    [InlineData("""{"schemaVersion":1,"id":"","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":0,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":[]}""")]
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":"one","installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]
    [InlineData("""{"schemaVersion":1,"id":"\ud800","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]   // lone surrogate in id
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["\ud800"]}""")]   // lone surrogate in vanillaPackTypes
    public void Skips_a_broken_profile_and_keeps_the_good_one(string broken)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a_broken.json"), broken);
        File.WriteAllText(dir.File("b_good.json"), ValidJson);
        var result = GameProfileLoader.LoadAll(dir.Path);
        Assert.Equal("testgame", Assert.Single(result.Profiles).Id);
        var problem = Assert.Single(result.Problems);
        Assert.Equal("a_broken.json", problem.FileName);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public void Duplicate_id_keeps_the_first_file()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.json"), ValidJson);
        File.WriteAllText(dir.File("b.json"), ValidJson.Replace("Test Game\",\"steamAppId", "Other\",\"steamAppId"));
        var result = GameProfileLoader.LoadAll(dir.Path);
        Assert.Equal("Test Game", Assert.Single(result.Profiles).DisplayName);
        Assert.Equal("b.json", Assert.Single(result.Problems).FileName);
    }

    [Fact]
    public void Missing_folder_gives_an_empty_result()
    {
        var result = GameProfileLoader.LoadAll(@"X:\does\not\exist");
        Assert.Empty(result.Profiles);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Shipped_profiles_load_without_problems()
    {
        var result = GameProfileLoader.LoadAll(Path.Combine(RepoRoot.Find(), "profiles"));
        Assert.Empty(result.Problems);
        var wh3 = Assert.Single(result.Profiles, p => p.Id == "warhammer_3");
        Assert.Equal((1142710, "Total War WARHAMMER III", "data", "PFH5", "schema_wh3.ron", "manifest.txt"),
            (wh3.SteamAppId, wh3.InstallFolderName, wh3.DataFolder, wh3.PackFormat, wh3.SchemaFile, wh3.VanillaManifestFile));
    }
}
