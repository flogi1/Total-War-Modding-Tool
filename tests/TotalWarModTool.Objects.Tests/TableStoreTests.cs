using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using static TotalWarModTool.Objects.Tests.TestSchema;

namespace TotalWarModTool.Objects.Tests;

public class TableStoreTests
{
    private static readonly GameSchema Schema = Build(
        ("units_tables", 2, [Field("key", key: true), Field("name"), Field("cost", FieldType.I32)]),
        ("units_tables", 1, [Field("key", key: true), Field("cost", FieldType.I32)]),
        ("pairs_tables", 1, [Field("a", key: true), Field("b", key: true), Field("n", FieldType.I32)]),
        ("plain_tables", 1, [Field("x")]));

    private static TableStore NewStore(long maxCacheBytes = TableStore.DefaultMaxCacheBytes) =>
        new(Schema, ReferenceGraph.Build(Schema), maxCacheBytes);

    private static object?[] U(string key, string name, int cost) => [key, name, cost];

    private static object?[] Pair(string a, string b, int n) => [a, b, n];

    private static string Keys(MergedTable table) => string.Join(",", table.Rows.Select(r => (string)r.Cells[0]!));

    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public void Rows_of_later_sources_override_earlier_ones_and_keep_the_position()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("units_tables", 2, [U("u1", "Alpha", 10), U("u2", "Beta", 20)])));
        var mod = TestSources.Mod(TestSources.WritePack(tmp.File("mod.pack"), Schema,
            ("units_tables", 2, [U("u2", "Beta mod", 25), U("u3", "Gamma", 30)])));

        var table = NewStore().Get("units_tables", TestSources.Order(vanilla, mod), None);

        Assert.Equal("u1,u2,u3", Keys(table));
        Assert.Empty(table.Problems);
        Assert.Equal(vanilla, table.Rows[0].Provenance.Source);
        Assert.Empty(table.Rows[0].Provenance.Overridden);
        Assert.Equal(mod, table.Rows[1].Provenance.Source);
        Assert.Equal([vanilla], table.Rows[1].Provenance.Overridden);
        Assert.Equal(mod, table.Rows[2].Provenance.Source);
        Assert.Equal("Beta mod", table.FindByKey(["u2"])[0].Cells[1]);
        Assert.Equal([0], table.KeyFields);
        Assert.Equal(3, table.Definition!.Fields.Count);
    }

    [Fact]
    public void A_row_replaced_by_two_later_sources_lists_all_earlier_ones()
    {
        using var tmp = new TempDir();
        var a = TestSources.Vanilla(TestSources.WritePack(tmp.File("a.pack"), Schema, ("units_tables", 2, [U("u1", "A", 1)])));
        var b = TestSources.Mod(TestSources.WritePack(tmp.File("b.pack"), Schema, ("units_tables", 2, [U("u1", "B", 2)])));
        var c = TestSources.Mod(TestSources.WritePack(tmp.File("c.pack"), Schema, ("units_tables", 2, [U("u1", "C", 3)])));

        var table = NewStore().Get("units_tables", TestSources.Order(a, b, c), None);

        var row = Assert.Single(table.Rows);
        Assert.Equal("C", row.Cells[1]);
        Assert.Equal(c, row.Provenance.Source);
        Assert.Equal([a, b], row.Provenance.Overridden);
    }

    [Fact]
    public void An_entry_with_an_older_layout_is_merged_by_field_name()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("units_tables", 2, [U("u1", "Alpha", 10)])));
        var mod = TestSources.Mod(TestSources.WritePack(tmp.File("mod.pack"), Schema,
            ("units_tables", 1, [["u9", 5]])));

        var table = NewStore().Get("units_tables", TestSources.Order(vanilla, mod), None);

        Assert.Empty(table.Problems);
        Assert.Equal("u1,u9", Keys(table));
        Assert.False(table.Rows[0].Provenance.OlderLayout);
        var old = table.Rows[1];
        Assert.True(old.Provenance.OlderLayout);
        Assert.Equal(3, old.Cells.Length);
        Assert.Equal("", old.Cells[1]);
        Assert.Equal(5, old.Cells[2]);
    }

    [Fact]
    public void A_duplicate_key_in_one_source_keeps_both_rows_marked()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("units_tables", 2, [U("u1", "First", 1), U("u1", "Second", 2)])));
        var mod = TestSources.Mod(TestSources.WritePack(tmp.File("mod.pack"), Schema,
            ("units_tables", 2, [U("u1", "Mod", 3)])));
        var store = NewStore();

        var alone = store.Get("units_tables", TestSources.Order(vanilla), None);
        Assert.Equal(2, alone.Rows.Count);
        Assert.All(alone.Rows, r => Assert.True(r.Provenance.Duplicate));
        Assert.Equal(2, alone.FindByKey(["u1"]).Count);

        var merged = store.Get("units_tables", TestSources.Order(vanilla, mod), None);
        var row = Assert.Single(merged.Rows);
        Assert.Equal("Mod", row.Cells[1]);
        Assert.Equal(mod, row.Provenance.Source);
        Assert.Equal([vanilla], row.Provenance.Overridden);
        Assert.False(row.Provenance.Duplicate);
    }

    [Fact]
    public void A_table_without_key_is_appended()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("plain_tables", 1, [["a"], ["b"]])));
        var mod = TestSources.Mod(TestSources.WritePack(tmp.File("mod.pack"), Schema,
            ("plain_tables", 1, [["b"], ["c"]])));

        var table = NewStore().Get("plain_tables", TestSources.Order(vanilla, mod), None);

        Assert.Equal("a,b,b,c", Keys(table));
        Assert.Empty(table.KeyFields);
        Assert.Empty(table.FindByKey(["a"]));
        Assert.Empty(table.FindByKey([]));
        Assert.All(table.Rows, r => Assert.False(r.Provenance.Duplicate));
    }

    [Fact]
    public void Project_tsv_files_are_the_last_source()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("units_tables", 2, [U("u1", "Alpha", 10)])));
        var proj = Path.Combine(tmp.Path, "proj");
        TestSources.WriteProjectTable(proj, Schema, "units_tables", 2, "a", [U("u1", "Project", 99)]);
        var project = TestSources.Project(proj);

        var table = NewStore().Get("units_tables", TestSources.Order(vanilla, project), None);

        Assert.Empty(table.Problems);
        var row = Assert.Single(table.Rows);
        Assert.Equal("Project", row.Cells[1]);
        Assert.Equal(SourceKind.Project, row.Provenance.Source.Kind);
        Assert.Equal([vanilla], row.Provenance.Overridden);
    }

    [Fact]
    public void A_broken_source_is_reported_and_the_others_are_merged()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("units_tables", 2, [U("u1", "Alpha", 10)])));
        File.WriteAllBytes(tmp.File("broken.pack"), new byte[10]);
        var broken = TestSources.Mod(tmp.File("broken.pack"));
        var schemaless = TestSources.Mod(new PackBuilder().AddEntry("db/ghost_tables/data__", [1, 2, 3]).WriteTo(tmp.File("schemaless.pack")));
        var proj = Path.Combine(tmp.Path, "proj");
        Directory.CreateDirectory(Path.Combine(proj, "pack", "db", "units_tables"));
        File.WriteAllText(Path.Combine(proj, "pack", "db", "units_tables", "a.tsv"), "kaputt");
        var project = TestSources.Project(proj);
        var order = TestSources.Order(vanilla, broken, schemaless, project);
        var store = NewStore();

        var units = store.Get("units_tables", order, None);

        Assert.Equal("u1", Keys(units));
        Assert.Equal(2, units.Problems.Count);
        var packProblem = Assert.Single(units.Problems, p => p.Source == broken);
        Assert.Equal(SourceProblemKind.Unreadable, packProblem.Kind);
        Assert.Equal("", packProblem.EntryPath);
        var tsvProblem = Assert.Single(units.Problems, p => p.Source == project);
        Assert.Equal(SourceProblemKind.TsvInvalid, tsvProblem.Kind);
        Assert.Equal("pack/db/units_tables/a.tsv", tsvProblem.EntryPath.Replace('\\', '/'));

        var ghost = store.Get("ghost_tables", order, None);

        Assert.Null(ghost.Definition);
        Assert.Empty(ghost.Rows);
        var notInSchema = Assert.Single(ghost.Problems, p => p.Kind == SourceProblemKind.NotInSchema);
        Assert.Equal(schemaless, notInSchema.Source);
        Assert.Equal("db/ghost_tables/data__", notInSchema.EntryPath);
    }

    [Fact]
    public void A_locked_pack_is_a_problem_not_an_exception()
    {
        using var tmp = new TempDir();
        var path = TestSources.WritePack(tmp.File("vanilla.pack"), Schema, ("units_tables", 2, [U("u1", "Alpha", 10)]));
        var vanilla = TestSources.Vanilla(path);
        var store = NewStore();

        MergedTable locked;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            locked = store.Get("units_tables", TestSources.Order(vanilla), None);
        }

        Assert.Empty(locked.Rows);
        var problem = Assert.Single(locked.Problems);
        Assert.Equal(SourceProblemKind.Unreadable, problem.Kind);
        Assert.Equal(vanilla, problem.Source);

        // The lock is gone: the failure was not remembered.
        var free = store.Get("units_tables", TestSources.Order(vanilla), None);
        Assert.NotSame(locked, free);
        Assert.Single(free.Rows);
        Assert.Empty(free.Problems);
    }

    [Fact]
    public void The_same_order_returns_the_cached_instance()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema, ("units_tables", 2, [U("u1", "Alpha", 10)])));
        var store = NewStore();
        var order = TestSources.Order(vanilla);

        var first = store.Get("units_tables", order, None);
        var second = store.Get("units_tables", order, None);

        Assert.Same(first, second);
        Assert.Equal(1, store.CachedTables);
        Assert.Equal(first.EstimatedBytes, store.CachedBytes);
    }

    [Fact]
    public void A_changed_pack_invalidates_only_its_tables()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("units_tables", 2, [U("u1", "Alpha", 10)]),
            ("pairs_tables", 1, [Pair("a1", "b1", 1)])));
        var modPath = TestSources.WritePack(tmp.File("mod.pack"), Schema, ("units_tables", 2, [U("u2", "Beta", 20)]));
        var order = TestSources.Order(vanilla, TestSources.Mod(modPath));
        var store = NewStore();
        var units = store.Get("units_tables", order, None);
        var pairs = store.Get("pairs_tables", order, None);

        TestSources.WritePack(modPath, Schema, ("units_tables", 2, [U("u2", "Beta", 20), U("u3", "Gamma", 30)]));

        var unitsAfter = store.Get("units_tables", order, None);
        var pairsAfter = store.Get("pairs_tables", order, None);
        Assert.NotSame(units, unitsAfter);
        Assert.Equal(3, unitsAfter.Rows.Count);
        Assert.Same(pairs, pairsAfter);
        Assert.Equal(2, store.CachedTables);
        Assert.Equal(unitsAfter.EstimatedBytes + pairs.EstimatedBytes, store.CachedBytes);
    }

    [Fact]
    public void A_changed_project_file_invalidates_the_table()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema, ("units_tables", 2, [U("u1", "Alpha", 10)])));
        var proj = Path.Combine(tmp.Path, "proj");
        var order = TestSources.Order(vanilla, TestSources.Project(proj));
        var store = NewStore();

        var before = store.Get("units_tables", order, None);
        TestSources.WriteProjectTable(proj, Schema, "units_tables", 2, "a", [U("u1", "Project", 1)]);
        var after = store.Get("units_tables", order, None);

        Assert.NotSame(before, after);
        Assert.Equal("Project", Assert.Single(after.Rows).Cells[1]);
        Assert.Equal(1, store.CachedTables);
    }

    [Fact]
    public void Two_load_orders_do_not_share_merged_tables()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("units_tables", 2, [U("u1", "Alpha", 10), U("u2", "Beta", 20)])));
        var mod = TestSources.Mod(TestSources.WritePack(tmp.File("mod.pack"), Schema, ("units_tables", 2, [U("u3", "Gamma", 30)])));
        var store = NewStore();

        var a = store.Get("units_tables", TestSources.Order(vanilla), None);
        var b = store.Get("units_tables", TestSources.Order(vanilla, mod), None);

        Assert.NotSame(a, b);
        Assert.Equal(2, a.Rows.Count);
        Assert.Equal(3, b.Rows.Count);
        Assert.Equal(2, store.CachedTables);
    }

    [Fact]
    public void A_table_above_the_limit_is_returned_but_not_cached()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("units_tables", 2, [U("u1", "Alpha", 10), U("u2", "Beta", 20)])));
        var store = NewStore(maxCacheBytes: 100);

        var table = store.Get("units_tables", TestSources.Order(vanilla), None);

        Assert.Equal(2, table.Rows.Count);
        Assert.True(table.EstimatedBytes > 100);
        Assert.Equal(0, store.CachedTables);
        Assert.Equal(0, store.CachedBytes);
    }

    [Fact]
    public void The_least_recently_used_table_is_evicted()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("units_tables", 2, [U("u1", "Alpha", 10)]),
            ("pairs_tables", 1, [Pair("a1", "b1", 1)]),
            ("plain_tables", 1, [["x"]])));
        var order = TestSources.Order(vanilla);
        var probe = NewStore();
        var limit = new[] { "units_tables", "pairs_tables", "plain_tables" }.Sum(t => probe.Get(t, order, None).EstimatedBytes) - 1;
        var store = NewStore(limit);

        var units = store.Get("units_tables", order, None);
        var pairs = store.Get("pairs_tables", order, None);
        Assert.Same(units, store.Get("units_tables", order, None));
        store.Get("plain_tables", order, None);

        Assert.Equal(2, store.CachedTables);
        Assert.True(store.CachedBytes <= limit);
        Assert.Same(units, store.Get("units_tables", order, None));
        Assert.NotSame(pairs, store.Get("pairs_tables", order, None));
    }

    [Fact]
    public void Cancellation_throws_and_caches_nothing()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema, ("units_tables", 2, [U("u1", "Alpha", 10)])));
        var store = NewStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => store.Get("units_tables", TestSources.Order(vanilla), cts.Token));

        Assert.Equal(0, store.CachedTables);
        Assert.Equal(0, store.CachedBytes);
    }

    [Fact]
    public void A_composite_key_is_found_by_all_values()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("pairs_tables", 1, [Pair("a1", "b1", 1), Pair("a1", "b2", 2)])));

        var table = NewStore().Get("pairs_tables", TestSources.Order(vanilla), None);

        Assert.Equal([0, 1], table.KeyFields);
        Assert.Equal(2, Assert.Single(table.FindByKey(["a1", "b2"])).Cells[2]);
        Assert.Empty(table.FindByKey(["a1"]));
        Assert.Empty(table.FindByKey(["b2", "a1"]));
        Assert.Equal(2, table.FindByField("a", "a1").Count);
        Assert.Equal(2, table.FindByField("N", "2").Single().Cells[2]);
        Assert.Empty(table.FindByField("zzz", "a1"));
        Assert.Equal(-1, table.FieldIndex("zzz"));
        Assert.Equal(2, table.FieldIndex("n"));
    }


    [Fact]
    public void A_field_whose_type_changed_is_converted_through_text()
    {
        var schema = Build(
            ("conv_tables", 2, [Field("key", key: true), Field("cost", FieldType.F32)]),
            ("conv_tables", 1, [Field("key", key: true), Field("cost", FieldType.I32)]),
            ("text_tables", 2, [Field("key", key: true), Field("cost", FieldType.I32)]),
            ("text_tables", 1, [Field("key", key: true), Field("cost")]));
        using var tmp = new TempDir();
        var mod = TestSources.Mod(TestSources.WritePack(tmp.File("mod.pack"), schema,
            ("conv_tables", 1, [["k1", 5]]),
            ("text_tables", 1, [["k1", "abc"], ["k2", "7"]])));
        var store = new TableStore(schema, ReferenceGraph.Build(schema));
        var order = TestSources.Order(mod);

        var converted = store.Get("conv_tables", order, None);
        var row = Assert.Single(converted.Rows);
        Assert.Equal(5f, row.Cells[1]);
        Assert.True(row.Provenance.OlderLayout);

        var text = store.Get("text_tables", order, None);
        Assert.Empty(text.Problems);
        Assert.Equal(0, text.FindByKey(["k1"])[0].Cells[1]);
        Assert.Equal(7, text.FindByKey(["k2"])[0].Cells[1]);
        Assert.All(text.Rows, r => Assert.True(r.Provenance.OlderLayout));
    }

    [Fact]
    public void A_pack_that_gains_the_table_invalidates_it()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("units_tables", 2, [U("u1", "Alpha", 10), U("u2", "Beta", 20)])));
        var modPath = TestSources.WritePack(tmp.File("mod.pack"), Schema, ("pairs_tables", 1, [Pair("a1", "b1", 1)]));
        var order = TestSources.Order(vanilla, TestSources.Mod(modPath));
        var store = NewStore();
        var before = store.Get("units_tables", order, None);
        Assert.Equal(2, before.Rows.Count);

        TestSources.WritePack(modPath, Schema, ("units_tables", 2, [U("u3", "Gamma", 30)]));

        var after = store.Get("units_tables", order, None);
        Assert.NotSame(before, after);
        Assert.Equal(3, after.Rows.Count);
    }

    [Fact]
    public void A_deleted_pack_invalidates_the_table()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema,
            ("units_tables", 2, [U("u1", "Alpha", 10), U("u2", "Beta", 20)])));
        var modPath = TestSources.WritePack(tmp.File("mod.pack"), Schema, ("units_tables", 2, [U("u3", "Gamma", 30)]));
        var order = TestSources.Order(vanilla, TestSources.Mod(modPath));
        var store = NewStore();
        var before = store.Get("units_tables", order, None);
        Assert.Equal(3, before.Rows.Count);

        File.Delete(modPath);

        var after = store.Get("units_tables", order, None);
        Assert.NotSame(before, after);
        Assert.Equal(2, after.Rows.Count);
    }

    [Fact]
    public void FindByKey_returns_a_fresh_list_each_call()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema, ("units_tables", 2, [U("u1", "Alpha", 10)])));

        var table = NewStore().Get("units_tables", TestSources.Order(vanilla), None);

        Assert.NotSame(table.FindByKey(["u1"]), table.FindByKey(["u1"]));
    }
    [Fact]
    public void Clear_drops_all_cached_state()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema, ("units_tables", 2, [U("u1", "Alpha", 10)])));
        var store = NewStore();
        var order = TestSources.Order(vanilla);
        var first = store.Get("units_tables", order, None);

        store.Clear();

        Assert.Equal(0, store.CachedTables);
        Assert.Equal(0, store.CachedBytes);
        Assert.NotSame(first, store.Get("units_tables", order, None));
    }

    [Fact]
    public void SignatureOf_equals_the_cache_key_and_changes_with_the_pack()
    {
        using var tmp = new TempDir();
        var vanilla = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema, ("units_tables", 2, [U("u1", "Alpha", 10)])));
        var modPath = TestSources.WritePack(tmp.File("mod.pack"), Schema, ("units_tables", 2, [U("u2", "Beta", 20)]));
        var otherPath = TestSources.WritePack(tmp.File("other.pack"), Schema, ("plain_tables", 1, [["x"]]));
        var order = TestSources.Order(vanilla, TestSources.Mod(modPath), TestSources.Mod(otherPath));
        var store = NewStore();

        // Only listings: nothing is decoded into the table cache.
        var before = store.SignatureOf("units_tables", order, None);
        Assert.Equal(0, store.CachedTables);
        Assert.Equal(before, store.SignatureOf("units_tables", order, None));

        var first = store.Get("units_tables", order, None);
        Assert.Equal(before, store.SignatureOf("units_tables", order, None));
        Assert.Same(first, store.Get("units_tables", order, None));

        // A pack without the table does not matter for it.
        TestSources.WritePack(otherPath, Schema, ("plain_tables", 1, [["x"], ["y"]]));
        Assert.Equal(before, store.SignatureOf("units_tables", order, None));
        Assert.Same(first, store.Get("units_tables", order, None));

        TestSources.WritePack(modPath, Schema, ("units_tables", 2, [U("u2", "Beta", 20), U("u3", "Gamma", 30)]));
        var after = store.SignatureOf("units_tables", order, None);

        Assert.NotEqual(before, after);
        var second = store.Get("units_tables", order, None);
        Assert.NotSame(first, second);
        Assert.Equal(3, second.Rows.Count);
        Assert.Equal(after, store.SignatureOf("units_tables", order, None));
        Assert.Same(second, store.Get("units_tables", order, None));
    }
}
