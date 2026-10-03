using TotalWarModTool.GameProfiles;
using TotalWarModTool.Operations;
using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Workspace.Tests;

public class PackCatalogTests
{
    private static GameProfile Profile(string? manifest) => new("testgame", "Test Game", 42, "Test Game", "packs", "PFH5", "schema_test.ron", ["boot", "release"], manifest);

    private sealed class Layout : IDisposable
    {
        public TempDir Root { get; } = new();
        public string Install => Root.Path;
        public string Data => Path.Combine(Root.Path, "packs");
        public string Workshop => Path.Combine(Root.Path, "workshop");
        public Layout() { Directory.CreateDirectory(Data); Directory.CreateDirectory(Workshop); }
        public void DataPack(string name, uint type) => new PackBuilder(packType: type).WriteTo(Path.Combine(Data, name));
        public void WorkshopPack(string id, string name) { Directory.CreateDirectory(Path.Combine(Workshop, id)); new PackBuilder().WriteTo(Path.Combine(Workshop, id, name)); }
        public void Dispose() => Root.Dispose();
    }

    private static string Describe(PackCatalogResult r) => string.Join(" | ", r.Packs.Select(p => $"{p.Name}:{p.Origin}:{p.WorkshopId}"));

    [Fact] public void Without_manifest_the_pack_type_decides()
    {
        using var l = new Layout();
        l.DataPack("zz_vanilla.pack", 1); l.DataPack("boot.pack", 0); l.DataPack("my_mod.pack", 3); l.DataPack("movie.pack", 4);
        l.WorkshopPack("123", "b_ws.pack"); l.WorkshopPack("99", "A_ws.pack");
        var r = PackCatalog.Scan(Profile(null), l.Install, l.Workshop, null, CancellationToken.None);
        Assert.False(r.UsedManifest);
        Assert.Equal("boot.pack:Vanilla: | zz_vanilla.pack:Vanilla: | movie.pack:DataFolderMod: | my_mod.pack:DataFolderMod: | A_ws.pack:Workshop:99 | b_ws.pack:Workshop:123", Describe(r));
        Assert.All(r.Packs, p => { Assert.Equal(PackProblem.None, p.Problem); Assert.NotNull(p.Fingerprint); Assert.NotNull(p.Header); });
    }

    [Fact] public void With_manifest_only_listed_packs_are_vanilla()
    {
        using var l = new Layout();
        l.DataPack("data.pack", 1); l.DataPack("sneaky_mod.pack", 1); l.DataPack("Local_EN.pack", 3);
        File.WriteAllText(Path.Combine(l.Data, "manifest.txt"), "text/credits.xml\t100\t1\ndata.pack\t5\t1\nlocal_en.pack\t7\t1\nmissing_dlc.pack\t9\t1\n");
        var r = PackCatalog.Scan(Profile("manifest.txt"), l.Install, null, null, CancellationToken.None);
        Assert.True(r.UsedManifest);
        Assert.Equal("data.pack:Vanilla: | Local_EN.pack:Vanilla: | sneaky_mod.pack:DataFolderMod:", Describe(r));
    }

    [Fact] public void Manifest_paths_with_a_directory_prefix_count_by_file_name()
    {
        using var l = new Layout();
        l.DataPack("slash.pack", 3); l.DataPack("back.pack", 3); l.DataPack("other.pack", 1);
        File.WriteAllText(Path.Combine(l.Data, "manifest.txt"), "sub/dir/slash.pack\t5\t1\nsub\\dir\\back.pack\t7\t1\n");
        var r = PackCatalog.Scan(Profile("manifest.txt"), l.Install, null, null, CancellationToken.None);
        Assert.True(r.UsedManifest);
        Assert.Equal("back.pack:Vanilla: | slash.pack:Vanilla: | other.pack:DataFolderMod:", Describe(r));
    }

    [Fact] public void Manifest_pack_with_a_broken_header_is_vanilla_and_keeps_its_problem()
    {
        using var l = new Layout();
        File.WriteAllBytes(Path.Combine(l.Data, "broken.pack"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(l.Data, "manifest.txt"), "broken.pack\t3\t1\n");
        var r = PackCatalog.Scan(Profile("manifest.txt"), l.Install, null, null, CancellationToken.None);
        var broken = Assert.Single(r.Packs);
        Assert.Equal((PackOrigin.Vanilla, PackProblem.Truncated, (PackHeader?)null), (broken.Origin, broken.Problem, broken.Header));
    }

    [Fact] public void Uppercase_pack_extension_is_listed()
    {
        using var l = new Layout();
        l.DataPack("Shout.PACK", 3);
        l.WorkshopPack("8", "Loud.PACK");
        var r = PackCatalog.Scan(Profile(null), l.Install, l.Workshop, null, CancellationToken.None);
        Assert.Equal("Shout.PACK:DataFolderMod: | Loud.PACK:Workshop:8", Describe(r));
    }

    [Fact] public void Named_but_missing_manifest_falls_back_to_the_pack_type()
    {
        using var l = new Layout();
        l.DataPack("data.pack", 1);
        var r = PackCatalog.Scan(Profile("manifest.txt"), l.Install, null, null, CancellationToken.None);
        Assert.Equal((false, "data.pack:Vanilla:"), (r.UsedManifest, Describe(r)));
    }

    [Fact] public void Broken_pack_stays_in_the_list_with_its_problem()
    {
        using var l = new Layout();
        File.WriteAllBytes(Path.Combine(l.Data, "broken.pack"), [1, 2, 3]);
        l.DataPack("good.pack", 1);
        var r = PackCatalog.Scan(Profile(null), l.Install, null, null, CancellationToken.None);
        var broken = Assert.Single(r.Packs, p => p.Name == "broken.pack");
        Assert.Equal((PackOrigin.DataFolderMod, PackProblem.Truncated, (PackHeader?)null), (broken.Origin, broken.Problem, broken.Header));
        Assert.Equal(PackOrigin.Vanilla, Assert.Single(r.Packs, p => p.Name == "good.pack").Origin);
    }

    [Fact] public void Ignores_subfolders_of_data_and_deeper_workshop_levels()
    {
        using var l = new Layout();
        Directory.CreateDirectory(Path.Combine(l.Data, "sub")); new PackBuilder().WriteTo(Path.Combine(l.Data, "sub", "nested.pack"));
        Directory.CreateDirectory(Path.Combine(l.Workshop, "5", "deep")); new PackBuilder().WriteTo(Path.Combine(l.Workshop, "5", "deep", "x.pack"));
        new PackBuilder().WriteTo(Path.Combine(l.Workshop, "loose.pack"));
        File.WriteAllText(Path.Combine(l.Data, "readme.txt"), "x");
        Assert.Empty(PackCatalog.Scan(Profile(null), l.Install, l.Workshop, null, CancellationToken.None).Packs);
    }

    [Fact] public void Missing_folders_give_an_empty_catalog()
    {
        using var root = new TempDir();
        Assert.Empty(PackCatalog.Scan(Profile(null), root.Path, Path.Combine(root.Path, "none"), null, CancellationToken.None).Packs);
    }

    [Fact] public void Null_profile_and_blank_install_path_are_argument_errors()
    {
        Assert.Throws<ArgumentNullException>(() => PackCatalog.Scan(null!, @"C:\game", null, null, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => PackCatalog.Scan(Profile(null), null!, null, null, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => PackCatalog.Scan(Profile(null), "", null, null, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => PackCatalog.Scan(Profile(null), "  ", null, null, CancellationToken.None));
    }

    [Fact] public void Fingerprint_equals_the_one_of_ReadIndex()
    {
        using var l = new Layout();
        l.DataPack("a.pack", 1);
        var source = Assert.Single(PackCatalog.Scan(Profile(null), l.Install, null, null, CancellationToken.None).Packs);
        Assert.Equal(PackReader.ReadIndex(source.Path).Index!.Fingerprint, source.Fingerprint);
    }

    private sealed class Collect : IProgress<OperationProgress> { public List<OperationProgress> Items = []; public void Report(OperationProgress value) => Items.Add(value); }

    [Fact] public void Reports_progress_per_pack()
    {
        using var l = new Layout();
        l.DataPack("a.pack", 1); l.DataPack("b.pack", 3); l.WorkshopPack("7", "c.pack");
        var progress = new Collect();
        PackCatalog.Scan(Profile(null), l.Install, l.Workshop, progress, CancellationToken.None);
        Assert.Equal([1L, 2L, 3L], progress.Items.Select(p => p.Done));
        Assert.All(progress.Items, p => { Assert.Equal(3L, p.Total); Assert.EndsWith(".pack", p.Item); });
    }

    private sealed class CancelAfterFirst(CancellationTokenSource source) : IProgress<OperationProgress> { public void Report(OperationProgress value) => source.Cancel(); }

    [Fact] public void Cancelling_in_the_middle_of_a_scan_throws()
    {
        using var l = new Layout();
        l.DataPack("a.pack", 1); l.DataPack("b.pack", 1); l.WorkshopPack("7", "c.pack");
        using var cts = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => PackCatalog.Scan(Profile(null), l.Install, l.Workshop, new CancelAfterFirst(cts), cts.Token));
    }

    [Fact] public void Cancelled_token_throws()
    {
        using var l = new Layout();
        l.DataPack("a.pack", 1);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PackCatalog.Scan(Profile(null), l.Install, null, null, cts.Token));
    }
}
