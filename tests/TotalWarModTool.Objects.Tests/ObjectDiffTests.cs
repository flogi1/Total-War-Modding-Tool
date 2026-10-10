using TotalWarModTool.Schema;
using static TotalWarModTool.Objects.Tests.BlockFixture;

namespace TotalWarModTool.Objects.Tests;

public class ObjectDiffTests
{
    private static ObjectChanges Diff(ObjectView view, string text, ReferenceExists? exists = null) =>
        ObjectDiff.Compute(view, ObjectTextParser.Parse(text, view, Category, Graph), Category, Graph, exists);

    private static string[] Errors(ObjectChanges changes) =>
        changes.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Kind + "@" + d.Line).ToArray();

    private static string Rendered(ObjectView view) => ObjectText.RenderBlock(view, Category).Text;

    private static string WithRoot(params string[] lines) => Lines([.. HeadAndRoot(), .. lines]);

    private static ObjectView WithEffects(params ObjectRow[] rows) => View(RootRow(), [Section("effects_tables", "building", false, rows)]);

    [Fact]
    public void An_unchanged_text_gives_no_changes()
    {
        var view = Full();

        var changes = Diff(view, Rendered(view));

        Assert.True(changes.IsEmpty);
        Assert.Empty(changes.Diagnostics);
    }

    [Fact]
    public void A_changed_root_field_is_a_set_with_the_full_row()
    {
        var view = Full();

        var changes = Diff(view, Rendered(view).Replace("create_cost = 100", "create_cost = 90", StringComparison.Ordinal));

        var row = Assert.Single(changes.Rows);
        Assert.Equal(RowChangeKind.Set, row.Kind);
        Assert.Equal(Root, row.Table);
        Assert.Null(row.RootField);
        Assert.Null(row.RootValue);
        Assert.Equal(["create_cost"], row.ChangedFields);
        Assert.Equal(["b1", "chain1", "90", ""], row.Cells);
        Assert.Same(view.Root!.Provenance, row.Provenance);
        Assert.Empty(changes.Texts);
        Assert.Empty(changes.Diagnostics);
    }

    [Fact]
    public void A_missing_root_field_keeps_its_value()
    {
        var view = Full();
        var text = string.Join('\n', Rendered(view).Split('\n').Where(l => l != "  note = "));

        var changes = Diff(view, text);

        Assert.True(changes.IsEmpty);
        var warning = Assert.Single(changes.Diagnostics);
        Assert.Equal(DiagnosticKind.RootFieldMissing, warning.Kind);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.False(changes.HasErrors);
    }

    [Fact]
    public void A_value_that_ends_with_a_space_is_unchanged_until_it_is_edited()
    {
        // 536 real values end with a space; the block trims them, so the space alone is no change.
        var view = View(RootRow(note: "abc "));

        var unchanged = Diff(view, Rendered(view));
        var edited = Diff(view, Rendered(view).Replace("note = abc ", "note = abd", StringComparison.Ordinal));

        Assert.True(unchanged.IsEmpty);
        Assert.Empty(unchanged.Diagnostics);
        var row = Assert.Single(edited.Rows);
        Assert.Equal(["note"], row.ChangedFields);
        Assert.Equal("abd", row.Cells[3]);
    }

    [Fact]
    public void Keys_and_keyless_rows_with_a_trailing_space_still_match()
    {
        var keyed = new ObjectRow([Val("building", "b1", refTable: Root, refColumn: "level_name"), Val("effect", "e1 ", key: true), Val("scope", "x "),
            Val("value", "1", FieldType.F32)], From(Vanilla), Id("effects_tables", "e1 "), null);
        var keyless = new ObjectRow([Val("building", "b1", refTable: Root, refColumn: "level_name"), Val("a", "x "), Val("b", "y ")], From(Vanilla), null, null);
        var view = View(RootRow(), [Section("effects_tables", "building", false, keyed), Section("notes_tables", "building", false, keyless)]);

        var changes = Diff(view, Rendered(view));

        Assert.True(changes.IsEmpty);
        Assert.Empty(changes.Diagnostics);
    }

    [Fact]
    public void A_changed_root_key_is_an_error()
    {
        var view = Full();

        var changes = Diff(view, Rendered(view).Replace("level_name = b1", "level_name = b2", StringComparison.Ordinal));

        Assert.Equal(["RootKeyChanged@2"], Errors(changes));
        Assert.Empty(changes.Rows);
    }

    [Fact]
    public void A_baseline_without_root_makes_the_root_an_insert()
    {
        var view = View(null);

        var changes = Diff(view, Lines("building_levels b1", "  level_name = b1", "  chain = chain1"));

        var row = Assert.Single(changes.Rows);
        Assert.Equal(RowChangeKind.Insert, row.Kind);
        Assert.Equal(Root, row.Table);
        Assert.Equal(["b1", "chain1", "0", ""], row.Cells);
        Assert.Null(row.Provenance);
        Assert.DoesNotContain(changes.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void A_baseline_without_root_takes_a_missing_key_from_the_id_and_rejects_a_different_one()
    {
        var view = View(null);

        var filled = Diff(view, Lines("building_levels b1", "  chain = chain1"));
        var other = Diff(view, Lines("building_levels b1", "  level_name = b2"));

        Assert.Equal("b1", Assert.Single(filled.Rows).Cells[0]);
        Assert.Equal(["RootKeyChanged@2"], Errors(other));
    }

    [Fact]
    public void A_new_list_row_is_an_insert_with_the_root_value()
    {
        var view = WithEffects(EffectRow("e1"));

        var changes = Diff(view, WithRoot("", "effects", "  effect = e1", "    scope = province", "    value = 10", "", "  effect = e2", "    value = 3"));

        var row = Assert.Single(changes.Rows);
        Assert.Equal(RowChangeKind.Insert, row.Kind);
        Assert.Equal(("effects_tables", "building", "b1"), (row.Table, row.RootField, row.RootValue));
        Assert.Equal(["b1", "e2", "", "3"], row.Cells);
        Assert.Equal(12, row.Line);
        Assert.Null(row.Provenance);
        Assert.Empty(changes.Diagnostics);
    }

    [Fact]
    public void A_removed_row_is_a_delete_with_its_provenance()
    {
        var second = From(Mod, Vanilla);
        var view = WithEffects(EffectRow("e1"), EffectRow("e2", "city", "4", second));

        var changes = Diff(view, WithRoot("", "effects", "  effect = e1", "    scope = province", "    value = 10"));

        var row = Assert.Single(changes.Rows);
        Assert.Equal(RowChangeKind.Delete, row.Kind);
        Assert.Equal(["b1", "e2", "city", "4"], row.Cells);
        Assert.Same(second, row.Provenance);
        Assert.Equal(("building", "b1"), (row.RootField, row.RootValue));
    }

    [Fact]
    public void A_changed_key_cell_is_a_delete_and_an_insert()
    {
        var view = WithEffects(EffectRow("e1"));

        var changes = Diff(view, WithRoot("", "effects", "  effect = e9", "    scope = province", "    value = 10"));

        Assert.Equal([RowChangeKind.Insert, RowChangeKind.Delete], changes.Rows.Select(r => r.Kind).ToArray());
        Assert.Equal(["b1", "e9", "province", "10"], changes.Rows[0].Cells);
        Assert.Equal("e1", changes.Rows[1].Cells[1]);
        Assert.Empty(changes.Diagnostics);
    }

    [Fact]
    public void A_changed_field_of_a_row_is_a_set_that_keeps_the_other_fields()
    {
        var provenance = From(Mod, Vanilla);
        var view = WithEffects(EffectRow("e1", provenance: provenance));

        var changes = Diff(view, WithRoot("", "effects", "  effect = e1", "    value = 12"));

        var row = Assert.Single(changes.Rows);
        Assert.Equal(RowChangeKind.Set, row.Kind);
        Assert.Equal(["value"], row.ChangedFields);
        Assert.Equal(["b1", "e1", "province", "12"], row.Cells);
        Assert.Same(provenance, row.Provenance);
        Assert.Equal(8, row.Line);
    }

    [Fact]
    public void Rows_without_key_are_matched_by_their_fields()
    {
        var view = View(RootRow(), [Section("units_tables", "building", true, UnitRow("u1"), UnitRow("u3"))]);

        var changes = Diff(view, WithRoot("", "units", "  allows u1", "    xp = 2", "  allows u2"));

        Assert.Equal([RowChangeKind.Insert, RowChangeKind.Delete], changes.Rows.Select(r => r.Kind).ToArray());
        Assert.Equal(["b1", "u2", "0"], changes.Rows[0].Cells);
        Assert.Equal(["b1", "u3", "2"], changes.Rows[1].Cells);
        Assert.Empty(changes.Diagnostics);
    }

    [Fact]
    public void A_keyless_row_with_a_changed_field_is_a_delete_and_an_insert()
    {
        var view = View(RootRow(), [Section("units_tables", "building", true, UnitRow("u1"))]);

        var changes = Diff(view, WithRoot("", "units", "  allows u1", "    xp = 5"));

        Assert.Equal([RowChangeKind.Insert, RowChangeKind.Delete], changes.Rows.Select(r => r.Kind).ToArray());
        Assert.Equal(["b1", "u1", "5"], changes.Rows[0].Cells);
    }

    private static RowProvenance Duplicate() => new(Vanilla, [], false, true);

    [Fact]
    public void Duplicate_keys_of_the_baseline_are_matched_by_position()
    {
        var first = Duplicate();
        var second = Duplicate();
        var view = WithEffects(EffectRow("e1", value: "1", provenance: first), EffectRow("e1", value: "2", provenance: second));

        var changes = Diff(view, WithRoot("", "effects", "  effect = e1", "    scope = province", "    value = 1", "", "  effect = e1", "    scope = province",
            "    value = 5"));

        var row = Assert.Single(changes.Rows);
        Assert.Equal(RowChangeKind.Set, row.Kind);
        Assert.Same(second, row.Provenance);
        Assert.Equal(["value"], row.ChangedFields);
        Assert.Equal(["b1", "e1", "province", "5"], row.Cells);
        Assert.Empty(changes.Diagnostics);
    }

    [Fact]
    public void One_more_duplicate_than_the_baseline_is_an_error()
    {
        var view = WithEffects(EffectRow("e1", value: "1", provenance: Duplicate()), EffectRow("e1", value: "2", provenance: Duplicate()));

        var changes = Diff(view, WithRoot("", "effects", "  effect = e1", "    value = 1", "  effect = e1", "    value = 2", "  effect = e1", "    value = 3"));

        Assert.Equal(["DuplicateCountExceeded@12"], Errors(changes));
    }

    [Fact]
    public void A_repeated_key_without_baseline_duplicates_is_an_error()
    {
        var present = WithEffects(EffectRow("e2"));
        var absent = WithEffects();

        var inBaseline = Diff(present, WithRoot("", "effects", "  effect = e2", "    scope = province", "    value = 10", "  effect = e2"));
        var notInBaseline = Diff(absent, WithRoot("", "effects", "  effect = e2", "  effect = e2"));

        Assert.Equal(["KeyRepeated@11"], Errors(inBaseline));
        Assert.Equal(["KeyRepeated@9"], Errors(notInBaseline));
    }

    [Fact]
    public void A_key_field_missing_in_a_row_is_an_error()
    {
        var view = WithEffects();

        var changes = Diff(view, WithRoot("", "effects", "  scope = x  value = 1"));

        Assert.Equal(["UnknownLineForm@8"], Errors(changes));
        Assert.Empty(changes.Rows);
    }

    [Fact]
    public void A_whole_section_missing_from_the_text_deletes_its_rows()
    {
        var hidden = new ObjectRow([Val("building", "b1", refTable: Root, refColumn: "level_name"), Val("key", "k1", key: true)], From(Vanilla),
            Id("cai_x_tables", "k1"), null);
        var baseline = Full();
        var view = baseline with { Sections = [.. baseline.Sections, Section("cai_x_tables", "building", false, hidden)] };

        var changes = Diff(view, WithRoot());

        Assert.All(changes.Rows, r => Assert.Equal(RowChangeKind.Delete, r.Kind));
        Assert.Equal(["effects_tables", "units_tables", "upgrades_tables"], changes.Rows.Select(r => r.Table).ToArray());
        Assert.Equal(["building", "building", "from"], changes.Rows.Select(r => r.RootField!).ToArray());
        Assert.Equal(["b1", "b1", "b1"], changes.Rows.Select(r => r.RootValue!).ToArray());
        Assert.Empty(changes.Texts);
    }

    [Fact]
    public void A_section_the_baseline_lacks_inserts_every_row()
    {
        var view = View(RootRow());

        var changes = Diff(view, WithRoot("", "effects", "  effect = e2", "    value = 3", "  effect = e3"));

        Assert.All(changes.Rows, r => Assert.Equal(RowChangeKind.Insert, r.Kind));
        Assert.Equal(["e2", "e3"], changes.Rows.Select(r => r.Cells[1]).ToArray());
        Assert.Empty(changes.Diagnostics);
    }

    [Fact]
    public void Values_are_compared_after_normalisation()
    {
        var view = WithEffects(EffectRow("e1"));

        var same = Diff(view, WithRoot("", "effects", "  effect = e1", "    value = 10.0"));
        var bad = Diff(view, WithRoot("", "effects", "  effect = e1", "    value = abc"));

        Assert.True(same.IsEmpty);
        Assert.Empty(same.Diagnostics);
        Assert.Equal(["InvalidValue@9"], Errors(bad));
        var row = Assert.Single(bad.Rows);
        Assert.Equal(["b1", "e1", "province", "abc"], row.Cells);
        Assert.Equal(["value"], row.ChangedFields);
    }

    [Fact]
    public void A_changed_root_text_is_a_set_and_a_new_one_an_insert()
    {
        var view = View(RootRow(), texts: [new LocEntry("building_levels", "onscreen_name", "building_levels_onscreen_name_b1", "Barracks", Mod, Id(Root, "b1"))]);

        var changes = Diff(view, WithRoot("  onscreen_name = \"Fort\"", "  short_name = \"F\""));

        Assert.Empty(changes.Rows);
        Assert.Equal(2, changes.Texts.Count);
        var set = changes.Texts[0];
        Assert.Equal((TextChangeKind.Set, "building_levels_onscreen_name_b1", "Fort"), (set.Kind, set.Key, set.Text));
        Assert.Equal(Mod, set.Source);
        var insert = changes.Texts[1];
        Assert.Equal((TextChangeKind.Insert, "building_levels_short_name_b1", "F"), (insert.Kind, insert.Key, insert.Text));
        Assert.Null(insert.Source);
    }

    [Fact]
    public void An_unchanged_text_and_a_missing_text_give_no_change()
    {
        var view = View(RootRow(), texts: [new LocEntry("building_levels", "onscreen_name", "building_levels_onscreen_name_b1", "Barracks", Mod, Id(Root, "b1"))]);

        var same = Diff(view, WithRoot("  onscreen_name = \"Barracks\""));
        var missing = Diff(view, WithRoot());

        Assert.True(same.IsEmpty);
        Assert.True(missing.IsEmpty);
    }

    [Fact]
    public void A_row_text_uses_the_joined_key_of_the_row()
    {
        var variantId = new ObjectId("variants_tables", ["c1", "v1"]);
        var row = new ObjectRow([Val("building", "b1", refTable: Root, refColumn: "level_name"), Val("culture", "c1", key: true), Val("variant", "v1", key: true),
            Val("icon", "i")], From(Vanilla), variantId, null);
        var view = View(RootRow(), [Section("variants_tables", "building", false, row)],
            [new LocEntry("variants", "name", "variants_name_c1v1", "Old", Mod, variantId)]);

        var changes = Diff(view, WithRoot("", "variants", "  culture = c1  variant = v1", "    icon = i", "    name = \"New\"", "",
            "  culture = c2  variant = v2", "    name = \"Other\""));

        Assert.Equal(["variants_name_c1v1", "variants_name_c2v2"], changes.Texts.Select(t => t.Key).ToArray());
        Assert.Equal([TextChangeKind.Set, TextChangeKind.Insert], changes.Texts.Select(t => t.Kind).ToArray());
        Assert.Equal(Mod, changes.Texts[0].Source);
        Assert.Equal(RowChangeKind.Insert, Assert.Single(changes.Rows).Kind);
    }

    [Fact]
    public void A_text_on_a_keyless_row_is_an_error()
    {
        var view = View(RootRow(), [Section("units_tables", "building", true, UnitRow("u1"))]);

        var changes = Diff(view, WithRoot("", "units", "  allows u1", "    xp = 2", "    note = \"hello\""));

        Assert.Equal(["TextWithoutOwnerKey@10"], Errors(changes));
        Assert.Empty(changes.Texts);
    }

    [Fact]
    public void A_reference_that_does_not_exist_is_a_warning()
    {
        var view = Full();
        var asked = new List<(string Table, string Column, string Value)>();

        var changes = Diff(view, Rendered(view).Replace("chain = chain1", "chain = zzz", StringComparison.Ordinal), (table, column, value) =>
        {
            asked.Add((table, column, value));
            return false;
        });

        var warning = Assert.Single(changes.Diagnostics);
        Assert.Equal((DiagnosticKind.ReferenceNotFound, DiagnosticSeverity.Warning, 3), (warning.Kind, warning.Severity, warning.Line));
        Assert.Equal([("building_chains_tables", "key", "zzz")], asked);
        Assert.False(changes.HasErrors);
        Assert.Equal(["chain"], Assert.Single(changes.Rows).ChangedFields);
    }

    [Fact]
    public void A_new_row_asks_for_every_reference_it_has()
    {
        var view = View(RootRow());
        var asked = new List<string>();

        var changes = Diff(view, WithRoot("", "units", "  allows u9"), (_, _, value) =>
        {
            asked.Add(value);
            return true;
        });

        Assert.Equal(["u9"], asked);
        Assert.Empty(changes.Diagnostics);
    }

    [Fact]
    public void Without_a_delegate_references_are_not_checked()
    {
        var view = Full();

        var changes = Diff(view, Rendered(view).Replace("chain = chain1", "chain = zzz", StringComparison.Ordinal));

        Assert.Empty(changes.Diagnostics);
        Assert.Single(changes.Rows);
    }

    [Fact]
    public void Parser_diagnostics_are_carried_over_first()
    {
        var view = Full();

        var changes = Diff(view, Rendered(view).Replace("create_cost = 100", "create_cost = abc", StringComparison.Ordinal) + "\nstray");

        Assert.Equal(["UnknownSection@" + (Rendered(view).Split('\n').Length + 1), "InvalidValue@4"], Errors(changes));
        Assert.True(changes.HasErrors);
    }

    [Fact]
    public void Changes_are_ordered_root_then_sections_in_text_order()
    {
        var view = View(RootRow());

        var changes = Diff(view, string.Join('\n', HeadAndRoot()[..3].Concat(["  create_cost = 7", "  note = "]).Concat(
            ["", "variants", "  culture = c1  variant = v1", "", "effects", "  effect = e2"])));

        Assert.Equal([Root, "variants_tables", "effects_tables"], changes.Rows.Select(r => r.Table).ToArray());
        Assert.Equal([RowChangeKind.Set, RowChangeKind.Insert, RowChangeKind.Insert], changes.Rows.Select(r => r.Kind).ToArray());
    }

    [Fact]
    public void A_section_hidden_by_the_category_is_never_deleted_or_inserted_through_the_baseline()
    {
        var hidden = new ObjectRow([Val("building", "b1", refTable: Root, refColumn: "level_name"), Val("key", "k1", key: true)], From(Vanilla),
            Id("cai_x_tables", "k1"), null);
        var view = View(RootRow(), [Section("cai_x_tables", "building", false, hidden)]);

        var changes = Diff(view, WithRoot());

        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void The_unsupported_type_keeps_the_text_as_it_is()
    {
        var graph = ReferenceGraph.Build(TestSchema.Build(("odd_tables", 1, [TestSchema.Field("key", key: true), TestSchema.Field("weird", FieldType.Unsupported)])));
        var id = new ObjectId("odd_tables", ["k"]);
        var root = new ObjectRow([Val("key", "k", key: true), Val("weird", "w", FieldType.Unsupported)], From(Vanilla), id, null);
        var view = new ObjectView(id, root, [], [], [], Order, ["odd_tables"]);

        var parsed = ObjectTextParser.Parse(ObjectText.RenderBlock(view, null).Text, view, null, graph);
        var changes = ObjectDiff.Compute(view, parsed, null, graph, null);

        Assert.True(changes.IsEmpty);
        Assert.Empty(changes.Diagnostics);
    }
}
