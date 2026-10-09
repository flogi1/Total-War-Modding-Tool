using TotalWarModTool.GameProfiles;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.Objects.Tests;

public class LoadOrderResolverTests
{
    private static GameProfile Profile() => new(
        "wh3", "WH3", 1, "Total War WARHAMMER III", "data", "PFH5", "schema_wh3.ron", ["boot", "release", "patch"], null,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["DE"] = "local_ge.pack" }, "local_en.pack");

    private static PackSource Vanilla(string path) =>
        new(path, Path.GetFileName(path), PackOrigin.Vanilla, null, null, null, PackProblem.None, null);

    private static PackCatalogResult Catalog(params PackSource[] packs) => new(packs, false, []);

    private static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    /// <summary>Builds the fake game folder with the given vanilla packs and returns its path.</summary>
    private static string Game(TempDir tmp, params string[] vanillaPacks)
    {
        var game = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(Path.Combine(game, "data"));
        foreach (var pack in vanillaPacks)
        {
            Touch(Path.Combine(game, "data", pack));
        }

        return game;
    }

    private static void WriteUsedMods(string game, string text) => File.WriteAllText(Path.Combine(game, "used_mods.txt"), text);

    private static void WriteLanguage(string game, string text) => File.WriteAllText(Path.Combine(game, "data", "language.txt"), text);

    private static LoadOrderRequest Request(string game, PackCatalogResult catalog, string? projectFolder = null, string? projectName = null, string? rootPack = null) =>
        new(Profile(), game, catalog, projectFolder, projectName, rootPack);

    private static PackCatalogResult VanillaCatalog(string game) =>
        Catalog(Vanilla(Path.Combine(game, "data", "data.pack")), Vanilla(Path.Combine(game, "data", "local_en.pack")));

    [Fact]
    public void Vanilla_then_used_mods_then_project()
    {
        using var tmp = new TempDir();
        var game = Game(tmp, "data.pack", "local_en.pack", "m2.pack");
        var m1 = Touch(Path.Combine(tmp.Path, "ws", "1", "m1.pack"));
        var proj = Path.Combine(tmp.Path, "proj");
        Directory.CreateDirectory(proj);
        WriteUsedMods(game, $"add_working_directory \"{Path.Combine(tmp.Path, "ws", "1")}\";\r\nmod \"m1.pack\";\r\nmod \"m2.pack\";\r\n");

        var order = LoadOrderResolver.Resolve(Request(game, VanillaCatalog(game), proj, "My Mod"));

        Assert.Equal(
            [
                new TableSource(SourceKind.Vanilla, "data.pack", Path.Combine(game, "data", "data.pack")),
                new TableSource(SourceKind.Vanilla, "local_en.pack", Path.Combine(game, "data", "local_en.pack")),
                new TableSource(SourceKind.Mod, "m1.pack", m1),
                new TableSource(SourceKind.Mod, "m2.pack", Path.Combine(game, "data", "m2.pack")),
                new TableSource(SourceKind.Project, "My Mod", proj),
            ],
            order.Sources);
        Assert.Empty(order.Warnings);
        Assert.NotNull(order.LocPack);
        Assert.Equal("local_en.pack", order.LocPack.Name);
        Assert.Same(order.Sources[1], order.LocPack);
    }

    [Fact]
    public void A_missing_used_mods_file_gives_a_warning_and_no_mods()
    {
        using var tmp = new TempDir();
        var game = Game(tmp, "data.pack", "local_en.pack");
        var proj = Path.Combine(tmp.Path, "proj");

        var order = LoadOrderResolver.Resolve(Request(game, VanillaCatalog(game), proj));

        var warning = Assert.Single(order.Warnings);
        Assert.Equal(LoadOrderProblem.UsedModsMissing, warning.Problem);
        Assert.Equal([SourceKind.Vanilla, SourceKind.Vanilla, SourceKind.Project], order.Sources.Select(s => s.Kind));
        Assert.Equal("proj", order.Sources[2].Name);
    }

    [Fact]
    public void A_missing_mod_pack_is_skipped_with_its_name()
    {
        using var tmp = new TempDir();
        var game = Game(tmp, "data.pack", "local_en.pack", "a.pack", "b.pack");
        WriteUsedMods(game, "mod \"a.pack\";\nmod \"gone.pack\";\nmod \"b.pack\";\n");

        var order = LoadOrderResolver.Resolve(Request(game, VanillaCatalog(game)));

        var warning = Assert.Single(order.Warnings);
        Assert.Equal(LoadOrderProblem.ModPackMissing, warning.Problem);
        Assert.Equal("gone.pack", warning.Name);
        Assert.Equal(["data.pack", "local_en.pack", "a.pack", "b.pack"], order.Sources.Select(s => s.Name));
    }

    [Fact]
    public void A_mod_name_with_a_path_part_is_treated_as_missing()
    {
        using var tmp = new TempDir();
        var game = Game(tmp, "data.pack", "local_en.pack");
        Touch(Path.Combine(tmp.Path, "evil.pack"));
        WriteUsedMods(game, "mod \"..\\..\\evil.pack\";\nmod \"sub/x.pack\";\n");

        var order = LoadOrderResolver.Resolve(Request(game, VanillaCatalog(game)));

        Assert.Equal([LoadOrderProblem.ModPackMissing, LoadOrderProblem.ModPackMissing], order.Warnings.Select(w => w.Problem));
        Assert.DoesNotContain(order.Sources, s => s.Kind == SourceKind.Mod);
    }

    [Fact]
    public void A_mod_that_is_already_vanilla_is_not_listed_twice_and_a_missing_working_directory_is_skipped()
    {
        using var tmp = new TempDir();
        var game = Game(tmp, "data.pack", "local_en.pack");
        WriteUsedMods(game, $"add_working_directory \"{Path.Combine(tmp.Path, "nowhere")}\";\nmod \"DATA.PACK\";\n");

        var order = LoadOrderResolver.Resolve(Request(game, VanillaCatalog(game)));

        Assert.Equal(["data.pack", "local_en.pack"], order.Sources.Select(s => s.Name));
        Assert.Empty(order.Warnings);
    }

    [Fact]
    public void The_first_working_directory_with_the_pack_wins_over_the_data_folder()
    {
        using var tmp = new TempDir();
        var game = Game(tmp, "data.pack", "local_en.pack", "m.pack");
        var first = Touch(Path.Combine(tmp.Path, "ws", "1", "m.pack"));
        Touch(Path.Combine(tmp.Path, "ws", "2", "m.pack"));
        WriteUsedMods(game, $"add_working_directory \"{Path.Combine(tmp.Path, "ws", "1")}\";\nadd_working_directory \"{Path.Combine(tmp.Path, "ws", "2")}\";\nmod \"m.pack\";\n");

        var order = LoadOrderResolver.Resolve(Request(game, VanillaCatalog(game)));

        Assert.Equal(first, Assert.Single(order.Sources, s => s.Kind == SourceKind.Mod).Path);
    }

    [Fact]
    public void The_root_pack_is_added_behind_the_mods_when_it_is_not_active()
    {
        using var tmp = new TempDir();
        var game = Game(tmp, "data.pack", "local_en.pack", "m1.pack");
        WriteUsedMods(game, "mod \"m1.pack\";\n");
        var proj = Path.Combine(tmp.Path, "proj");
        var root = Path.Combine(tmp.Path, "other", "x.pack");

        var order = LoadOrderResolver.Resolve(Request(game, VanillaCatalog(game), proj, "P", root));

        Assert.Equal(["data.pack", "local_en.pack", "m1.pack", "x.pack", "P"], order.Sources.Select(s => s.Name));
        Assert.Equal(new TableSource(SourceKind.Mod, "x.pack", root), order.Sources[3]);
        var warning = Assert.Single(order.Warnings);
        Assert.Equal(LoadOrderProblem.RootPackAdded, warning.Problem);

        var modPath = Path.Combine(game, "data", "m1.pack");
        var same = LoadOrderResolver.Resolve(Request(game, VanillaCatalog(game), proj, "P", modPath.ToUpperInvariant()));

        Assert.Equal(["data.pack", "local_en.pack", "m1.pack", "P"], same.Sources.Select(s => s.Name));
        Assert.Empty(same.Warnings);
    }

    [Fact]
    public void The_loc_pack_follows_language_txt_and_the_profile()
    {
        using var tmp = new TempDir();
        var game = Game(tmp, "data.pack", "local_en.pack", "local_ge.pack");
        WriteUsedMods(game, "");
        var catalog = VanillaCatalog(game);

        WriteLanguage(game, "de\r\n");
        var german = LoadOrderResolver.Resolve(Request(game, catalog));
        Assert.Equal("local_ge.pack", german.LocPack?.Name);
        Assert.Equal(SourceKind.Vanilla, german.LocPack!.Kind);
        Assert.Equal(Path.Combine(game, "data", "local_ge.pack"), german.LocPack.Path);
        Assert.Empty(german.Warnings);

        WriteLanguage(game, "xx");
        var unknown = LoadOrderResolver.Resolve(Request(game, catalog));
        Assert.Equal("local_en.pack", unknown.LocPack?.Name);

        File.Delete(Path.Combine(game, "data", "local_ge.pack"));
        WriteLanguage(game, "DE");
        var missing = LoadOrderResolver.Resolve(Request(game, catalog));
        Assert.Null(missing.LocPack);
        var warning = Assert.Single(missing.Warnings);
        Assert.Equal(LoadOrderProblem.LocPackMissing, warning.Problem);
        Assert.Equal("local_ge.pack", warning.Name);
    }

    [Fact]
    public void A_loc_pack_that_is_not_in_the_catalog_is_not_added_to_the_sources()
    {
        using var tmp = new TempDir();
        var game = Game(tmp, "data.pack", "local_en.pack");
        WriteUsedMods(game, "");

        var order = LoadOrderResolver.Resolve(Request(game, Catalog(Vanilla(Path.Combine(game, "data", "data.pack")))));

        Assert.Equal(["data.pack"], order.Sources.Select(s => s.Name));
        Assert.Equal(new TableSource(SourceKind.Vanilla, "local_en.pack", Path.Combine(game, "data", "local_en.pack")), order.LocPack);
    }

    [Fact]
    public void A_used_mods_file_that_cannot_be_read_is_a_warning()
    {
        using var tmp = new TempDir();
        var game = Game(tmp, "data.pack", "local_en.pack", "m1.pack");
        WriteUsedMods(game, "mod \"m1.pack\";\n");

        LoadOrder order;
        using (new FileStream(Path.Combine(game, "used_mods.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            order = LoadOrderResolver.Resolve(Request(game, VanillaCatalog(game)));
        }

        var warning = Assert.Single(order.Warnings);
        Assert.Equal(LoadOrderProblem.UsedModsUnreadable, warning.Problem);
        Assert.DoesNotContain(order.Sources, s => s.Kind == SourceKind.Mod);
    }

    [Fact]
    public void A_language_file_that_cannot_be_read_falls_back_to_the_default_loc_pack()
    {
        using var tmp = new TempDir();
        var game = Game(tmp, "data.pack", "local_en.pack", "local_ge.pack");
        WriteUsedMods(game, "");
        WriteLanguage(game, "DE");

        LoadOrder order;
        using (new FileStream(Path.Combine(game, "data", "language.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            order = LoadOrderResolver.Resolve(Request(game, VanillaCatalog(game)));
        }

        var warning = Assert.Single(order.Warnings);
        Assert.Equal(LoadOrderProblem.LanguageFileUnreadable, warning.Problem);
        Assert.Equal("local_en.pack", order.LocPack?.Name);
    }
}
