using TotalWarModTool.Schema;

namespace TotalWarModTool.Objects.Tests;

public class ObjectTextTests
{
    private static readonly TableSource Vanilla = new(SourceKind.Vanilla, "db.pack", "db.pack");
    private static readonly TableSource Mod = new(SourceKind.Mod, "mod.pack", "mod.pack");
    private static readonly LoadOrder Order = new([Vanilla, Mod], null, []);
    private static readonly RenderOptions NoExpansion = new(new HashSet<int>());

    private static string Text(TextLine line) => string.Concat(line.Spans.Select(span => span.Text));

    private static SpanKind[] Kinds(TextLine line) => line.Spans.Select(span => span.Kind).ToArray();

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

    [Fact]
    public void ShortTableName_removes_the_tables_suffix()
    {
        Assert.Equal("building_levels", ObjectText.ShortTableName("building_levels_tables"));
        Assert.Equal("technologies", ObjectText.ShortTableName("technologies"));
        Assert.Equal("_tables", ObjectText.ShortTableName("_tables"));
    }

    [Fact]
    public void The_head_shows_table_key_and_provenance()
    {
        var root = Row(From(Mod, Vanilla), Plain("key", "b1"));

        var lines = ObjectText.Render(View(root), null, NoExpansion);

        Assert.Equal("building_levels  b1  [mod.pack] [db.pack]", Text(lines[0]));
        Assert.Equal([SpanKind.TableName, SpanKind.Normal, SpanKind.Key, SpanKind.Normal, SpanKind.ProvenanceMod, SpanKind.Normal, SpanKind.Overridden],
            Kinds(lines[0]));
        Assert.Equal(0, lines[0].Indent);
    }

    [Fact]
    public void Root_fields_render_values_links_and_loc_texts()
    {
        var target = Id("building_chains_tables", "chain1");
        var root = Row(From(Vanilla),
            Reference("chain", "chain1", ReferenceResolution.Found, target, "Chain One"),
            Reference("commodity", "gold", ReferenceResolution.LocOnly, null, "Gold"),
            Plain("create_cost", "100"),
            Reference("missing", "zzz", ReferenceResolution.NotFound, null, null));

        var lines = ObjectText.Render(View(root), null, NoExpansion);

        Assert.Equal("chain = chain1  \"Chain One\"", Text(lines[1]));
        Assert.Equal([SpanKind.FieldName, SpanKind.Normal, SpanKind.Link, SpanKind.Normal, SpanKind.LocText], Kinds(lines[1]));
        Assert.Equal(target, lines[1].Spans[2].Target);
        Assert.Equal(1, lines[1].Indent);
        Assert.Equal("commodity = gold  \"Gold\"", Text(lines[2]));
        Assert.Equal([SpanKind.FieldName, SpanKind.Normal, SpanKind.Value, SpanKind.Normal, SpanKind.LocText], Kinds(lines[2]));
        Assert.Equal("create_cost = 100", Text(lines[3]));
        Assert.Equal([SpanKind.FieldName, SpanKind.Normal, SpanKind.Value], Kinds(lines[3]));
        Assert.Equal(SpanKind.Warning, lines[4].Spans[2].Kind);
    }

    [Fact]
    public void The_loc_block_lists_every_text_with_its_source()
    {
        var root = Row(From(Vanilla), Plain("key", "b1"));
        var texts = new[] { new LocEntry("building_levels", "name", "k1", "Barracks", Mod, Id("building_levels_tables", "b1")) };

        var lines = ObjectText.Render(View(root, texts: texts), null, NoExpansion);

        Assert.Equal("building_levels.name  \"Barracks\"  [mod.pack]", Text(lines[2]));
        Assert.Equal([SpanKind.FieldName, SpanKind.Normal, SpanKind.LocText, SpanKind.Normal, SpanKind.ProvenanceMod], Kinds(lines[2]));
        Assert.Equal(1, lines[2].Indent);
    }

    [Fact]
    public void Loc_entries_name_their_owner()
    {
        var root = Row(From(Vanilla), Plain("key", "b1"));
        var technology = Id("technologies_tables", "t1");
        var texts = new[]
        {
            new LocEntry("building_levels", "onscreen_name", "building_levels_onscreen_name_b1", "Barracks", Vanilla, Id("BUILDING_LEVELS_TABLES", "b1")),
            new LocEntry("technologies", "onscreen_name", "technologies_onscreen_name_t1", "Drill", Mod, technology),
        };

        var lines = ObjectText.Render(View(root, texts: texts), null, NoExpansion);

        // The text of the root needs no link: the head already names it.
        Assert.Equal("building_levels.onscreen_name  \"Barracks\"  [db.pack]", Text(lines[2]));
        Assert.DoesNotContain(lines[2].Spans, s => s.Kind == SpanKind.Link);
        // The text of a section row links the row it belongs to.
        Assert.Equal("technologies.onscreen_name  \"Drill\"  t1  [mod.pack]", Text(lines[3]));
        Assert.Equal([SpanKind.FieldName, SpanKind.Normal, SpanKind.LocText, SpanKind.Normal, SpanKind.Link, SpanKind.Normal, SpanKind.ProvenanceMod],
            Kinds(lines[3]));
        Assert.Equal(technology, lines[3].Spans[4].Target);
        Assert.Equal(1, lines[3].Indent);
    }

    [Fact]
    public void Sections_render_list_rows_without_the_back_reference_field()
    {
        var eff1 = Id("effects_tables", "eff1");
        var row = Row(From(Vanilla),
            Plain("building", "b1"),
            Reference("effect", "eff1", ReferenceResolution.Unchecked, eff1, null),
            Plain("effect_scope", "province"),
            Plain("value", "5"));
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("building_effects_junction", "building", false, row)]);

        var lines = ObjectText.Render(view, null, NoExpansion);

        var start = lines.ToList().FindIndex(l => Text(l).StartsWith("effect = ", StringComparison.Ordinal));
        var line = lines[start];
        Assert.Equal("effect = eff1  [db.pack]", Text(line));
        Assert.Equal(SpanKind.Link, line.Spans[2].Kind);
        Assert.Equal(eff1, line.Spans[2].Target);
        Assert.Equal(SpanKind.ProvenanceVanilla, line.Spans[^1].Kind);
        Assert.Equal(1, line.Indent);
        Assert.Equal(["effect_scope = province", "value = 5"], lines.Skip(start + 1).Select(Text).ToArray());
        Assert.DoesNotContain(lines, l => Text(l).StartsWith("building", StringComparison.Ordinal) && l.Indent > 0);
    }

    [Fact]
    public void A_list_row_is_a_block_with_the_key_line_first_and_one_field_per_line()
    {
        var row = Row(From(Mod, Vanilla),
            Key("building", "b1"),
            Key("culture", "c1"),
            Key("variant", "v1"),
            Reference("short_description", "sd1", ReferenceResolution.LocOnly, null, "Short"),
            Reference("missing", "zzz", ReferenceResolution.NotFound, null, null),
            Plain("value", "5"));
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("building_culture_variants_tables", "building", false, row)]);

        var lines = ObjectText.Render(view, null, NoExpansion);

        var start = lines.ToList().FindIndex(l => Text(l).StartsWith("culture = ", StringComparison.Ordinal));
        var block = lines.Skip(start).ToList();
        Assert.Equal(4, block.Count);
        Assert.Equal("culture = c1  variant = v1  [mod.pack] [db.pack]", Text(block[0]));
        Assert.Equal([SpanKind.FieldName, SpanKind.Normal, SpanKind.Value, SpanKind.Normal, SpanKind.FieldName, SpanKind.Normal, SpanKind.Value,
            SpanKind.Normal, SpanKind.ProvenanceMod, SpanKind.Normal, SpanKind.Overridden], Kinds(block[0]));
        Assert.Equal(1, block[0].Indent);
        Assert.Equal("short_description = sd1  \"Short\"", Text(block[1]));
        Assert.Equal([SpanKind.FieldName, SpanKind.Normal, SpanKind.Value, SpanKind.Normal, SpanKind.LocText], Kinds(block[1]));
        Assert.Equal("missing = zzz", Text(block[2]));
        Assert.Equal(SpanKind.Warning, block[2].Spans[2].Kind);
        Assert.Equal("value = 5", Text(block[3]));
        Assert.All(block.Skip(1), l => Assert.Equal(2, l.Indent));
    }

    [Fact]
    public void Empty_fields_are_omitted_in_a_list_block()
    {
        var row = Row(From(Vanilla), Key("building", "b1"), Key("culture", "c1"), Plain("a", ""), Plain("b", "x"), Plain("c", ""));
        var view = View(Row(From(Vanilla), Plain("key", "b1"), Plain("empty", "")), [Section("t", "building", false, row)]);

        var lines = ObjectText.Render(view, null, NoExpansion);

        // Root fields stay as they are, empty ones included.
        Assert.Equal("empty = ", Text(lines[2]));
        var start = lines.ToList().FindIndex(l => Text(l).StartsWith("culture = ", StringComparison.Ordinal));
        Assert.Equal(["culture = c1  [db.pack]", "b = x"], lines.Skip(start).Select(Text).ToArray());
    }

    [Fact]
    public void A_table_without_key_uses_its_first_field_as_the_block_head()
    {
        var eff1 = Id("effects_tables", "eff1");
        var keyless = Row(From(Vanilla),
            Plain("building", "b1"),
            Plain("note", ""),
            Reference("effect", "eff1", ReferenceResolution.Unchecked, eff1, null),
            Plain("value", "5"));
        // A key that is only the field pointing at the root leaves no key for the head: the first other field takes its place.
        var backKey = Row(From(Vanilla), Key("building", "b1"), Plain("effect", "e2"), Plain("value", "6"));
        var backOnly = Row(From(Vanilla), Key("building", "b1"), Plain("note", ""));
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("t", "building", false, keyless, backKey, backOnly)]);

        var lines = ObjectText.Render(view, null, NoExpansion);

        var title = lines.ToList().FindIndex(l => Text(l).StartsWith("t  (3)", StringComparison.Ordinal));
        Assert.Equal(["effect = eff1  [db.pack]", "value = 5", "", "effect = e2  [db.pack]", "value = 6", "", "[db.pack]"],
            lines.Skip(title + 1).Select(Text).ToArray());
        Assert.Equal(SpanKind.Link, lines[title + 1].Spans[2].Kind);
        Assert.Equal(eff1, lines[title + 1].Spans[2].Target);
        Assert.Equal([SpanKind.ProvenanceVanilla], Kinds(lines[title + 7]));
    }

    [Fact]
    public void Row_blocks_are_separated_by_an_empty_line_and_the_collapse_counts_rows()
    {
        var rows = Enumerable.Range(0, 25)
            .Select(i => Row(From(Vanilla), Plain("building", "b1"), Key("key", "k" + i), Plain("value", i.ToString()))).ToArray();
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("t", "building", false, rows)]);

        var collapsed = ObjectText.Render(view, null, NoExpansion);

        var title = collapsed.ToList().FindIndex(l => Text(l).StartsWith("t  (25)", StringComparison.Ordinal));
        var body = collapsed.Skip(title + 1).ToList();
        // Five row blocks of two lines, an empty line between two blocks, then the collapsed line.
        Assert.Equal(5 * 2 + 4 + 1, body.Count);
        for (var block = 0; block < 5; block++)
        {
            Assert.Equal("key = k" + block + "  [db.pack]", Text(body[block * 3]));
            Assert.Equal("value = " + block, Text(body[block * 3 + 1]));
            if (block < 4)
            {
                Assert.Empty(body[block * 3 + 2].Spans);
                Assert.Equal(1, body[block * 3 + 2].Indent);
            }
        }

        var more = body[^1];
        Assert.Equal("+20", Text(more));
        Assert.Equal(20, more.Spans[0].Count);
        Assert.Equal(1, more.Indent);

        var expanded = ObjectText.Render(view, null, new RenderOptions(new HashSet<int> { 0 }));

        var expandedBody = expanded.Skip(title + 1).ToList();
        Assert.Equal(25 * 2 + 24, expandedBody.Count);
        Assert.Equal(24, expandedBody.Count(l => l.Spans.Count == 0));
        Assert.Equal("key = k24  [db.pack]", Text(expandedBody[^2]));
    }

    [Fact]
    public void Line_breaks_in_loc_texts_are_shown_as_spaces()
    {
        var target = Id("building_chains_tables", "chain1");
        var root = Row(From(Vanilla), Reference("chain", "chain1", ReferenceResolution.Found, target, "Chain\r\nOne"));
        var texts = new[] { new LocEntry("building_levels", "name", "k1", "Line one\r\nLine two\nthree\rfour", Mod, Id("building_levels_tables", "b1")) };
        var row = Row(From(Vanilla), Plain("building", "b1"), Reference("commodity", "gold", ReferenceResolution.LocOnly, null, "Pure\ngold"));

        var lines = ObjectText.Render(View(root, [Section("t", "building", false, row)], texts: texts), null, NoExpansion);

        Assert.Equal("chain = chain1  \"Chain One\"", Text(lines[1]));
        Assert.Equal("building_levels.name  \"Line one Line two three four\"  [mod.pack]", Text(lines[2]));
        Assert.Contains(lines, l => Text(l) == "commodity = gold  \"Pure gold\"  [db.pack]");
    }

    [Fact]
    public void A_composite_key_is_shown_with_a_separator_in_the_head_and_the_owner_link()
    {
        var id = new ObjectId("pairs_tables", ["x", "y"]);
        var owner = new ObjectId("pair_uses_tables", ["p", "q"]);
        var texts = new[] { new LocEntry("pair_uses", "name", "pair_uses_name_pq", "Use", Vanilla, owner) };
        var view = new ObjectView(id, Row(From(Vanilla), Key("a", "x"), Key("b", "y")), texts, [], [], Order, ["pairs_tables"]);

        var lines = ObjectText.Render(view, null, NoExpansion);

        Assert.Equal("pairs  x | y  [db.pack]", Text(lines[0]));
        Assert.Equal(SpanKind.Key, lines[0].Spans[2].Kind);
        var loc = lines.Single(l => Text(l).StartsWith("pair_uses.name", StringComparison.Ordinal));
        Assert.Equal("pair_uses.name  \"Use\"  p | q  [db.pack]", Text(loc));
        Assert.Equal(owner, loc.Spans[4].Target);
    }

    [Fact]
    public void Section_titles_show_count_and_distinct_sources()
    {
        var target = Id("effects_tables", "e");
        var rows = new[]
        {
            Junction(target, "e", null, From(Vanilla)),
            Junction(target, "e", null, From(Mod)),
            Junction(target, "e", null, From(Vanilla)),
        };
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("building_effects_junction", "building", true, rows)]);

        var lines = ObjectText.Render(view, null, NoExpansion);

        var title = lines.Single(l => Text(l).StartsWith("building_effects_junction", StringComparison.Ordinal));
        Assert.Equal("building_effects_junction  (3) [db.pack] [mod.pack]", Text(title));
        Assert.Equal(0, title.Indent);
        Assert.Equal(0, title.Spans[0].Section);
        Assert.Equal(SpanKind.TableName, title.Spans[0].Kind);
        var blank = lines[lines.ToList().IndexOf(title) - 1];
        Assert.Empty(blank.Spans);
    }

    [Fact]
    public void Junction_rows_render_only_the_other_end()
    {
        var b2 = Id("building_levels_tables", "b2");
        var row = new ObjectRow([Plain("a", "b1"), Plain("b", "b2")], From(Vanilla), null,
            Reference("b", "b2", ReferenceResolution.Found, b2, "Barracks 2"));
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("junction", "a", true, row)]);

        var lines = ObjectText.Render(view, null, NoExpansion);

        var line = lines.Single(l => Text(l).StartsWith("b2", StringComparison.Ordinal));
        Assert.Equal("b2  \"Barracks 2\"  [db.pack]", Text(line));
        Assert.Equal(b2, line.Spans[0].Target);
    }

    [Fact]
    public void A_long_section_is_collapsed_unless_expanded()
    {
        var rows = Enumerable.Range(0, 25).Select(i => Row(From(Vanilla), Plain("building", "b1"), Plain("value", i.ToString()))).ToArray();
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("t", "building", false, rows)]);

        var collapsed = ObjectText.Render(view, null, NoExpansion);

        Assert.Equal(5, collapsed.Count(l => Text(l).StartsWith("value = ", StringComparison.Ordinal)));
        var more = collapsed.Single(l => l.Spans.Any(s => s.Kind == SpanKind.Collapsed));
        Assert.Equal("+20", Text(more));
        Assert.Equal(0, more.Spans[0].Section);
        Assert.Equal(20, more.Spans[0].Count);

        var expanded = ObjectText.Render(view, null, new RenderOptions(new HashSet<int> { 0 }));

        Assert.Equal(25, expanded.Count(l => Text(l).StartsWith("value = ", StringComparison.Ordinal)));
        Assert.DoesNotContain(expanded, l => l.Spans.Any(s => s.Kind == SpanKind.Collapsed));
    }

    [Fact]
    public void A_category_orders_titles_and_hides_sections()
    {
        var target = Id("effects_tables", "e");
        var row = Junction(target, "e", null, From(Vanilla));
        var view = View(Row(From(Vanilla), Plain("key", "b1")),
        [
            Section("cai_x", "building", false, row),
            Section("building_effects_junction", "building", true, row),
            Section("technologies", "building", false, row),
        ]);
        var category = new CategoryDefinition("building_levels_tables", "cat",
        [
            new SectionRule("building_effects_junction", null, "objects.section.effects", SectionMode.List, null, false),
            new SectionRule("cai_x", null, null, SectionMode.List, null, true),
        ]);

        var lines = ObjectText.Render(view, category, NoExpansion);

        var titles = lines.Where(l => l.Spans.Count > 0 && l.Spans[0].Section is not null).ToList();
        Assert.Equal(["objects.section.effects", "technologies"], titles.Select(t => t.Spans[0].Text).ToArray());
        Assert.Equal(SpanKind.Title, titles[0].Spans[0].Kind);
        Assert.Equal(SpanKind.TableName, titles[1].Spans[0].Kind);
        Assert.Equal(1, titles[0].Spans[0].Section);
        Assert.Equal(2, titles[1].Spans[0].Section);
        Assert.DoesNotContain(lines, l => Text(l).StartsWith("cai_x", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_category_sections_are_alphabetical()
    {
        var row = Row(From(Vanilla), Plain("building", "b1"));
        var view = View(Row(From(Vanilla), Plain("key", "b1")),
        [
            Section("technologies", "building", false, row),
            Section("a_tables", "zeta", false, row),
            Section("a_tables", "alpha", false, row),
        ]);

        var lines = ObjectText.Render(view, null, NoExpansion);

        var order = lines.Where(l => l.Spans.Count > 0 && l.Spans[0].Section is not null).Select(l => l.Spans[0].Section!.Value).ToArray();
        Assert.Equal([2, 1, 0], order);
    }

    [Fact]
    public void A_sentence_rule_renders_the_pattern()
    {
        var t1 = Id("technologies_tables", "t1");
        var row = new ObjectRow([Plain("building_level", "b1"), Plain("technology_key", "t1")], From(Vanilla), null,
            Reference("technology_key", "t1", ReferenceResolution.Found, t1, null));
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("reqs", "building_level", true, row)]);
        var rule = new SectionRule("reqs", null, null, SectionMode.Sentence, "requires technology {technology_key}", false);

        var lines = ObjectText.Render(view, new CategoryDefinition("building_levels_tables", "cat", [rule]), NoExpansion);

        var line = lines.Single(l => Text(l).StartsWith("requires", StringComparison.Ordinal));
        Assert.Equal("requires technology t1  [db.pack]", Text(line));
        Assert.Equal([SpanKind.Normal, SpanKind.Link, SpanKind.Normal, SpanKind.ProvenanceVanilla], Kinds(line));
        Assert.Equal(t1, line.Spans[1].Target);

        var unknown = rule with { Pattern = "needs {zzz}" };
        var warned = ObjectText.Render(view, new CategoryDefinition("building_levels_tables", "cat", [unknown]), NoExpansion);
        var warnedLine = warned.Single(l => Text(l).StartsWith("needs", StringComparison.Ordinal));
        Assert.Equal(SpanKind.Warning, warnedLine.Spans[1].Kind);
        Assert.Equal("{zzz}", warnedLine.Spans[1].Text);
    }

    [Fact]
    public void Row_provenance_marks_overridden_duplicate_and_older_layout()
    {
        var provenance = new RowProvenance(Mod, [Vanilla], true, true);
        var view = View(Row(From(Vanilla), Plain("key", "b1")),
            [Section("t", "building", false, Row(provenance, Plain("building", "b1"), Plain("value", "1")))]);

        var lines = ObjectText.Render(view, null, NoExpansion);

        var line = lines.Single(l => Text(l).StartsWith("value = ", StringComparison.Ordinal));
        Assert.Equal("value = 1  [mod.pack] [db.pack] duplicate older layout", Text(line));
        Assert.Contains(line.Spans, s => s.Kind == SpanKind.Overridden && s.Text == "[db.pack]");
        // The kind carries the meaning; the text is only a fallback the App replaces.
        Assert.Equal("duplicate", Assert.Single(line.Spans, s => s.Kind == SpanKind.Duplicate).Text);
        Assert.Equal("older layout", Assert.Single(line.Spans, s => s.Kind == SpanKind.OlderLayout).Text);
        Assert.DoesNotContain(line.Spans, s => s.Kind == SpanKind.Warning);
    }

    [Fact]
    public void Warnings_and_a_missing_root_are_rendered()
    {
        var view = View(null, warnings: [new ObjectWarning(ObjectWarningKind.RootNotFound, null, "no such row")], key: "b9");

        var lines = ObjectText.Render(view, null, NoExpansion);

        Assert.Equal("building_levels  b9", Text(lines[0]));
        Assert.Equal(SpanKind.Warning, lines[1].Spans[0].Kind);
        Assert.Equal("RootNotFound: no such row", Text(lines[1]));
    }

    [Fact]
    public void A_named_warning_includes_the_name()
    {
        var view = View(null, warnings: [new ObjectWarning(ObjectWarningKind.SourceProblem, "mod.pack", "bad")]);

        var lines = ObjectText.Render(view, null, NoExpansion);

        Assert.Equal("SourceProblem: mod.pack: bad", Text(lines[1]));
    }

    [Fact]
    public void Rendering_is_deterministic()
    {
        var target = Id("effects_tables", "e");
        var row = Junction(target, "e", "Effect", From(Vanilla, Mod));
        var view = View(Row(From(Mod, Vanilla), Plain("key", "b1")),
            [Section("b", "building", true, row), Section("a", "building", true, row)],
            [new ObjectWarning(ObjectWarningKind.LoadOrder, null, "x")]);

        var first = ObjectText.Render(view, null, NoExpansion);
        var second = ObjectText.Render(view, null, NoExpansion);

        Assert.True(first.Select(Text).SequenceEqual(second.Select(Text)));
        Assert.True(first.SelectMany(Kinds).SequenceEqual(second.SelectMany(Kinds)));
    }

    [Fact]
    public void A_section_without_rows_is_not_rendered()
    {
        var row = Row(From(Vanilla), Plain("building", "b1"), Plain("value", "1"));
        var view = View(Row(From(Vanilla), Plain("key", "b1")),
        [
            Section("a_empty", "building", false),
            Section("b_full", "building", false, row),
        ]);

        var lines = ObjectText.Render(view, null, NoExpansion);

        Assert.DoesNotContain(lines, l => Text(l).StartsWith("a_empty", StringComparison.Ordinal));
        Assert.Single(lines, l => l.Spans.Count == 0);
        Assert.Equal("b_full  (1) [db.pack]", Text(lines.Single(l => Text(l).StartsWith("b_full", StringComparison.Ordinal))));
        Assert.DoesNotContain(lines, l => l.Spans.Any(s => s.Section == 0));
    }

    [Fact]
    public void A_preview_larger_than_the_threshold_is_clamped()
    {
        var rows = Enumerable.Range(0, 5).Select(i => Row(From(Vanilla), Plain("building", "b1"), Plain("value", i.ToString()))).ToArray();
        var view = View(Row(From(Vanilla), Plain("key", "b1")), [Section("t", "building", false, rows)]);

        var lines = ObjectText.Render(view, null, new RenderOptions(new HashSet<int>(), CollapseThreshold: 3, CollapsedPreview: 10));

        Assert.Equal(3, lines.Count(l => Text(l).StartsWith("value = ", StringComparison.Ordinal)));
        Assert.Equal("+2", Text(lines.Single(l => l.Spans.Any(s => s.Kind == SpanKind.Collapsed))));

        var negative = ObjectText.Render(view, null, new RenderOptions(new HashSet<int>(), CollapseThreshold: 3, CollapsedPreview: -4));

        Assert.Equal(0, negative.Count(l => Text(l).StartsWith("value = ", StringComparison.Ordinal)));
        var hidden = negative.Single(l => l.Spans.Any(s => s.Kind == SpanKind.Collapsed)).Spans[0];
        Assert.Equal("+5", hidden.Text);
        Assert.Equal(5, hidden.Count);
        Assert.Equal(0, hidden.Section);
    }

    [Fact]
    public void The_first_matching_rule_of_a_table_wins()
    {
        var row = Row(From(Vanilla), Plain("from", "b1"), Plain("to", "b1"));
        var view = View(Row(From(Vanilla), Plain("key", "b1")),
        [
            Section("x", "from", false, row),
            Section("x", "to", false, row),
        ]);
        var category = new CategoryDefinition("building_levels_tables", "cat",
        [
            new SectionRule("x", null, "a", SectionMode.List, null, false),
            new SectionRule("x", "to", "b", SectionMode.List, null, false),
        ]);

        var lines = ObjectText.Render(view, category, NoExpansion);

        var titles = lines.Where(l => l.Spans.Count > 0 && l.Spans[0].Section is not null).Select(l => l.Spans[0]).ToList();
        Assert.Equal(["a", "a"], titles.Select(t => t.Text).ToArray());
        Assert.Equal(SpanKind.Title, titles[0].Kind);
    }
}
