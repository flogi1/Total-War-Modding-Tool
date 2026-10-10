using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using static TotalWarModTool.Objects.Tests.TestSchema;

namespace TotalWarModTool.Objects.Tests;

/// <summary>A file of a later source with the same pack path replaces the earlier file as a whole, like the game does (spec 5.5).</summary>
public class FileReplacementTests
{
    private static readonly GameSchema Schema = Build(("units_tables", 2, [Field("key", key: true), Field("name"), Field("cost", FieldType.I32)]));

    private static readonly CancellationToken None = CancellationToken.None;

    private static TableStore NewStore() => new(Schema, ReferenceGraph.Build(Schema));

    private static object?[] U(string key, string name, int cost) => [key, name, cost];

    private static string Keys(MergedTable table) => string.Join(",", table.Rows.Select(r => (string)r.Cells[0]!));

    private static LocRow Loc(string key, string text) => new(key, text, false);

    [Fact]
    public void A_project_file_with_the_same_pack_path_replaces_the_vanilla_file()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePackEntries(tmp.File("vanilla.pack"), Schema,
            ("data__", "units_tables", 2, [U("u1", "Alpha", 10), U("u2", "Beta", 20)])));
        var proj = Path.Combine(tmp.Path, "proj");
        // The pack path differs from the vanilla one only in case: the game compares them without regard to case.
        TestSources.WriteProjectTable(proj, Schema, "units_tables", 2, "Data__", [U("u2", "Beta project", 22)]);
        var project = TestSources.Project(proj);

        var table = NewStore().Get("units_tables", TestSources.Order(vanilla, project), None);

        Assert.Empty(table.Problems);
        var row = Assert.Single(table.Rows);
        Assert.Equal("u2", row.Cells[0]);
        Assert.Equal("Beta project", row.Cells[1]);
        Assert.Equal(project, row.Provenance.Source);
        // The vanilla file is gone, so nothing was overridden.
        Assert.Empty(row.Provenance.Overridden);
        Assert.Empty(row.Provenance.OverriddenEntries);
    }

    [Fact]
    public void A_mod_file_replaces_the_vanilla_file_but_not_its_siblings()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePackEntries(tmp.File("vanilla.pack"), Schema,
            ("data__", "units_tables", 2, [U("u1", "Alpha", 10)]),
            ("extra", "units_tables", 2, [U("u3", "Gamma", 30)])));
        var mod = TestSources.Mod(TestSources.WritePackEntries(tmp.File("mod.pack"), Schema, ("data__", "units_tables", 2, [U("u2", "Beta mod", 25)])));

        var table = NewStore().Get("units_tables", TestSources.Order(vanilla, mod), None);

        Assert.Empty(table.Problems);
        Assert.Equal("u3,u2", Keys(table));
        Assert.Equal(vanilla, table.FindByKey(["u3"])[0].Provenance.Source);
        Assert.Equal(mod, table.FindByKey(["u2"])[0].Provenance.Source);
        Assert.Empty(table.FindByKey(["u1"]));
    }

    [Fact]
    public void A_later_project_row_still_overrides_rows_of_other_vanilla_files()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema, ("units_tables", 2, [U("u1", "Alpha", 10)])));
        var proj = Path.Combine(tmp.Path, "proj");
        TestSources.WriteProjectTable(proj, Schema, "units_tables", 2, "a", [U("u1", "Project", 99)]);
        var project = TestSources.Project(proj);

        var table = NewStore().Get("units_tables", TestSources.Order(vanilla, project), None);

        var row = Assert.Single(table.Rows);
        Assert.Equal("Project", row.Cells[1]);
        Assert.Equal([vanilla], row.Provenance.Overridden);
        // The pack names its file after itself, so the project row overrides the vanilla row instead of replacing the file.
        Assert.Equal(["db/units_tables/vanilla"], row.Provenance.OverriddenEntries);
    }

    [Fact]
    public void Overridden_layers_keep_their_entry_paths()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePackEntries(tmp.File("vanilla.pack"), Schema, ("data__", "units_tables", 2, [U("u1", "Alpha", 10)])));
        var mod = TestSources.Mod(TestSources.WritePackEntries(tmp.File("mod.pack"), Schema, ("data_mod", "units_tables", 2, [U("u1", "Mod", 20)])));
        var proj = Path.Combine(tmp.Path, "proj");
        TestSources.WriteProjectTable(proj, Schema, "units_tables", 2, "a", [U("u1", "Project", 99)]);
        var project = TestSources.Project(proj);

        var table = NewStore().Get("units_tables", TestSources.Order(vanilla, mod, project), None);

        var row = Assert.Single(table.Rows);
        Assert.Equal(project, row.Provenance.Source);
        Assert.Equal([vanilla, mod], row.Provenance.Overridden);
        Assert.Equal(["db/units_tables/data__", "db/units_tables/data_mod"], row.Provenance.OverriddenEntries);
        Assert.Equal(Path.Combine("pack", "db", "units_tables", "a.tsv"), row.Provenance.EntryPath);
    }

    [Fact]
    public void A_row_nothing_overrides_has_no_overridden_entries()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema, ("units_tables", 2, [U("u1", "Alpha", 10)])));

        var table = NewStore().Get("units_tables", TestSources.Order(vanilla), None);

        Assert.Empty(Assert.Single(table.Rows).Provenance.OverriddenEntries);
    }

    [Fact]
    public void The_signature_changes_when_a_replacing_file_appears()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePackEntries(tmp.File("vanilla.pack"), Schema, ("data__", "units_tables", 2, [U("u1", "Alpha", 10)])));
        var proj = Path.Combine(tmp.Path, "proj");
        TestSources.WriteProjectTable(proj, Schema, "units_tables", 2, "other", [U("u9", "Other", 1)]);
        var order = TestSources.Order(vanilla, TestSources.Project(proj));
        var store = NewStore();
        var before = store.SignatureOf("units_tables", order, None);
        var first = store.Get("units_tables", order, None);

        TestSources.WriteProjectTable(proj, Schema, "units_tables", 2, "data__", [U("u2", "Beta", 2)]);

        Assert.NotEqual(before, store.SignatureOf("units_tables", order, None));
        var second = store.Get("units_tables", order, None);
        Assert.NotSame(first, second);
        // Project files are read by name: data__ before other, and data__ replaced the vanilla file of the same name.
        Assert.Equal("u2,u9", Keys(second));
    }

    [Fact]
    public void A_project_loc_file_replaces_the_pack_loc_file()
    {
        using var tmp = new TempDir();
        var locPack = TestSources.Vanilla(TestSources.WritePack(tmp.File("local_en.pack"), Build(), [],
            [("db/data__", [Loc("k1", "one"), Loc("k2", "two")]), ("db/other", [Loc("k4", "four")])]));
        var proj = Path.Combine(tmp.Path, "proj");
        TestSources.WriteProjectLoc(proj, @"text\db\data__.loc.tsv", [Loc("k2", "two project")]);
        var project = TestSources.Project(proj);

        var table = new LocStore().Get(TestSources.OrderWithLoc(locPack, locPack, project), None);

        Assert.Empty(table.Problems);
        Assert.False(table.TryGet("k1", out _));
        Assert.True(table.TryGet("k2", out var k2));
        Assert.Equal("two project", k2.Text);
        Assert.Equal(project, k2.Source);
        // Another loc file of the pack stays.
        Assert.True(table.TryGet("k4", out _));
    }

    [Fact]
    public void A_mod_loc_file_replaces_the_loc_pack_file_with_a_different_spelling()
    {
        using var tmp = new TempDir();
        var locPack = TestSources.Vanilla(TestSources.WritePack(tmp.File("local_en.pack"), Build(), [], [("db/data__", [Loc("k1", "one")])]));
        var mod = TestSources.Mod(TestSources.WritePack(tmp.File("mod.pack"), Build(), [], [("DB/Data__", [Loc("k2", "two")])]));

        var table = new LocStore().Get(TestSources.OrderWithLoc(locPack, locPack, mod), None);

        Assert.False(table.TryGet("k1", out _));
        Assert.True(table.TryGet("k2", out _));
    }

    [Theory]
    [InlineData("db/units_tables/data__", null, "db/units_tables/data__")]
    [InlineData(@"pack\db\units_tables\Data__.tsv", "x", "db/units_tables/Data__")]
    [InlineData(@"pack\text\db\data__.loc.tsv", "x", "text/db/data__.loc")]
    [InlineData(@"pack\sub\a.loc.tsv", "x", "sub/a.loc")]
    [InlineData(@"pack\sub\a.png", "x", "sub/a.png")]
    public void PackPathOf_gives_the_path_the_game_sees(string entryPath, string? fullPath, string expected)
    {
        var entry = new EntryRef(entryPath, null, null, fullPath);

        Assert.Equal(expected, SourceReader.PackPathOf(entry));
    }
}
