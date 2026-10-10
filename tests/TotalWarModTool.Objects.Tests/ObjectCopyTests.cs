using TotalWarModTool.Schema;
using static TotalWarModTool.Objects.Tests.BlockFixture;

namespace TotalWarModTool.Objects.Tests;

public class ObjectCopyTests
{
    private static ObjectRow BonusRow(string building, string culture, string other, string amount = "5", string note = "") => new(
        [
            Val("building", building, key: true, refTable: Root, refColumn: "level_name"),
            Val("culture", culture, key: true),
            Val("other", other, key: true),
            Val("amount", amount, FieldType.I32),
            Val("note", note),
        ],
        From(Vanilla), new ObjectId("bonus_tables", [building, culture, other]), null);

    private static ObjectView Source(IReadOnlyList<ObjectSection> sections, IReadOnlyList<LocEntry>? texts = null) => View(RootRow(), sections, texts);

    private static CopyResult Rename(ObjectView source, string newKey = "b9", Func<ObjectId, ObjectView>? build = null,
        CategoryDefinition? category = null) =>
        ObjectCopy.Rename(source, Id(Root, newKey), build ?? (id => throw new InvalidOperationException("unexpected build of " + id)), _ => category, Graph,
            NoIntegerKey);

    private static string NoIntegerKey(string table, string field) => throw new InvalidOperationException("unexpected integer key for " + table + "." + field);

    private static (string Name, string Text)[] Fields(ParsedRow row) => row.Fields.Select(f => (f.Name, f.Text)).ToArray();

    // ---- a section marked "copy": "owned": rows with an integer key of their own ----

    private static readonly ReferenceGraph OwnedGraph = ReferenceGraph.Build(TestSchema.Build(
        (Root, 1, [TestSchema.Field("level_name", key: true)]),
        ("units_tables", 1,
        [
            TestSchema.Field("key", FieldType.I32, key: true),
            TestSchema.Field("building", reference: (Root, "level_name")),
            TestSchema.Field("unit"),
        ])));

    private static ObjectRow OwnedUnitRow(string key, string unit) => new(
        [Val("key", key, FieldType.I32, key: true), Val("building", "b1", refTable: Root, refColumn: "level_name"), Val("unit", unit)],
        From(Vanilla), Id("units_tables", key), null);

    private static ObjectView OwnedSource() => Source([Section("units_tables", "building", false, OwnedUnitRow("7", "u1"), OwnedUnitRow("8", "u2"))]);

    [Fact]
    public void A_section_marked_copy_owned_is_copied_with_new_integer_keys()
    {
        var asked = new List<(string Table, string Field)>();
        var values = new Queue<string>(["1000000001", "1000000002"]);
        var category = new CategoryDefinition(Root, "cat", [new SectionRule("units_tables", null, null, SectionMode.List, null, false, CopyOwned: true)]);

        var result = ObjectCopy.Rename(OwnedSource(), Id(Root, "b9"), _ => throw new InvalidOperationException("unexpected build"), _ => category, OwnedGraph,
            (table, field) =>
            {
                asked.Add((table, field));
                return values.Dequeue();
            });

        var section = Assert.Single(Assert.Single(result.Objects).Sections);
        Assert.Equal("units_tables", section.SourceTable);
        Assert.Equal(2, section.Rows.Count);
        Assert.Equal([("key", "1000000001"), ("unit", "u1")], Fields(section.Rows[0]));
        Assert.Equal([("key", "1000000002"), ("unit", "u2")], Fields(section.Rows[1]));
        Assert.All(section.Rows, row => Assert.DoesNotContain(row.Fields, f => f.Name == "building"));
        Assert.Equal([("units_tables", "key"), ("units_tables", "key")], asked);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void An_unmarked_section_with_its_own_integer_key_stays_a_note()
    {
        var result = ObjectCopy.Rename(OwnedSource(), Id(Root, "b9"), _ => throw new InvalidOperationException("unexpected build"), _ => null, OwnedGraph, NoIntegerKey);

        Assert.Empty(Assert.Single(result.Objects).Sections);
        Assert.Equal(new CopyNote(CopyNoteKind.ForeignKeyRows, "units_tables", 2), Assert.Single(result.Notes));
    }

    // ---- traits: a root with prefix rows ----

    private static ObjectRow LevelRow(string key) => new(
        [Val("key", key, key: true), Val("trait", "t1", refTable: "traits_tables"), Val("rank", "1", FieldType.I32)],
        From(Vanilla), Id("trait_levels_tables", key), null);

    private static ObjectView TraitView(params string[] levels) => new(Id("traits_tables", "t1"),
        new ObjectRow([Val("key", "t1", key: true)], From(Vanilla), Id("traits_tables", "t1"), null),
        [],
        [Section("trait_levels_tables", "trait", false, levels.Select(LevelRow).ToArray())],
        [], Order, ["traits_tables"]);

    private static ObjectView StageView(string key, bool withNote = false)
    {
        var effect = new ObjectRow([Val("level", key, key: true, refTable: "trait_levels_tables"), Val("effect", "eff_" + key, key: true)], From(Vanilla),
            new ObjectId("level_effects_tables", [key, "eff_" + key]), null);
        var note = new ObjectRow([Val("key", key + "_a", key: true), Val("level", key, refTable: "trait_levels_tables")], From(Vanilla),
            Id("level_notes_tables", key + "_a"), null);
        List<ObjectSection> sections = [Section("level_effects_tables", "level", false, effect)];
        if (withNote)
        {
            sections.Add(Section("level_notes_tables", "level", false, note));
        }

        return new ObjectView(Id("trait_levels_tables", key), LevelRow(key), [], sections, [], Order, ["trait_levels_tables"]);
    }

    [Fact]
    public void The_root_gets_the_new_key_and_its_texts()
    {
        var source = Source([], [new LocEntry("building_levels", "onscreen_name", "building_levels_onscreen_name_b1", "Barracks", Mod, Id(Root, "b1")),
            new LocEntry("building_chains", "name", "building_chains_name_chain1", "Chain One", Vanilla, Id("building_chains_tables", "chain1"))]);

        var result = Rename(source);

        var copy = Assert.Single(result.Objects);
        Assert.Equal(Id(Root, "b9"), copy.Id);
        Assert.Equal([("level_name", "b9"), ("chain", "chain1"), ("create_cost", "100"), ("note", "")], copy.Root.Select(f => (f.Name, f.Text)).ToArray());
        var text = Assert.Single(copy.Texts);
        Assert.Equal(("onscreen_name", "Barracks"), (text.Column, text.Text));
        Assert.Empty(copy.Diagnostics);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Junction_rows_and_rows_keyed_by_the_root_are_copied_without_the_root_field()
    {
        var bonus = BonusRow("b1", "emp", "x");
        var source = Source(
            [
                Section("bonus_tables", "building", false, bonus),
                Section("upgrades_tables", "from", true, UpgradeRow("b1", "t1")),
                Section("units_tables", "building", true, UnitRow("u1")),
                Section("notes_tables", "building", false, new ObjectRow([Val("building", "b1", refTable: Root, refColumn: "level_name"), Val("a", "x"), Val("b", "")],
                    From(Vanilla), null, null)),
            ],
            [new LocEntry("bonus", "label", "bonus_label_b1empx", "Label", Vanilla, bonus.RowId!)]);

        var copy = Assert.Single(Rename(source).Objects);

        Assert.Equal(["bonus_tables", "upgrades_tables", "units_tables", "notes_tables"], copy.Sections.Select(s => s.SourceTable).ToArray());
        Assert.Equal(["building", "from", "building", "building"], copy.Sections.Select(s => s.FieldName).ToArray());
        var bonusRow = Assert.Single(copy.Sections[0].Rows);
        Assert.Equal([("culture", "emp"), ("other", "x"), ("amount", "5"), ("note", "")], Fields(bonusRow));
        Assert.Equal(("label", "Label"), (bonusRow.Texts[0].Column, bonusRow.Texts[0].Text));
        Assert.Equal([("to", "t1")], Fields(Assert.Single(copy.Sections[1].Rows)));
        Assert.Equal([("unit", "u1"), ("xp", "2")], Fields(Assert.Single(copy.Sections[2].Rows)));
        Assert.Equal([("a", "x"), ("b", "")], Fields(Assert.Single(copy.Sections[3].Rows)));
    }

    [Fact]
    public void A_composite_key_containing_the_root_key_is_replaced()
    {
        var source = Source([Section("bonus_tables", "building", false, BonusRow("b1", "emp", "b1"), BonusRow("b1", "b1", "b1_x"), BonusRow("b1", "emp", "other"))]);

        var rows = Assert.Single(Assert.Single(Rename(source).Objects).Sections).Rows;

        Assert.Equal(["emp", "b9"], [rows[0].Fields[0].Text, rows[0].Fields[1].Text]);
        Assert.Equal(["b9", "b1_x"], [rows[1].Fields[0].Text, rows[1].Fields[1].Text]);
        Assert.Equal(["emp", "other"], [rows[2].Fields[0].Text, rows[2].Fields[1].Text]);
    }

    [Fact]
    public void Foreign_key_rows_are_reported_not_copied()
    {
        var tech = (string key) => new ObjectRow([Val("key", key, key: true), Val("building", "b1", refTable: Root, refColumn: "level_name")], From(Vanilla),
            Id("technologies_tables", key), null);
        var source = Source([Section("technologies_tables", "building", false, tech("tech_a"), tech("tech_b"), tech("tech_c"))]);

        var result = Rename(source);

        Assert.Empty(Assert.Single(result.Objects).Sections);
        Assert.Equal(new CopyNote(CopyNoteKind.ForeignKeyRows, "technologies_tables", 3), Assert.Single(result.Notes));
    }

    [Fact]
    public void Hidden_sections_are_reported_not_copied()
    {
        var hidden = (string key) => new ObjectRow([Val("key", key, key: true), Val("building", "b1", refTable: Root, refColumn: "level_name")], From(Vanilla),
            Id("cai_x_tables", key), null);
        var source = Source([Section("cai_x_tables", "building", false, hidden("k1"), hidden("k2")), Section("cai_x_tables", "other", false)]);

        var result = Rename(source, category: Category);

        Assert.Empty(Assert.Single(result.Objects).Sections);
        Assert.Equal(new CopyNote(CopyNoteKind.HiddenSection, "cai_x_tables", 2), Assert.Single(result.Notes));
    }

    [Fact]
    public void Prefix_rows_are_copied_as_sub_objects_with_their_own_sections()
    {
        var built = new List<ObjectId>();
        var result = ObjectCopy.Rename(TraitView("t1_1", "t1_2"), Id("traits_tables", "t9"), id =>
        {
            built.Add(id);
            return StageView(id.KeyValues[0]);
        }, _ => null, Graph, NoIntegerKey);

        Assert.Equal([Id("trait_levels_tables", "t1_1"), Id("trait_levels_tables", "t1_2")], built);
        Assert.Equal(["traits_tables:t9", "trait_levels_tables:t9_1", "trait_levels_tables:t9_2"], result.Objects.Select(o => o.Id.ToString()).ToArray());
        Assert.Empty(result.Objects[0].Sections);
        var stage = result.Objects[1];
        Assert.Equal([("key", "t9_1"), ("trait", "t9"), ("rank", "1")], stage.Root.Select(f => (f.Name, f.Text)).ToArray());
        var effects = Assert.Single(stage.Sections);
        Assert.Equal(("level_effects_tables", "level"), (effects.SourceTable, effects.FieldName));
        Assert.Equal([("effect", "eff_t1_1")], Fields(Assert.Single(effects.Rows)));
        Assert.Equal("t9_2", result.Objects[2].Id.KeyValues[0]);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void A_third_level_is_reported_not_copied()
    {
        var result = ObjectCopy.Rename(TraitView("t1_1"), Id("traits_tables", "t9"), id => StageView(id.KeyValues[0], withNote: true), _ => null, Graph, NoIntegerKey);

        Assert.Equal(2, result.Objects.Count);
        Assert.Equal(new CopyNote(CopyNoteKind.DepthExceeded, "level_notes_tables", 1), Assert.Single(result.Notes));
        Assert.DoesNotContain(result.Objects[1].Sections, s => s.SourceTable == "level_notes_tables");
    }

    [Fact]
    public void A_source_without_root_is_rejected()
    {
        var source = View(null);

        Assert.Throws<ArgumentException>(() => Rename(source));
    }

    [Fact]
    public void A_new_id_of_another_table_or_shape_is_rejected()
    {
        var source = Source([]);

        Assert.Throws<ArgumentException>(() => ObjectCopy.Rename(source, Id("other_tables", "b9"), _ => source, _ => null, Graph, NoIntegerKey));
        Assert.Throws<ArgumentException>(() => ObjectCopy.Rename(source, new ObjectId(Root, ["a", "b"]), _ => source, _ => null, Graph, NoIntegerKey));
    }

    [Fact]
    public void The_copy_parses_and_diffs_as_inserts_against_an_empty_baseline()
    {
        var bonus = BonusRow("b1", "emp", "x");
        var source = Source(
            [
                Section("bonus_tables", "building", false, bonus),
                Section("upgrades_tables", "from", true, UpgradeRow("b1", "t1")),
                Section("units_tables", "building", true, UnitRow("u1")),
            ],
            [
                new LocEntry("building_levels", "onscreen_name", "building_levels_onscreen_name_b1", "Barracks", Mod, Id(Root, "b1")),
                new LocEntry("bonus", "label", "bonus_label_b1empx", "Label", Vanilla, bonus.RowId!),
            ]);
        var copy = Assert.Single(Rename(source).Objects);
        var empty = new ObjectView(copy.Id, null, [], [], [], Order, []);

        var changes = ObjectDiff.Compute(empty, copy, Category, Graph, null);

        Assert.Empty(changes.Diagnostics);
        Assert.All(changes.Rows, r => Assert.Equal(RowChangeKind.Insert, r.Kind));
        Assert.Equal([Root, "bonus_tables", "upgrades_tables", "units_tables"], changes.Rows.Select(r => r.Table).ToArray());
        Assert.Equal(["b9", "chain1", "100", ""], changes.Rows[0].Cells);
        Assert.Equal(["b9", "emp", "x", "5", ""], changes.Rows[1].Cells);
        Assert.Equal(["building_levels_onscreen_name_b9", "bonus_label_b9empx"], changes.Texts.Select(t => t.Key).ToArray());
        Assert.All(changes.Texts, t => Assert.Equal(TextChangeKind.Insert, t.Kind));
    }
}
