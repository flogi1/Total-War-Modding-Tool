using TotalWarModTool.Schema;
using static TotalWarModTool.Objects.Tests.BlockFixture;
using static TotalWarModTool.Objects.Tests.TestSchema;

namespace TotalWarModTool.Objects.Tests;

public class BuildingWizardTests
{
    private static readonly BuildingWizardDefinition Definition = new(
        "levels_tables", "chain", "level", "chains_tables", "_chain", "upgrades_tables", "from", "to", "variants_tables", "building", "culture",
        "subculture", "subcultures_tables", "culture", new TextRef("variants_tables", "name"));

    private static readonly GameSchema Schema = Build(
        ("levels_tables", 1, [Field("level_name", key: true), Field("chain", reference: ("chains", "key")), Field("level", FieldType.I32)]),
        ("chains_tables", 1, [Field("key", key: true)]),
        ("variants_tables", 1, [Field("building", key: true, reference: ("levels", "level_name")), Field("culture", key: true), Field("subculture", key: true),
            Field("faction", key: true)]),
        ("subcultures_tables", 1, [Field("subculture", key: true), Field("culture")]),
        ("upgrades_tables", 1, [Field("from", key: true, reference: ("levels", "level_name")), Field("to", key: true, reference: ("levels", "level_name"))]),
        ("ranges_tables", 1,
            [Field("chain", key: true, reference: ("chains", "key")), Field("start", key: true, reference: ("levels", "level_name")),
                Field("end", key: true, reference: ("levels", "level_name"))]));

    private static readonly ReferenceGraph Graph = ReferenceGraph.Build(Schema);

    /// <summary>The template "template_l" in the chain "template_chain" with the stages template_l and l2.</summary>
    private static readonly TemplateChain Template = new("template_l", "template_chain", ["template_l", "l2"]);

    private static object?[] R(params object?[] cells) => cells;

    private static (string Table, int Version, object?[][] Rows) T(string table, params object?[][] rows) => (table, 1, rows);

    private static LocTable Loc(params (string Key, string Text)[] texts) =>
        new(texts.ToDictionary(t => t.Key, t => new LocText(t.Text, Vanilla), StringComparer.Ordinal), []);

    /// <summary>Builds the catalog over one pack holding the given tables.</summary>
    private static BuildingCatalog Catalog(LocTable loc, params (string Table, int Version, object?[][] Rows)[] tables)
    {
        using var tmp = new TempDir();
        var pack = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema, tables));
        var order = TestSources.Order(pack);
        var graph = ReferenceGraph.Build(Schema);
        var store = new TableStore(Schema, graph);
        return BuildingWizard.Catalog(Definition, name => store.Get(name, order, CancellationToken.None), loc);
    }

    private static readonly (string Table, int Version, object?[][] Rows) Subcultures = T("subcultures_tables", R("sc1", "c1"), R("sc2", "c1"), R("sc3", "c2"));

    private static string[] Keys(IEnumerable<WizardChoice> choices) => choices.Select(c => c.Key).ToArray();

    [Fact]
    public void Subcultures_are_listed_with_their_names_sorted_by_name()
    {
        var catalog = Catalog(Loc(("subcultures_name_sc1", "Zeta"), ("subcultures_name_sc3", "alpha")), Subcultures, T("levels_tables"), T("variants_tables"));

        Assert.Equal(["sc3", "sc2", "sc1"], Keys(catalog.Subcultures));
        Assert.Equal(["alpha", null, "Zeta"], catalog.Subcultures.Select(c => c.Name));
    }

    [Fact]
    public void Templates_of_a_subculture_are_the_levels_with_a_variant_of_it_or_of_its_culture()
    {
        var catalog = Catalog(
            Loc(("variants_name_l1c1", "Level B"), ("variants_name_l2sc1", "Level A"), ("variants_name_l3c2", "Level C")),
            Subcultures,
            T("levels_tables", R("l1", "ch1", 0), R("l2", "ch1", 1), R("l3", "ch2", 0), R("l4", "ch3", 0)),
            T("variants_tables", R("l1", "c1", "", ""), R("l2", "", "sc1", ""), R("l3", "c2", "", ""), R("l4", "", "", "f1")));

        var templates = catalog.TemplatesOf("sc1");

        Assert.Equal(["l2", "l1"], Keys(templates));
        Assert.Equal(["Level A", "Level B"], templates.Select(t => t.Name));
        Assert.Equal(["l3"], Keys(catalog.TemplatesOf("sc3")));
        Assert.Empty(catalog.TemplatesOf("unknown"));
    }

    [Fact]
    public void A_template_is_listed_once_even_with_two_matching_variants()
    {
        var catalog = Catalog(
            Loc(("variants_name_l1sc1", "By subculture"), ("variants_name_l1c1", "By culture")),
            Subcultures,
            T("levels_tables", R("l1", "ch1", 0)),
            T("variants_tables", R("l1", "", "sc1", ""), R("l1", "c1", "", "")));

        var template = Assert.Single(catalog.TemplatesOf("sc1"));

        Assert.Equal("l1", template.Key);
        Assert.NotNull(template.Name);
    }

    [Fact]
    public void Stages_of_a_chain_are_sorted_by_level_then_key()
    {
        var catalog = Catalog(
            Loc(("variants_name_ac1", "Stage A")),
            Subcultures,
            T("levels_tables", R("b", "ch1", 1), R("a", "ch1", 1), R("c", "ch1", 0), R("x", "ch2", 0)),
            T("variants_tables", R("a", "c1", "", "")));

        var stages = catalog.StagesOf("ch1");

        Assert.Equal(["c", "a", "b"], stages.Select(s => s.LevelKey));
        Assert.Equal([0L, 1L, 1L], stages.Select(s => s.Level));
        Assert.Equal([null, "Stage A", null], stages.Select(s => s.Name));
        Assert.Empty(catalog.StagesOf("ch9"));
    }

    [Fact]
    public void The_catalog_knows_the_chains_of_its_levels()
    {
        var catalog = Catalog(Loc(), Subcultures, T("levels_tables", R("l1", "ch1", 0), R("l2", "", 0)), T("variants_tables"));

        Assert.True(catalog.HasChain("ch1"));
        Assert.False(catalog.HasChain("ch9"));
        Assert.False(catalog.HasChain(""));
        Assert.True(catalog.HasLevel("l1"));
        Assert.False(catalog.HasLevel("L1"));
        Assert.Equal("ch1", catalog.ChainOf("l1"));
        Assert.Null(catalog.ChainOf("l2"));
        Assert.Null(catalog.ChainOf("nothing"));
    }

    private static BuildingCatalog PreselectCatalog() => Catalog(Loc(("subcultures_name_sc1", "A"), ("subcultures_name_sc2", "B")), Subcultures,
        T("levels_tables", R("b1", "ch1", 0)), T("variants_tables"));

    private static ObjectRow Variant(string culture, string subculture) => new(
        [Val("building", "b1", key: true), Val("culture", culture, key: true), Val("subculture", subculture, key: true), Val("faction", "", key: true)],
        From(Vanilla), new ObjectId("variants_tables", ["b1", culture, subculture, ""]), null);

    private static ObjectView ViewOf(string table, params ObjectRow[] variants) => new(new ObjectId(table, ["b1"]), null, [],
        [Section("variants_tables", "building", false, variants)], [], Order, [table]);

    [Fact]
    public void The_preselection_comes_from_the_variant_of_the_shown_building()
    {
        var catalog = PreselectCatalog();

        Assert.Equal("sc2", catalog.PreselectFor(ViewOf("levels_tables", Variant("c2", ""), Variant("", "sc2"), Variant("", "sc1"))));
    }

    [Fact]
    public void The_preselection_falls_back_to_the_first_subculture_of_the_culture()
    {
        var catalog = PreselectCatalog();

        Assert.Equal("sc1", catalog.PreselectFor(ViewOf("levels_tables", Variant("", ""), Variant("c1", ""))));
        Assert.Null(catalog.PreselectFor(ViewOf("levels_tables", Variant("", ""))));
    }

    [Fact]
    public void The_preselection_is_null_for_another_kind_of_object()
    {
        var catalog = PreselectCatalog();

        Assert.Null(catalog.PreselectFor(ViewOf("units_tables", Variant("", "sc1"))));
        Assert.Null(catalog.PreselectFor(null));
    }

    // ---- Compose ----

    private static ParsedSection Rows(string table, string field, string fieldName, params string[] values) =>
        new(table, field, values.Select(v => new ParsedRow([new ParsedField(fieldName, v, 0)], [], 0)).ToList(), 0);

    private static ParsedObject Level(IReadOnlyList<ParsedSection> sections) => new(
        new ObjectId("levels_tables", ["new_l"]),
        [new ParsedField("level_name", "new_l", 0), new ParsedField("Chain", "template_chain", 0), new ParsedField("level", "2", 0)],
        [], sections, []);

    private static CopyResult LevelCopy(params CopyNote[] notes) => new(
        [
            Level(
            [
                Rows("units_tables", "building", "unit", "u1"),
                Rows("upgrades_tables", "to", "from", "template_prev"),
                new ParsedSection("variants_tables", "building",
                    [new ParsedRow([new ParsedField("culture", "c1", 0)], [new ParsedText("name", "Barracks", 0)], 0)], 0),
            ]),
            new ParsedObject(new ObjectId("sub_tables", ["new_l_sub"]), [new ParsedField("key", "new_l_sub", 0)], [], [], []),
        ],
        notes);

    private static CopyResult ChainCopy(params CopyNote[] notes) => new(
        [
            new ParsedObject(new ObjectId("chains_tables", ["new_l_chain"]), [new ParsedField("key", "new_l_chain", 0)],
                [new ParsedText("chain_name", "Chain", 0)], [], []),
        ],
        notes);

    private static string RootField(ParsedObject o, string name) => o.Root.Single(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).Text;

    [Fact]
    public void A_new_chain_points_the_level_at_the_copied_chain_at_level_zero_and_drops_the_upgrades()
    {
        var result = BuildingWizard.Compose(Definition, LevelCopy(), ChainCopy(), null, " (copy)", Graph, Template);

        Assert.Equal(["levels_tables", "sub_tables", "chains_tables"], result.Objects.Select(o => o.Id.Table));
        var level = result.Objects[0];
        Assert.Equal("new_l_chain", RootField(level, "chain"));
        Assert.Equal("0", RootField(level, "level"));
        Assert.Equal(["units_tables", "variants_tables"], level.Sections.Select(s => s.SourceTable));
        Assert.Equal(ChainCopy().Objects[0].Id, result.Objects[2].Id);
    }

    [Fact]
    public void A_new_chain_takes_over_no_stage_of_the_template_chain()
    {
        // The stage "new_l_chain_1" starts with the chain key plus "_", so ObjectCopy made it a sub-object of the chain copy.
        var chainCopy = new CopyResult(
        [
            ChainCopy().Objects[0],
            new ParsedObject(new ObjectId("levels_tables", ["new_l_chain_1"]),
                [new ParsedField("level_name", "new_l_chain_1", 0), new ParsedField("chain", "new_l_chain", 0), new ParsedField("level", "1", 0)], [], [], []),
            new ParsedObject(new ObjectId("sub_tables", ["new_l_chain_sub"]), [new ParsedField("key", "new_l_chain_sub", 0)], [], [], []),
        ],
        []);

        var result = BuildingWizard.Compose(Definition, LevelCopy(), chainCopy, null, " (copy)", Graph, Template);

        var levels = result.Objects.Where(o => o.Id.Table == "levels_tables").ToList();
        Assert.Equal("new_l", Assert.Single(levels).Id.KeyValues[0]);
        Assert.Contains(result.Objects, o => o.Id.Table == "chains_tables" && o.Id.KeyValues[0] == "new_l_chain");
        Assert.Contains(result.Objects, o => o.Id.Table == "sub_tables" && o.Id.KeyValues[0] == "new_l_chain_sub");
    }

    [Fact]
    public void A_new_chain_drops_the_note_about_the_stages_of_the_template_chain()
    {
        var chainCopy = ChainCopy(new CopyNote(CopyNoteKind.ForeignKeyRows, "levels_tables", 3), new CopyNote(CopyNoteKind.ForeignKeyRows, "other", 1));

        var result = BuildingWizard.Compose(Definition, LevelCopy(), chainCopy, null, " (copy)", Graph, Template);

        Assert.Equal(new CopyNote(CopyNoteKind.ForeignKeyRows, "other", 1), Assert.Single(result.Notes));
    }

    [Fact]
    public void The_notes_of_both_copies_are_added_up()
    {
        var levelCopy = LevelCopy(new CopyNote(CopyNoteKind.ForeignKeyRows, "other", 2), new CopyNote(CopyNoteKind.HiddenSection, "h", 1));
        var chainCopy = ChainCopy(new CopyNote(CopyNoteKind.ForeignKeyRows, "other", 1));

        var result = BuildingWizard.Compose(Definition, levelCopy, chainCopy, null, " (copy)", Graph, Template);

        Assert.Equal([new CopyNote(CopyNoteKind.ForeignKeyRows, "other", 3), new CopyNote(CopyNoteKind.HiddenSection, "h", 1)], result.Notes);
    }

    [Fact]
    public void A_template_without_chain_gets_no_chain_and_a_note()
    {
        var result = BuildingWizard.Compose(Definition, LevelCopy(), null, null, " (copy)", Graph, null);

        Assert.Equal(["levels_tables", "sub_tables"], result.Objects.Select(o => o.Id.Table));
        Assert.Equal("template_chain", RootField(result.Objects[0], "chain"));
        Assert.Equal("0", RootField(result.Objects[0], "level"));
        Assert.DoesNotContain(result.Objects[0].Sections, s => s.SourceTable == "upgrades_tables");
        Assert.Equal(new CopyNote(CopyNoteKind.NoChain, "chains_tables", 0), Assert.Single(result.Notes));
    }

    [Fact]
    public void A_stage_in_an_existing_chain_follows_the_chosen_stage()
    {
        var result = BuildingWizard.Compose(Definition, LevelCopy(), null, new BuildingStage("l3", 2, null), " (copy)", Graph, null);

        var level = Assert.Single(result.Objects, o => o.Id.Table == "levels_tables");
        Assert.Equal("3", RootField(level, "level"));
        Assert.Equal("template_chain", RootField(level, "chain"));
        var upgrade = Assert.Single(level.Sections, s => s.SourceTable == "upgrades_tables");
        Assert.Equal("to", upgrade.FieldName);
        var row = Assert.Single(upgrade.Rows);
        var field = Assert.Single(row.Fields);
        Assert.Equal(("from", "l3"), (field.Name, field.Text));
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void The_name_texts_get_the_suffix()
    {
        var result = BuildingWizard.Compose(Definition, LevelCopy(), ChainCopy(), null, " (copy)", Graph, Template);

        var variants = Assert.Single(result.Objects[0].Sections, s => s.SourceTable == "variants_tables");
        Assert.Equal("Barracks (copy)", Assert.Single(Assert.Single(variants.Rows).Texts).Text);
        Assert.Equal("Chain", Assert.Single(result.Objects[2].Texts).Text);
    }

    [Fact]
    public void A_root_field_the_definition_names_but_the_level_lacks_is_an_error()
    {
        var level = Level([]) with { Root = [new ParsedField("level_name", "new_l", 0)] };

        Assert.Throws<InvalidOperationException>(() => BuildingWizard.Compose(Definition, new CopyResult([level], []), null, null, "", Graph, null));
    }

    private static ParsedRow RangeRow(params (string Name, string Text)[] fields) =>
        new(fields.Select(f => new ParsedField(f.Name, f.Text, 0)).ToList(), [], 0);

    private static (string Name, string Text)[] RowFields(ParsedSection section) => Assert.Single(section.Rows).Fields.Select(f => (f.Name, f.Text)).ToArray();

    [Fact]
    public void A_new_chain_points_the_copied_rows_at_the_new_stage_and_chain_and_drops_rows_of_other_stages()
    {
        // The level copy reaches the range through "start"; the chain copy through "chain" (ObjectCopy left the other fields as they were).
        var levelCopy = LevelCopy();
        levelCopy = levelCopy with
        {
            Objects =
            [
                levelCopy.Objects[0] with
                {
                    Sections =
                    [
                        .. levelCopy.Objects[0].Sections,
                        new ParsedSection("ranges_tables", "start",
                            [RangeRow(("chain", "template_chain"), ("end", "template_l")), RangeRow(("chain", "template_chain"), ("end", "l2"))], 0),
                    ],
                },
                .. levelCopy.Objects.Skip(1),
            ],
        };
        var chainCopy = ChainCopy() with
        {
            Objects =
            [
                ChainCopy().Objects[0] with
                {
                    Sections =
                    [
                        new ParsedSection("ranges_tables", "chain",
                            [RangeRow(("start", "template_l"), ("end", "template_l")), RangeRow(("start", "l2"), ("end", "l2")), RangeRow(("start", "x1"), ("end", "x1"))], 0),
                    ],
                },
            ],
        };

        var result = BuildingWizard.Compose(Definition, levelCopy, chainCopy, null, " (copy)", Graph, Template);

        Assert.Equal([("chain", "new_l_chain"), ("end", "new_l")], RowFields(Assert.Single(result.Objects[0].Sections, s => s.SourceTable == "ranges_tables")));
        var chainRanges = Assert.Single(result.Objects[2].Sections);
        Assert.Equal([[("start", "new_l"), ("end", "new_l")], [("start", "x1"), ("end", "x1")]],
            chainRanges.Rows.Select(r => r.Fields.Select(f => (f.Name, f.Text)).ToArray()).ToArray());
        Assert.Equal(new CopyNote(CopyNoteKind.ForeignKeyRows, "ranges_tables", 2), Assert.Single(result.Notes));
    }

    [Fact]
    public void A_stage_in_an_existing_chain_keeps_the_references_of_the_template()
    {
        var levelCopy = LevelCopy();
        levelCopy = levelCopy with
        {
            Objects = [levelCopy.Objects[0] with { Sections = [new ParsedSection("ranges_tables", "start", [RangeRow(("chain", "template_chain"), ("end", "l2"))], 0)] }],
        };

        var result = BuildingWizard.Compose(Definition, levelCopy, null, new BuildingStage("l2", 1, null), " (copy)", Graph, null);

        Assert.Equal([("chain", "template_chain"), ("end", "l2")], RowFields(Assert.Single(result.Objects[0].Sections, s => s.SourceTable == "ranges_tables")));
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void A_chain_copy_needs_the_template_chain()
    {
        Assert.Throws<ArgumentException>(() => BuildingWizard.Compose(Definition, LevelCopy(), ChainCopy(), null, " (copy)", Graph, null));
    }

    [Fact]
    public void The_chain_key_is_the_new_level_key_plus_the_suffix()
    {
        var id = BuildingWizard.ChainIdFor(Definition, "my_barracks");

        Assert.Equal(new ObjectId("chains_tables", ["my_barracks_chain"]), id);
    }
}
