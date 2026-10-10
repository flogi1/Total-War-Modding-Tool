using TotalWarModTool.Schema;

namespace TotalWarModTool.Objects.Tests;

public class BlockTextTests
{
    private static readonly TableSource Vanilla = new(SourceKind.Vanilla, "db.pack", "db.pack");
    private static readonly TableSource Mod = new(SourceKind.Mod, "mod.pack", "mod.pack");
    private static readonly LoadOrder Order = new([Vanilla, Mod], null, []);

    private static string Text(BlockLine line) => string.Concat(line.Spans.Select(span => span.Text));

    private static string Trailing(BlockLine line) => string.Concat(line.Trailing.Select(span => span.Text));

    private static ObjectId Id(string table, string key) => new(table, [key]);

    private static RowProvenance From(TableSource source, params TableSource[] overridden) => new(source, overridden, false, false);

    private static FieldValue Plain(string name, string text) =>
        new(name, FieldType.StringU8, text, null, ReferenceResolution.None, null, null);

    private static FieldValue Key(string name, string text) =>
        new(name, FieldType.StringU8, text, null, ReferenceResolution.None, null, null, IsKey: true);

    private static FieldValue Reference(string name, string text, ReferenceResolution resolution, ObjectId? target, string? loc) =>
        new(name, FieldType.StringU8, text, new TableReference("x_tables", "key"), resolution, target, loc);

    private static ObjectRow Row(RowProvenance provenance, params FieldValue[] fields) => new(fields, provenance, null, null);

    private static ObjectView View(ObjectRow? root, IReadOnlyList<ObjectSection>? sections = null, IReadOnlyList<ObjectWarning>? warnings = null,
        string key = "b1", IReadOnlyList<LocEntry>? texts = null) =>
        new(Id("building_levels_tables", key), root, texts ?? [], sections ?? [], warnings ?? [], Order, ["building_levels_tables"]);

    private static ObjectSection Section(string table, string field, bool junction, params ObjectRow[] rows) => new(table, field, junction, rows);

    private static ObjectRow Junction(ObjectId target, string text, string? loc, RowProvenance provenance) =>
        new([Plain("building", "b1")], provenance, null, Reference("effect", text, ReferenceResolution.Found, target, loc));

    private static ObjectRow KeyedRow(string key, params FieldValue[] more) =>
        new([Plain("building", "b1"), Key("effect", key), .. more], From(Vanilla), Id("effects_tables", key), null);

    [Fact]
    public void The_head_holds_table_and_key_and_the_provenance_is_trailing()
    {
        var root = Row(From(Mod, Vanilla), Key("level_name", "b1"));

        var block = ObjectText.RenderBlock(View(root), null);

        var head = block.Lines[0];
        Assert.Equal(BlockLineKind.Head, head.Kind);
        Assert.Equal("building_levels b1", Text(head));
        Assert.Equal([SpanKind.TableName, SpanKind.Normal, SpanKind.Key], head.Spans.Select(s => s.Kind).ToArray());
        Assert.Equal("[mod.pack] [db.pack]", Trailing(head));
        Assert.Equal(0, head.Indent);
    }

    [Fact]
    public void Root_fields_are_complete_and_loc_texts_are_trailing()
    {
        var target = Id("building_chains_tables", "chain1");
        var root = Row(From(Vanilla),
            Reference("chain", "chain1", ReferenceResolution.Found, target, "Chain One"),
            Reference("commodity", "gold", ReferenceResolution.LocOnly, null, "Gold"),
            Plain("create_cost", "100"),
            Plain("empty", ""));

        var block = ObjectText.RenderBlock(View(root), null);

        Assert.Equal(5, block.Lines.Count);
        Assert.All(block.Lines.Skip(1), l => Assert.Equal(BlockLineKind.RootField, l.Kind));
        Assert.All(block.Lines.Skip(1), l => Assert.Equal(1, l.Indent));
        Assert.Equal("chain = chain1", Text(block.Lines[1]));
        Assert.Equal("\"Chain One\"", Trailing(block.Lines[1]));
        Assert.Equal([SpanKind.FieldName, SpanKind.Normal, SpanKind.Link], block.Lines[1].Spans.Select(s => s.Kind).ToArray());
        Assert.Equal(target, block.Lines[1].Spans[2].Target);
        Assert.Equal([SpanKind.LocText], block.Lines[1].Trailing.Select(s => s.Kind).ToArray());
        Assert.Equal("commodity = gold", Text(block.Lines[2]));
        Assert.Equal("\"Gold\"", Trailing(block.Lines[2]));
        Assert.Equal("create_cost = 100", Text(block.Lines[3]));
        Assert.Equal("", Trailing(block.Lines[3]));
        Assert.Equal("empty = ", Text(block.Lines[4]));
        Assert.Equal("", Trailing(block.Lines[4]));
        Assert.Equal(2, block.Lines[4].Spans.Count);
    }

    [Fact]
    public void Root_texts_stand_below_the_fields_as_column_equals_quoted_text()
    {
        var root = Row(From(Vanilla), Plain("key", "b1"));
        var texts = new[] { new LocEntry("building_levels", "onscreen_name", "building_levels_onscreen_name_b1", "Barracks", Mod, Id("building_levels_tables", "b1")) };

        var block = ObjectText.RenderBlock(View(root, texts: texts), null);

        var line = block.Lines[2];
        Assert.Equal(BlockLineKind.RootText, line.Kind);
        Assert.Equal("onscreen_name = \"Barracks\"", Text(line));
        Assert.Equal("[mod.pack]", Trailing(line));
        Assert.Equal(1, line.Indent);
        Assert.Equal([SpanKind.FieldName, SpanKind.Normal, SpanKind.LocText], line.Spans.Select(s => s.Kind).ToArray());
    }

    [Fact]
    public void Texts_of_reference_targets_are_not_lines()
    {
        var chain = Id("building_chains_tables", "chain1");
        var root = Row(From(Vanilla), Reference("chain", "chain1", ReferenceResolution.Found, chain, "Chain One"));
        var texts = new[] { new LocEntry("building_chains", "name", "building_chains_name_chain1", "Chain One", Vanilla, chain) };

        var block = ObjectText.RenderBlock(View(root, texts: texts), null);

        Assert.Equal([BlockLineKind.Head, BlockLineKind.RootField], block.Lines.Select(l => l.Kind).ToArray());
    }

    [Fact]
    public void A_list_row_puts_keys_in_the_head_and_fields_and_texts_below()
    {
        var e1 = Id("effects_tables", "e1");
        var effect = new FieldValue("effect", FieldType.StringU8, "e1", new TableReference("x_tables", "key"), ReferenceResolution.Found, e1, "Income",
            IsKey: true);
        var row = new ObjectRow(
            [Plain("building", "b1"), effect, Plain("scope", "province"), Plain("value", "10"), Plain("note", "")],
            From(Vanilla), e1, null);
        var texts = new[] { new LocEntry("effects", "description", "effects_description_e1", "More gold", Vanilla, e1) };
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("effects_tables", "building", false, row)], texts: texts);

        var block = ObjectText.RenderBlock(view, null);

        Assert.Equal(
            [BlockLineKind.Head, BlockLineKind.RootField, BlockLineKind.Blank, BlockLineKind.SectionHead, BlockLineKind.RowHead, BlockLineKind.RowField,
                BlockLineKind.RowField, BlockLineKind.RowText],
            block.Lines.Select(l => l.Kind).ToArray());
        Assert.Equal("effects", Text(block.Lines[3]));
        Assert.Equal("(1) [db.pack]", Trailing(block.Lines[3]));
        Assert.Equal(0, block.Lines[3].Indent);
        Assert.Equal(0, block.Lines[3].Section);
        Assert.Equal("effect = e1", Text(block.Lines[4]));
        Assert.Equal("\"Income\"  [db.pack]", Trailing(block.Lines[4]));
        Assert.Equal(1, block.Lines[4].Indent);
        Assert.Equal(0, block.Lines[4].Row);
        Assert.Equal("scope = province", Text(block.Lines[5]));
        Assert.Equal("value = 10", Text(block.Lines[6]));
        Assert.Equal("description = \"More gold\"", Text(block.Lines[7]));
        Assert.All(block.Lines.Skip(5), l => Assert.Equal(2, l.Indent));
        Assert.All(block.Lines.Skip(4), l => Assert.Equal((0, 0), (l.Section!.Value, l.Row!.Value)));
    }

    [Fact]
    public void A_section_head_names_the_field_when_the_table_appears_twice()
    {
        var from = Row(From(Vanilla), Plain("from", "b1"), Key("to", "b2"));
        var to = Row(From(Vanilla), Plain("to", "b1"), Key("from", "b0"));
        var twice = View(Row(From(Vanilla), Plain("key", "b1")),
            [Section("upgrades_tables", "from", false, from), Section("upgrades_tables", "to", false, to)]);
        var once = View(Row(From(Vanilla), Plain("key", "b1")), [Section("upgrades_tables", "from", false, from)]);

        var heads = ObjectText.RenderBlock(twice, null).Lines.Where(l => l.Kind == BlockLineKind.SectionHead).Select(Text).ToArray();
        var single = ObjectText.RenderBlock(once, null).Lines.Single(l => l.Kind == BlockLineKind.SectionHead);

        Assert.Equal(["upgrades.from", "upgrades.to"], heads);
        Assert.Equal("upgrades", Text(single));
    }

    [Fact]
    public void A_table_without_key_uses_its_first_field_as_the_head()
    {
        var row = Row(From(Vanilla), Plain("building", "b1"), Plain("a", ""), Plain("b", "x"));
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("t_tables", "building", false, row)]);

        var block = ObjectText.RenderBlock(view, null);

        var head = block.Lines.Single(l => l.Kind == BlockLineKind.RowHead);
        Assert.Equal("a = ", Text(head));
        Assert.Equal(2, head.Spans.Count);
        Assert.Equal("b = x", Text(block.Lines.Single(l => l.Kind == BlockLineKind.RowField)));
    }

    [Fact]
    public void A_sentence_row_is_one_line_with_its_values_and_only_the_fields_the_pattern_does_not_name_below()
    {
        var u1 = Id("units_def_tables", "u1");
        var row = Row(From(Vanilla), Plain("building", "b1"), Reference("unit", "u1", ReferenceResolution.Found, u1, "Swordsmen"), Plain("xp", "2"),
            Plain("rank", ""));
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("units_tables", "building", false, row)]);
        var category = new CategoryDefinition("building_levels_tables", "cat",
            [new SectionRule("units_tables", null, null, SectionMode.Sentence, "allows {unit}", false)]);

        var block = ObjectText.RenderBlock(view, category);

        var rows = block.Lines.SkipWhile(l => l.Kind != BlockLineKind.SectionHead).Skip(1).ToList();
        Assert.Equal([BlockLineKind.RowHead, BlockLineKind.RowField], rows.Select(l => l.Kind).ToArray());
        Assert.Equal("allows u1", Text(rows[0]));
        Assert.Equal("\"Swordsmen\"  [db.pack]", Trailing(rows[0]));
        Assert.Equal([SpanKind.Normal, SpanKind.Link], rows[0].Spans.Select(s => s.Kind).ToArray());
        Assert.Equal("xp = 2", Text(rows[1]));
        Assert.Equal(2, rows[1].Indent);
    }

    [Fact]
    public void A_junction_row_is_only_the_other_end_plus_its_other_fields()
    {
        var e1 = Id("effects_tables", "e1");
        var plain = Junction(e1, "e1", "Income", From(Vanilla));
        var extra = new ObjectRow([Plain("building", "b1"), Plain("value", "3"), Plain("empty", "")], From(Vanilla), null,
            Reference("effect", "e2", ReferenceResolution.Unchecked, null, null));
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("junction_tables", "building", true, plain, extra)]);

        var block = ObjectText.RenderBlock(view, null);

        var rows = block.Lines.SkipWhile(l => l.Kind != BlockLineKind.SectionHead).Skip(1).ToList();
        // A one-line junction row is no block: no blank line between it and the block that follows.
        Assert.Equal([BlockLineKind.RowHead, BlockLineKind.RowHead, BlockLineKind.RowField], rows.Select(l => l.Kind).ToArray());
        Assert.Equal("e1", Text(rows[0]));
        Assert.Equal("\"Income\"  [db.pack]", Trailing(rows[0]));
        Assert.Equal(e1, rows[0].Spans[0].Target);
        Assert.Equal("e2", Text(rows[1]));
        Assert.Equal("value = 3", Text(rows[2]));
        Assert.Equal(2, rows[2].Indent);
    }

    [Fact]
    public void Long_sections_are_not_collapsed_and_the_ranges_cover_every_row()
    {
        var rows = Enumerable.Range(0, 25).Select(i => Row(From(Vanilla), Plain("building", "b1"), Key("key", "k" + i))).ToArray();
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("t_tables", "building", false, rows)]);

        var block = ObjectText.RenderBlock(view, null);

        Assert.Equal(25, block.Lines.Count(l => l.Kind == BlockLineKind.RowHead));
        Assert.DoesNotContain(block.Lines, l => l.Spans.Concat(l.Trailing).Any(s => s.Kind == SpanKind.Collapsed));
        var range = Assert.Single(block.Sections);
        Assert.Equal(25, range.Rows.Count);
        Assert.Equal(range.HeadLine + 1, range.Rows[0].FirstLine);
        Assert.Equal(block.Lines.Count - 1, range.Rows[^1].LastLine);
        Assert.Equal(range.Rows[^1].LastLine, range.LastLine);
        Assert.Equal(range.HeadLine + 1, range.FirstLine);
        Assert.Equal(BlockLineKind.SectionHead, block.Lines[range.HeadLine].Kind);
    }

    [Fact]
    public void Blocks_are_separated_by_a_blank_line_inside_the_row_ranges()
    {
        var one = Row(From(Vanilla), Plain("building", "b1"), Key("key", "k1"), Plain("value", "1"));
        var two = Row(From(Vanilla), Plain("building", "b1"), Key("key", "k2"), Plain("value", "2"));
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("t_tables", "building", false, one, two)]);

        var block = ObjectText.RenderBlock(view, null);

        var range = Assert.Single(block.Sections);
        var h = range.HeadLine + 1;
        Assert.Equal([BlockLineKind.RowHead, BlockLineKind.RowField, BlockLineKind.Blank, BlockLineKind.RowHead, BlockLineKind.RowField],
            block.Lines.Skip(h).Select(l => l.Kind).ToArray());
        Assert.Equal(1, block.Lines[h + 2].Indent);
        Assert.Equal(new BlockRowRange(0, 0, h, h + 1), range.Rows[0]);
        Assert.Equal(new BlockRowRange(0, 1, h + 3, h + 4), range.Rows[1]);
    }

    [Fact]
    public void Warnings_are_annotation_lines_without_text()
    {
        var view = View(null, warnings: [new ObjectWarning(ObjectWarningKind.RootNotFound, null, "no such row")], key: "b9");

        var block = ObjectText.RenderBlock(view, null);

        Assert.Equal(2, block.Lines.Count);
        Assert.Equal(BlockLineKind.Annotation, block.Lines[1].Kind);
        Assert.Empty(block.Lines[1].Spans);
        var trailing = Assert.Single(block.Lines[1].Trailing);
        Assert.Equal(SpanKind.Warning, trailing.Kind);
        Assert.Equal("RootNotFound: no such row", trailing.Text);
        Assert.Equal("building_levels b9\n", block.Text);
    }

    [Fact]
    public void Hidden_and_empty_sections_are_omitted_and_the_order_follows_the_category()
    {
        var target = Id("effects_tables", "e");
        var row = Junction(target, "e", null, From(Vanilla));
        var view = View(Row(From(Vanilla), Plain("key", "b1")),
        [
            Section("cai_x", "building", false, row),
            Section("building_effects_junction", "building", true, row),
            Section("technologies", "building", false, row),
            Section("a_empty", "building", false),
        ]);
        var category = new CategoryDefinition("building_levels_tables", "cat",
        [
            new SectionRule("building_effects_junction", null, "objects.section.effects", SectionMode.List, null, false),
            new SectionRule("cai_x", null, null, SectionMode.List, null, true),
        ]);

        var block = ObjectText.RenderBlock(view, category);

        var heads = block.Lines.Where(l => l.Kind == BlockLineKind.SectionHead).ToList();
        Assert.Equal(["building_effects_junction", "technologies"], heads.Select(Text).ToArray());
        Assert.Equal([1, 2], heads.Select(h => h.Section!.Value).ToArray());
        var title = heads[0].Trailing[0];
        Assert.Equal(SpanKind.Title, title.Kind);
        Assert.Equal("objects.section.effects", title.Text);
        Assert.Equal(1, title.Section);
        Assert.Equal("objects.section.effects  (1) [db.pack]", Trailing(heads[0]));
        Assert.Equal("(1) [db.pack]", Trailing(heads[1]));
        Assert.Equal([1, 2], block.Sections.Select(s => s.Section).ToArray());
        Assert.DoesNotContain(block.Lines, l => Text(l).StartsWith("cai_x", StringComparison.Ordinal));
    }

    [Fact]
    public void The_text_property_joins_lines_with_indent_and_newline()
    {
        var row = KeyedRow("e1", Plain("value", "10"));
        var view = View(Row(From(Vanilla), Plain("create_cost", "100")), [Section("effects_tables", "building", false, row)]);

        var block = ObjectText.RenderBlock(view, null);

        Assert.Equal("building_levels b1\n  create_cost = 100\n\neffects\n  effect = e1\n    value = 10", block.Text);
    }

    [Fact]
    public void A_value_containing_the_assignment_token_round_trips()
    {
        var root = Row(From(Vanilla), Plain("note", "a = b  c"));

        var block = ObjectText.RenderBlock(View(root), null);

        Assert.Equal("note = a = b  c", Text(block.Lines[1]));
    }

    [Fact]
    public void Render_then_parse_gives_no_diagnostics_for_every_kind_of_row()
    {
        var view = BlockFixture.EveryKind();

        var block = ObjectText.RenderBlock(view, BlockFixture.Category);
        var parsed = ObjectTextParser.Parse(block.Text, view, BlockFixture.Category, BlockFixture.Graph);

        Assert.Empty(parsed.Diagnostics);
        // The category's rule order first (units), then the rest alphabetically.
        Assert.Equal(["units_tables", "effects_tables", "notes_tables", "upgrades_tables", "variants_tables"], parsed.Sections.Select(s => s.SourceTable).ToArray());
        Assert.Equal("a = b  c", parsed.Root.Single(f => f.Name == "note").Text);
        Assert.Equal("He said \"hi\" \\ x", Assert.Single(parsed.Texts).Text);
    }

    private static ObjectView PerksView(string scope)
    {
        var perk = BlockFixture.Val("perk", "p1", refTable: "perk_defs_tables");
        var row = new ObjectRow([BlockFixture.Val("building", "b1", refTable: BlockFixture.Root, refColumn: "level_name"), perk,
            BlockFixture.Val("scope", scope, key: true), BlockFixture.Val("rank", "1", FieldType.I32)], From(Vanilla),
            new ObjectId("perks_tables", [scope]), perk);
        return BlockFixture.View(BlockFixture.RootRow(), [Section("perks_tables", "building", true, row)]);
    }

    [Fact]
    public void An_empty_key_field_under_a_junction_row_round_trips()
    {
        var view = PerksView("");

        var block = ObjectText.RenderBlock(view, BlockFixture.Category);
        var parsed = ObjectTextParser.Parse(block.Text, view, BlockFixture.Category, BlockFixture.Graph);
        var changes = ObjectDiff.Compute(view, parsed, BlockFixture.Category, BlockFixture.Graph, null);

        Assert.Contains("    scope = ", block.Text.Split('\n'));
        Assert.Empty(changes.Diagnostics);
        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void A_junction_field_the_sentence_pattern_does_not_name_is_still_in_the_text()
    {
        var view = PerksView("s1");
        var category = new CategoryDefinition(BlockFixture.Root, "cat", [new SectionRule("perks_tables", null, null, SectionMode.Sentence, "rank {rank}", false)]);

        var block = ObjectText.RenderBlock(view, category);
        var parsed = ObjectTextParser.Parse(block.Text, view, category, BlockFixture.Graph);
        var changes = ObjectDiff.Compute(view, parsed, category, BlockFixture.Graph, null);

        Assert.Contains("    perk = p1", block.Text.Split('\n'));
        Assert.Empty(changes.Diagnostics);
        Assert.True(changes.IsEmpty);
        Assert.Contains(Assert.Single(Assert.Single(parsed.Sections).Rows).Fields, f => f.Name == "perk" && f.Text == "p1");
    }

    [Fact]
    public void A_row_shows_its_empty_field_when_a_text_of_the_row_has_the_same_name()
    {
        var e1 = Id("effects_tables", "e1");
        var row = new ObjectRow([Plain("building", "b1"), Key("effect", "e1"), Plain("scope", ""), Plain("value", "")], From(Vanilla), e1, null);
        var texts = new[] { new LocEntry("effects", "scope", "effects_scope_e1", "Scope text", Vanilla, e1) };
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("effects_tables", "building", false, row)], texts: texts);

        var block = ObjectText.RenderBlock(view, null);

        var rows = block.Lines.SkipWhile(l => l.Kind != BlockLineKind.SectionHead).Skip(1).Select(Text).ToArray();
        // The empty value field stays out, the empty field that shares its name with the text is shown before the text.
        Assert.Equal(["effect = e1", "scope = ", "scope = \"Scope text\""], rows);
    }

    [Fact]
    public void A_text_that_shares_its_name_with_a_root_field_round_trips()
    {
        var view = BlockFixture.View(BlockFixture.RootRow(), texts: [new LocEntry("building_levels", "note", "building_levels_note_b1", "A note", Vanilla, Id(BlockFixture.Root, "b1"))]);

        var block = ObjectText.RenderBlock(view, BlockFixture.Category);
        var parsed = ObjectTextParser.Parse(block.Text, view, BlockFixture.Category, BlockFixture.Graph);
        var changes = ObjectDiff.Compute(view, parsed, BlockFixture.Category, BlockFixture.Graph, null);

        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(("note", "A note"), (Assert.Single(parsed.Texts).Column, Assert.Single(parsed.Texts).Text));
        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void A_row_text_that_shares_its_name_with_a_field_round_trips()
    {
        var e1 = Id("effects_tables", "e1");
        var row = new ObjectRow([BlockFixture.Val("building", "b1"), BlockFixture.Val("effect", "e1", key: true), BlockFixture.Val("scope", ""), BlockFixture.Val("value", "", FieldType.F32)], From(Vanilla), e1, null);
        var view = BlockFixture.View(BlockFixture.RootRow(), [Section("effects_tables", "building", false, row)],
            [new LocEntry("effects", "scope", "effects_scope_e1", "Scope text", Vanilla, e1)]);

        var parsed = ObjectTextParser.Parse(ObjectText.RenderBlock(view, BlockFixture.Category).Text, view, BlockFixture.Category, BlockFixture.Graph);
        var changes = ObjectDiff.Compute(view, parsed, BlockFixture.Category, BlockFixture.Graph, null);

        Assert.Empty(parsed.Diagnostics);
        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void Render_then_parse_then_diff_is_empty_for_every_kind_of_row()
    {
        var view = BlockFixture.EveryKind();

        var block = ObjectText.RenderBlock(view, BlockFixture.Category);
        var parsed = ObjectTextParser.Parse(block.Text, view, BlockFixture.Category, BlockFixture.Graph);
        var changes = ObjectDiff.Compute(view, parsed, BlockFixture.Category, BlockFixture.Graph, null);

        Assert.Empty(changes.Diagnostics);
        Assert.True(changes.IsEmpty);
    }

    [Theory]
    [InlineData("He said \"hi\"", "\"He said \\\"hi\\\"\"")]
    [InlineData("a\\b", "\"a\\\\b\"")]
    [InlineData("line1\r\nline2", "\"line1\\nline2\"")]
    [InlineData("x\ry", "\"x\\ny\"")]
    public void QuoteText_escapes_quotes_backslashes_and_line_breaks(string text, string expected)
    {
        Assert.Equal(expected, ObjectText.QuoteText(text));
    }
}
