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
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"..\\x","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]   // dataFolder leaves the install folder
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"C:\\x","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]   // dataFolder is rooted
    [InlineData("""{"schemaVersion":1,"id":"Foo","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]   // id with an upper-case letter
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s\u0000x","vanillaPackTypes":["release"]}""")]   // NUL in schemaFile
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":1,"installFolderName":"X/Y","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]   // separator in installFolderName
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"..","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]   // dataFolder is ..
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaManifestFile":"..\\m.txt","vanillaPackTypes":["release"]}""")]   // vanillaManifestFile leaves the folder
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

    [Fact]
    public void Loc_packs_are_read_with_upper_case_codes()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("testgame.json"), WithExtra("""
            "locPacks":{"de":"local_ge.pack","EN":"local_en.pack"},"defaultLocPack":"local_en.pack"
            """));
        var result = GameProfileLoader.LoadAll(dir.Path);
        Assert.Empty(result.Problems);
        var p = Assert.Single(result.Profiles);
        Assert.Equal(2, p.LocPacks!.Count);
        Assert.Equal("local_ge.pack", p.LocPacks["DE"]);
        Assert.Equal("local_ge.pack", p.LocPacks["de"]);
        Assert.Equal("local_en.pack", p.LocPacks["EN"]);
        Assert.Equal(["DE", "EN"], p.LocPacks.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("local_en.pack", p.DefaultLocPack);
    }

    [Fact]
    public void Missing_loc_packs_give_an_empty_map()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("testgame.json"), ValidJson);
        var result = GameProfileLoader.LoadAll(dir.Path);
        Assert.Empty(result.Problems);
        var p = Assert.Single(result.Profiles);
        Assert.NotNull(p.LocPacks);
        Assert.Empty(p.LocPacks);
        Assert.Null(p.DefaultLocPack);
    }

    [Theory]
    [InlineData("\"locPacks\":[\"x\"]")]
    [InlineData("\"locPacks\":{\"DE\":1}")]
    [InlineData("\"locPacks\":{\"DE\":\"\"}")]
    [InlineData("""
        "locPacks":{"DE":"..\\x.pack"}
        """)]
    [InlineData("\"locPacks\":{\"\":\"local_ge.pack\"}")]
    [InlineData("\"locPacks\":{\"de\":\"a.pack\",\"DE\":\"b.pack\"}")]
    public void A_non_object_loc_packs_value_is_a_problem(string extra)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a_broken.json"), WithExtra(extra));
        File.WriteAllText(dir.File("b_good.json"), ValidJson);
        var result = GameProfileLoader.LoadAll(dir.Path);
        Assert.Equal("testgame", Assert.Single(result.Profiles).Id);
        var problem = Assert.Single(result.Problems);
        Assert.Equal("a_broken.json", problem.FileName);
        Assert.Contains("locPacks", problem.Detail);
    }

    [Fact]
    public void A_lone_surrogate_in_a_loc_pack_name_skips_the_file()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a_broken.json"), WithExtra("""
            "locPacks":{"DE":"\ud800"}
            """));
        var result = GameProfileLoader.LoadAll(dir.Path);
        Assert.Empty(result.Profiles);
        Assert.Equal("a_broken.json", Assert.Single(result.Problems).FileName);
    }

    [Theory]
    [InlineData("\"defaultLocPack\":5")]
    [InlineData("\"defaultLocPack\":\"\"")]
    [InlineData("""
        "defaultLocPack":"..\\x.pack"
        """)]
    public void A_bad_default_loc_pack_is_a_problem(string extra)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a_broken.json"), WithExtra(extra));
        var result = GameProfileLoader.LoadAll(dir.Path);
        Assert.Empty(result.Profiles);
        Assert.Contains("defaultLocPack", Assert.Single(result.Problems).Detail);
    }

    [Fact]
    public void The_shipped_profile_maps_the_measured_languages()
    {
        var result = GameProfileLoader.LoadAll(Path.Combine(RepoRoot.Find(), "profiles"));
        var wh3 = Assert.Single(result.Profiles, p => p.Id == "warhammer_3");
        Assert.Equal(13, wh3.LocPacks!.Count);
        Assert.Equal("local_ge.pack", wh3.LocPacks["DE"]);
        Assert.Equal("local_en.pack", wh3.DefaultLocPack);
    }

    // Adds extra top-level JSON members to the valid profile.
    private static string WithExtra(string members)
    {
        var json = ValidJson.TrimEnd();
        return json[..^1] + "," + members + "}";
    }
}
