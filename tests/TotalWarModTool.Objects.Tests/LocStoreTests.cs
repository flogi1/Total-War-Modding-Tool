using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects.Tests;

public class LocStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static LocRow Row(string key, string text) => new(key, text, false);

    private static string LocPack(string path, string entryPath, params LocRow[] rows) =>
        new PackBuilder().AddEntry(entryPath, LocEncoder.Encode(rows)).WriteTo(path);

    [Fact]
    public void Loc_pack_then_mods_then_project_override_each_other()
    {
        using var tmp = new TempDir();
        var locPack = TestSources.Vanilla(TestSources.WritePack(tmp.File("local_en.pack"), TestSchema.Build(), [],
            [("db/a", [Row("k1", "one"), Row("k2", "two")])]));
        var mod = TestSources.Mod(TestSources.WritePack(tmp.File("mod.pack"), TestSchema.Build(), [], [("m", [Row("k2", "two mod")])]));
        var proj = Path.Combine(tmp.Path, "proj");
        TestSources.WriteProjectLoc(proj, @"text\p.loc.tsv", [Row("k3", "three")]);
        var project = TestSources.Project(proj);

        var table = new LocStore().Get(TestSources.OrderWithLoc(locPack, locPack, mod, project), None);

        Assert.Empty(table.Problems);
        Assert.Equal(3, table.Count);
        Assert.True(table.TryGet("k1", out var k1));
        Assert.Equal("one", k1.Text);
        Assert.Equal(locPack, k1.Source);
        Assert.True(table.TryGet("k2", out var k2));
        Assert.Equal("two mod", k2.Text);
        Assert.Equal(mod, k2.Source);
        Assert.True(table.TryGet("k3", out var k3));
        Assert.Equal("three", k3.Text);
        Assert.Equal(project, k3.Source);
        Assert.False(table.TryGet("k9", out _));
    }

    [Fact]
    public void Vanilla_packs_other_than_the_loc_pack_are_not_read_for_loc()
    {
        using var tmp = new TempDir();
        var data = TestSources.Vanilla(LocPack(tmp.File("data.pack"), "text/x.loc", Row("k5", "five")));
        var locPack = TestSources.Vanilla(LocPack(tmp.File("local_en.pack"), "text/y.loc", Row("k6", "six")));

        var table = new LocStore().Get(TestSources.OrderWithLoc(locPack, data, locPack), None);

        Assert.False(table.TryGet("k5", out _));
        Assert.True(table.TryGet("k6", out _));
        Assert.Equal(1, table.Count);
    }

    [Fact]
    public void A_missing_loc_pack_gives_an_empty_table_without_problems()
    {
        using var tmp = new TempDir();
        var data = TestSources.Vanilla(LocPack(tmp.File("data.pack"), "text/x.loc", Row("k5", "five")));

        var table = new LocStore().Get(TestSources.Order(data), None);

        Assert.Equal(0, table.Count);
        Assert.Empty(table.Problems);
    }

    [Fact]
    public void A_broken_loc_entry_is_a_problem()
    {
        using var tmp = new TempDir();
        var locPack = TestSources.Vanilla(LocPack(tmp.File("local_en.pack"), "text/ok.loc", Row("k1", "one")));
        var mod = TestSources.Mod(new PackBuilder()
            .AddEntry("text/bad.loc", "xx"u8.ToArray())
            .AddEntry("text/good.loc", LocEncoder.Encode([Row("k2", "two")]))
            .WriteTo(tmp.File("mod.pack")));

        var table = new LocStore().Get(TestSources.OrderWithLoc(locPack, locPack, mod), None);

        var problem = Assert.Single(table.Problems);
        Assert.Equal(SourceProblemKind.DecodeFailed, problem.Kind);
        Assert.Equal("text/bad.loc", problem.EntryPath);
        Assert.Equal(mod, problem.Source);
        Assert.True(table.TryGet("k1", out _));
        Assert.True(table.TryGet("k2", out _));
    }

    [Fact]
    public void A_broken_project_loc_file_is_a_problem()
    {
        using var tmp = new TempDir();
        var proj = Path.Combine(tmp.Path, "proj");
        Directory.CreateDirectory(Path.Combine(proj, "pack", "text"));
        File.WriteAllText(Path.Combine(proj, "pack", "text", "p.loc.tsv"), "kaputt");

        var table = new LocStore().Get(TestSources.Order(TestSources.Project(proj)), None);

        var problem = Assert.Single(table.Problems);
        Assert.Equal(SourceProblemKind.TsvInvalid, problem.Kind);
        Assert.Equal("pack/text/p.loc.tsv", problem.EntryPath.Replace('\\', '/'));
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void The_loc_table_is_cached_until_a_source_changes()
    {
        using var tmp = new TempDir();
        var locPack = TestSources.Vanilla(LocPack(tmp.File("local_en.pack"), "text/y.loc", Row("k1", "one")));
        var modPath = LocPack(tmp.File("mod.pack"), "text/m.loc", Row("k2", "two"));
        var order = TestSources.OrderWithLoc(locPack, locPack, TestSources.Mod(modPath));
        var store = new LocStore();

        var first = store.Get(order, None);
        Assert.Same(first, store.Get(order, None));

        LocPack(modPath, "text/m.loc", Row("k2", "two"), Row("k3", "three"));
        var second = store.Get(order, None);

        Assert.NotSame(first, second);
        Assert.Equal(3, second.Count);
    }

    [Fact]
    public void A_changed_project_loc_file_gives_a_new_table()
    {
        using var tmp = new TempDir();
        var proj = Path.Combine(tmp.Path, "proj");
        var order = TestSources.Order(TestSources.Project(proj));
        var store = new LocStore();
        var before = store.Get(order, None);

        TestSources.WriteProjectLoc(proj, @"text\p.loc.tsv", [Row("k1", "one")]);
        var after = store.Get(order, None);

        Assert.NotSame(before, after);
        Assert.Equal(1, after.Count);
    }

    [Fact]
    public void Cancellation_throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new LocStore().Get(TestSources.Order(), cts.Token));
    }
}
