using TotalWarModTool.Operations;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using static TotalWarModTool.Objects.Tests.TestSchema;

namespace TotalWarModTool.Objects.Tests;

public class ObjectBuilderTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly GameSchema Schema = Build(
        ("building_levels_tables", 1, [Field("level_name", key: true), Field("chain", reference: ("building_chains", "key")),
            Field("create_cost", FieldType.I32), Field("commodity", reference: ("commodities", "key"))]),
        ("building_chains_tables", 1, [Field("key", key: true), Field("name")]),
        ("building_effects_junction_tables", 1, [Field("building", reference: ("building_levels", "level_name")),
            Field("effect", reference: ("effects", "effect")), Field("effect_scope"), Field("value", FieldType.F32)]),
        ("building_upgrades_junction_tables", 1, [Field("from", key: true, reference: ("building_levels", "level_name")),
            Field("to", key: true, reference: ("building_levels", "level_name"))]),
        ("building_level_required_technology_junctions_tables", 1, [Field("building_level_key", key: true, reference: ("building_levels", "level_name")),
            Field("technology_key", key: true, reference: ("technologies", "key"))]),
        ("technologies_tables", 1, [Field("key", key: true), Field("building_level", reference: ("building_levels", "level_name"))]),
        ("effects_tables", 1, [Field("effect", key: true), Field("icon")]),
        ("building_culture_variants_tables", 1, [Field("building", key: true, reference: ("building_levels", "level_name")), Field("culture", key: true),
            Field("name"), Field("short_description", reference: ("building_short_description_texts", "key"))]),
        ("notes_tables", 1, [Field("building", reference: ("building_levels", "create_cost"))]),
        ("pairs_tables", 1, [Field("a", key: true), Field("b", key: true)]),
        ("pair_uses_tables", 1, [Field("pa", reference: ("pairs", "a")), Field("pb", reference: ("pairs", "b"))]));

    private static (string Table, int Version, object?[][] Rows) T(string table, params object?[][] rows) => (table, 1, rows);

    private static object?[] R(params object?[] cells) => cells;

    private static LocRow L(string key, string text) => new(key, text, false);

    private static ObjectBuilder NewBuilder()
    {
        var graph = ReferenceGraph.Build(Schema);
        return new ObjectBuilder(graph, new TableStore(Schema, graph), new LocStore());
    }

    private static TableSource Pack(TempDir tmp, string file, SourceKind kind, LocRow[]? locs, params (string Table, int Version, object?[][] Rows)[] tables)
    {
        var path = TestSources.WritePack(tmp.File(file), Schema, tables, locs is null ? [] : [("x", locs)]);
        return kind == SourceKind.Vanilla ? TestSources.Vanilla(path) : TestSources.Mod(path);
    }

    /// <summary>One vanilla pack that is also the loc pack.</summary>
    private static LoadOrder Single(TempDir tmp, LocRow[]? locs, params (string Table, int Version, object?[][] Rows)[] tables)
    {
        var pack = Pack(tmp, "vanilla.pack", SourceKind.Vanilla, locs, tables);
        return TestSources.OrderWithLoc(pack, pack);
    }

    private static ObjectId Level(string key) => new("building_levels_tables", [key]);

    private sealed class FakeProgress : IProgress<OperationProgress>
    {
        public List<OperationProgress> Reports { get; } = [];

        public void Report(OperationProgress value) => Reports.Add(value);
    }

    private static FieldValue Field_(ObjectRow row, string name) => row.Fields.Single(f => f.Name == name);

    [Fact]
    public void Root_fields_resolve_references_against_the_store()
    {
        using var tmp = new TempDir();
        var order = Single(tmp, [L("building_chains_name_chain1", "Chain One Loc")],
            T("building_levels_tables", R("b1", "chain1", 100, "gold")),
            T("building_chains_tables", R("chain1", "Chain One")));

        var view = NewBuilder().Build(Level("b1"), order, null, None);

        Assert.NotNull(view.Root);
        var chain = Field_(view.Root, "chain");
        Assert.Equal(ReferenceResolution.Found, chain.Resolution);
        Assert.Equal(new ObjectId("building_chains_tables", ["chain1"]), chain.Target);
        Assert.Equal("Chain One Loc", chain.LocText);
        Assert.Equal("chain1", chain.Text);
        Assert.Equal(new TableReference("building_chains", "key"), chain.Reference);
        var commodity = Field_(view.Root, "commodity");
        Assert.Equal(ReferenceResolution.NotFound, commodity.Resolution);
        Assert.Null(commodity.Target);
        Assert.Null(commodity.LocText);
        var cost = Field_(view.Root, "create_cost");
        Assert.Equal(ReferenceResolution.None, cost.Resolution);
        Assert.Equal("100", cost.Text);
        Assert.Equal(FieldType.I32, cost.Type);
        Assert.Equal(Level("b1"), view.Root.RowId);
        Assert.Null(view.Root.OtherEnd);
        Assert.Empty(view.Warnings);
    }

    [Fact]
    public void A_reference_to_a_loc_only_table_shows_the_text()
    {
        using var tmp = new TempDir();
        var tables = new[]
        {
            T("building_levels_tables", R("b1", "", 1, "gold")),
            T("building_culture_variants_tables", R("b1", "cult1", "n", "sd1")),
        };
        var withLoc = Single(tmp, [L("commodities_key_gold", "Gold"), L("building_short_description_texts_key_sd1", "Short")], tables);

        var view = NewBuilder().Build(Level("b1"), withLoc, null, None);

        var commodity = Field_(view.Root!, "commodity");
        Assert.Equal(ReferenceResolution.LocOnly, commodity.Resolution);
        Assert.Equal("Gold", commodity.LocText);
        Assert.Null(commodity.Target);
        Assert.Equal(ReferenceResolution.None, Field_(view.Root!, "chain").Resolution);
        var section = Assert.Single(view.Sections);
        var description = Field_(Assert.Single(section.Rows), "short_description");
        Assert.Equal(ReferenceResolution.Unchecked, description.Resolution);
        Assert.Null(description.LocText);
        Assert.Null(description.Target);

        using var tmp2 = new TempDir();
        var withoutLoc = Single(tmp2, null, tables);
        var plain = NewBuilder().Build(Level("b1"), withoutLoc, null, None);
        Assert.Equal(ReferenceResolution.NotFound, Field_(plain.Root!, "commodity").Resolution);
    }

    [Fact]
    public void Sections_come_from_every_field_that_points_at_the_key()
    {
        using var tmp = new TempDir();
        var order = Single(tmp, null,
            T("building_levels_tables", R("b1", "", 1, ""), R("b2", "", 1, "")),
            T("building_effects_junction_tables", R("b1", "eff1", "province", 5f), R("b1", "eff2", "province", -2f), R("b2", "eff1", "province", 1f)),
            T("technologies_tables", R("t1", "b1")),
            T("notes_tables", R("100")));

        var view = NewBuilder().Build(Level("b1"), order, null, None);

        Assert.Equal([("building_effects_junction_tables", "building"), ("technologies_tables", "building_level")],
            view.Sections.Select(s => (s.SourceTable, s.FieldName)));
        var effects = view.Sections[0];
        // Two reference fields in at most four fields make it a junction for the graph, whatever its name says.
        Assert.True(effects.IsJunction);
        Assert.Equal(2, effects.Rows.Count);
        Assert.Equal(["eff1", "eff2"], effects.Rows.Select(r => Field_(r, "effect").Text));
        Assert.Equal("eff1", effects.Rows[0].OtherEnd!.Text);
        Assert.Equal(ReferenceResolution.NotFound, effects.Rows[0].OtherEnd!.Resolution);
        Assert.Null(effects.Rows[0].RowId);
        // Rows of a section never hit the store: the effect stays unchecked, with a target because effects has one key field.
        var effect = Field_(effects.Rows[0], "effect");
        Assert.Equal(ReferenceResolution.Unchecked, effect.Resolution);
        Assert.Equal(new ObjectId("effects_tables", ["eff1"]), effect.Target);
        var technologies = view.Sections[1];
        Assert.False(technologies.IsJunction);
        Assert.Single(technologies.Rows);
        Assert.Null(technologies.Rows[0].OtherEnd);
        Assert.Equal(new ObjectId("technologies_tables", ["t1"]), technologies.Rows[0].RowId);
        Assert.Equal(new ObjectId("building_levels_tables", ["b1"]), Field_(technologies.Rows[0], "building_level").Target);
    }

    [Fact]
    public void A_reference_to_a_non_key_column_gives_no_section()
    {
        using var tmp = new TempDir();
        var order = Single(tmp, null,
            T("building_levels_tables", R("b1", "", 100, "")),
            T("notes_tables", R("100")));

        var view = NewBuilder().Build(Level("b1"), order, null, None);

        Assert.Empty(view.Sections);
        Assert.Empty(view.Warnings);
    }

    [Fact]
    public void A_self_referencing_junction_gives_one_section_per_field()
    {
        using var tmp = new TempDir();
        var order = Single(tmp, null,
            T("building_levels_tables", R("b0", "", 1, ""), R("b1", "", 1, ""), R("b2", "", 1, "")),
            T("building_upgrades_junction_tables", R("b0", "b1"), R("b1", "b2")));

        var view = NewBuilder().Build(Level("b1"), order, null, None);

        Assert.Equal([("building_upgrades_junction_tables", "from"), ("building_upgrades_junction_tables", "to")],
            view.Sections.Select(s => (s.SourceTable, s.FieldName)));
        Assert.All(view.Sections, s => Assert.True(s.IsJunction));
        var from = Assert.Single(view.Sections[0].Rows);
        Assert.Equal("b2", from.OtherEnd!.Text);
        Assert.Equal(ReferenceResolution.Found, from.OtherEnd.Resolution);
        Assert.Equal(Level("b2"), from.OtherEnd.Target);
        Assert.Equal("to", from.OtherEnd.Name);
        Assert.Equal(new ObjectId("building_upgrades_junction_tables", ["b1", "b2"]), from.RowId);
        var to = Assert.Single(view.Sections[1].Rows);
        Assert.Equal("b0", to.OtherEnd!.Text);
        Assert.Equal("from", to.OtherEnd.Name);
    }

    [Fact]
    public void A_junction_other_end_that_does_not_exist_is_not_found()
    {
        using var tmp = new TempDir();
        var order = Single(tmp, null,
            T("building_levels_tables", R("b1", "", 1, "")),
            T("building_level_required_technology_junctions_tables", R("b1", "t_missing")));

        var view = NewBuilder().Build(Level("b1"), order, null, None);

        var section = Assert.Single(view.Sections);
        Assert.True(section.IsJunction);
        var other = Assert.Single(section.Rows).OtherEnd!;
        Assert.Equal("t_missing", other.Text);
        Assert.Equal(ReferenceResolution.NotFound, other.Resolution);
        Assert.Null(other.Target);
        Assert.Empty(view.Warnings);
    }

    [Fact]
    public void Loc_keys_are_built_from_table_column_and_key()
    {
        using var tmp = new TempDir();
        var order = Single(tmp,
            [
                L("building_levels_level_name_b1", "B1 Name"),
                L("building_chains_name_chain1", "Chain Loc"),
                L("building_culture_variants_name_b1", "Barracks"),
                L("technologies_key_b1", "T"),
                L("building_culture_variants_name_b1x", "Wrong"),
                L("building_effects_junction_effect_b1", "Wrong"),
            ],
            T("building_levels_tables", R("b1", "chain1", 100, "")),
            T("building_chains_tables", R("chain1", "Chain One")),
            T("building_culture_variants_tables", R("b1", "cult1", "n", "sd1")),
            T("technologies_tables", R("t1", "b1")));

        var view = NewBuilder().Build(Level("b1"), order, null, None);

        Assert.Equal(
            ["building_levels_level_name_b1", "building_chains_name_chain1", "building_culture_variants_name_b1", "technologies_key_b1"],
            view.Texts.Select(t => t.Key));
        var first = view.Texts[0];
        Assert.Equal("building_levels", first.Table);
        Assert.Equal("level_name", first.Column);
        Assert.Equal("B1 Name", first.Text);
        Assert.Equal(order.LocPack, first.Source);
        Assert.Equal("Chain Loc", view.Texts[1].Text);
    }

    [Fact]
    public void A_composite_key_root_matches_each_key_column_separately()
    {
        using var tmp = new TempDir();
        var order = Single(tmp, null,
            T("pairs_tables", R("x", "y")),
            T("pair_uses_tables", R("x", "q"), R("z", "y")));
        var id = new ObjectId("pairs_tables", ["x", "y"]);

        var view = NewBuilder().Build(id, order, null, None);

        Assert.Equal("xy", id.JoinedKey);
        Assert.NotNull(view.Root);
        Assert.Equal([("pair_uses_tables", "pa"), ("pair_uses_tables", "pb")], view.Sections.Select(s => (s.SourceTable, s.FieldName)));
        var pa = Assert.Single(view.Sections[0].Rows);
        Assert.Equal(["x", "q"], pa.Fields.Select(f => f.Text));
        var pb = Assert.Single(view.Sections[1].Rows);
        Assert.Equal(["z", "y"], pb.Fields.Select(f => f.Text));
    }

    [Fact]
    public void Provenance_and_overrides_reach_the_rows()
    {
        using var tmp = new TempDir();
        var vanilla = Pack(tmp, "vanilla.pack", SourceKind.Vanilla, null,
            T("building_levels_tables", R("b1", "", 1, "")),
            T("building_effects_junction_tables", R("b1", "eff1", "p", 5f)));
        var mod = Pack(tmp, "mod.pack", SourceKind.Mod, null,
            T("building_levels_tables", R("b1", "", 99, "")),
            T("building_effects_junction_tables", R("b1", "eff3", "p", 1f)));

        var view = NewBuilder().Build(Level("b1"), TestSources.Order(vanilla, mod), null, None);

        Assert.Equal("mod.pack", view.Root!.Provenance.Source.Name);
        Assert.Equal([vanilla], view.Root.Provenance.Overridden);
        Assert.Equal("99", Field_(view.Root, "create_cost").Text);
        var section = Assert.Single(view.Sections);
        Assert.Equal([vanilla, mod], section.Rows.Select(r => r.Provenance.Source));
        Assert.Equal(["eff1", "eff3"], section.Rows.Select(r => Field_(r, "effect").Text));
    }

    [Fact]
    public void A_missing_root_still_builds_the_sections_with_a_warning()
    {
        using var tmp = new TempDir();
        var order = Single(tmp, null,
            T("building_levels_tables", R("b1", "", 1, "")),
            T("building_effects_junction_tables", R("b7", "eff1", "p", 5f)));

        var view = NewBuilder().Build(Level("b7"), order, null, None);

        Assert.Null(view.Root);
        var warning = Assert.Single(view.Warnings);
        Assert.Equal(ObjectWarningKind.RootNotFound, warning.Kind);
        var section = Assert.Single(view.Sections);
        Assert.Equal("building_effects_junction_tables", section.SourceTable);
        Assert.Single(section.Rows);
    }

    [Fact]
    public void An_unknown_root_table_gives_only_a_warning()
    {
        using var tmp = new TempDir();
        var order = Single(tmp, null, T("building_levels_tables", R("b1", "", 1, "")));

        var view = NewBuilder().Build(new ObjectId("ghost_tables", ["x"]), order, null, None);

        Assert.Null(view.Root);
        Assert.Empty(view.Sections);
        Assert.Empty(view.Texts);
        var warning = Assert.Single(view.Warnings);
        Assert.Equal(ObjectWarningKind.TableNotInSchema, warning.Kind);
        Assert.Equal("ghost_tables", warning.Name);
    }

    [Fact]
    public void Source_problems_become_warnings()
    {
        using var tmp = new TempDir();
        var vanilla = Pack(tmp, "vanilla.pack", SourceKind.Vanilla, null,
            T("building_levels_tables", R("b1", "", 1, "")),
            T("technologies_tables", R("t1", "b1")));
        File.WriteAllBytes(tmp.File("broken.pack"), new byte[10]);
        var broken = TestSources.Mod(tmp.File("broken.pack"));
        var order = new LoadOrder([vanilla, broken], null,
            [new LoadOrderWarning(LoadOrderProblem.ModPackMissing, "gone.pack", "The pack file does not exist")]);

        var view = NewBuilder().Build(Level("b1"), order, null, None);

        // The root table, the technologies section and the other end of nothing: at least two tables report the broken pack.
        var problem = Assert.Single(view.Warnings, w => w.Kind == ObjectWarningKind.SourceProblem);
        Assert.Equal("broken.pack", problem.Name);
        var loadOrder = Assert.Single(view.Warnings, w => w.Kind == ObjectWarningKind.LoadOrder);
        Assert.Equal("gone.pack", loadOrder.Name);
        Assert.Contains("ModPackMissing", loadOrder.Detail);
        Assert.Equal(2, view.Warnings.Count);
        Assert.Same(order, view.Order);
    }

    [Fact]
    public void Progress_counts_tables_and_cancellation_stops()
    {
        using var tmp = new TempDir();
        var order = Single(tmp, null,
            T("building_levels_tables", R("b1", "", 1, "")),
            T("technologies_tables", R("t1", "b1")),
            T("building_effects_junction_tables", R("b1", "eff1", "p", 5f)));
        var progress = new FakeProgress();

        NewBuilder().Build(Level("b1"), order, progress, None);

        Assert.NotEmpty(progress.Reports);
        var last = progress.Reports[^1];
        Assert.NotNull(last.Total);
        Assert.Equal(last.Total, last.Done);
        Assert.All(progress.Reports, r => Assert.Equal(last.Total, r.Total));
        Assert.Equal(progress.Reports.Select(r => r.Done).Order(), progress.Reports.Select(r => r.Done));
        Assert.Contains(progress.Reports, r => r.Item == "building_levels_tables");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => NewBuilder().Build(Level("b1"), order, null, cts.Token));
    }

    [Fact]
    public void TryDisplayName_uses_the_column_order()
    {
        using var tmp = new TempDir();
        var tables = new[] { T("technologies_tables", R("t1", "")) };
        var id = new ObjectId("technologies_tables", ["t1"]);
        var withLoc = Single(tmp, [L("technologies_name_t1", "Other"), L("technologies_onscreen_name_t1", "Tech")], tables);
        using var tmp2 = new TempDir();
        var withoutLoc = Single(tmp2, null, tables);
        var builder = NewBuilder();

        Assert.Equal("Tech", builder.TryDisplayName(id, withLoc, None));
        Assert.Null(builder.TryDisplayName(id, withoutLoc, None));
    }

    [Fact]
    public void ObjectId_compares_the_table_ignoring_case_and_the_values_ordinal()
    {
        var a = new ObjectId("Pairs_Tables", ["x", "y"]);
        var b = new ObjectId("pairs_tables", ["x", "y"]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new ObjectId("pairs_tables", ["X", "y"]));
        Assert.NotEqual(a, new ObjectId("pairs_tables", ["x"]));
        Assert.Equal("Pairs_Tables:x|y", a.ToString());
        Assert.Equal("xy", a.JoinedKey);
    }
}
