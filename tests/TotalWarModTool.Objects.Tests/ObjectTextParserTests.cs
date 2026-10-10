using static TotalWarModTool.Objects.Tests.BlockFixture;

namespace TotalWarModTool.Objects.Tests;

public class ObjectTextParserTests
{
    private static ParsedObject Parse(string text, ObjectView? baseline = null) =>
        ObjectTextParser.Parse(text, baseline ?? Full(), Category, Graph);

    private static string[] Errors(ParsedObject parsed) =>
        parsed.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Kind + "@" + d.Line).ToArray();

    private static BlockDiagnostic Single(ParsedObject parsed, DiagnosticKind kind) => Assert.Single(parsed.Diagnostics, d => d.Kind == kind);

    /// <summary>A root with all fields, then the given lines.</summary>
    private static string WithRoot(params string[] lines) => Lines([.. HeadAndRoot(), .. lines]);

    [Fact]
    public void The_rendered_text_parses_without_diagnostics()
    {
        var view = Full();

        var parsed = Parse(ObjectText.RenderBlock(view, Category).Text, view);

        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(view.Id, parsed.Id);
        Assert.Equal(4, parsed.Root.Count);
        Assert.Equal("Barracks", Assert.Single(parsed.Texts).Text);
        Assert.Equal(["units_tables", "effects_tables", "upgrades_tables"], parsed.Sections.Select(s => s.SourceTable).ToArray());
        Assert.Equal(["building", "building", "from"], parsed.Sections.Select(s => s.FieldName).ToArray());
        Assert.All(parsed.Sections, s => Assert.Single(s.Rows));
        Assert.Equal("More gold", Assert.Single(parsed.Sections[1].Rows[0].Texts).Text);
        Assert.Equal(["unit", "xp"], parsed.Sections[0].Rows[0].Fields.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void A_changed_head_is_an_error_and_the_rest_is_still_parsed()
    {
        var parsed = Parse(Lines("building_levels b2", "  create_cost = 5"));

        var head = Single(parsed, DiagnosticKind.HeadChanged);
        Assert.Equal(1, head.Line);
        Assert.Equal(DiagnosticSeverity.Error, head.Severity);
        Assert.Equal("create_cost", Assert.Single(parsed.Root).Name);
    }

    [Fact]
    public void An_empty_text_has_a_changed_head_at_line_one()
    {
        var parsed = Parse("");

        Assert.Equal(1, Single(parsed, DiagnosticKind.HeadChanged).Line);
    }

    [Fact]
    public void Indentation_accepts_tabs_and_odd_spaces()
    {
        var parsed = Parse(Lines("building_levels b1", "\tcreate_cost = 5", "   note = x"));

        Assert.Equal(["create_cost", "note"], parsed.Root.Select(f => f.Name).ToArray());
        Assert.Equal(["5", "x"], parsed.Root.Select(f => f.Text).ToArray());
        Assert.DoesNotContain(parsed.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void A_field_value_keeps_inner_spacing_and_drops_trailing_spaces()
    {
        var parsed = Parse(Lines("building_levels b1", "  note = a = b  c   "));

        Assert.Equal("a = b  c", Assert.Single(parsed.Root).Text);
    }

    [Theory]
    [InlineData("  note =", "")]
    [InlineData("  note = ", "")]
    [InlineData("  note=x", "x")]
    [InlineData("  note =  x", " x")]
    public void A_value_starts_after_one_space_behind_the_equals_sign(string line, string expected)
    {
        var parsed = Parse(Lines("building_levels b1", line));

        Assert.Equal(expected, Assert.Single(parsed.Root).Text);
    }

    [Fact]
    public void An_unknown_field_is_an_error_with_its_line()
    {
        var parsed = Parse(Lines("building_levels b1", "  create_cost = 1", "  colour = red"));

        var diagnostic = Single(parsed, DiagnosticKind.UnknownField);
        Assert.Equal(3, diagnostic.Line);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void A_field_name_is_matched_without_case_and_keeps_the_schema_spelling()
    {
        var parsed = Parse(Lines("BUILDING_LEVELS b1", "  Create_Cost = 5"));

        Assert.DoesNotContain(parsed.Diagnostics, d => d.Kind != DiagnosticKind.RootFieldMissing);
        Assert.Equal("create_cost", Assert.Single(parsed.Root).Name);
    }

    [Fact]
    public void A_quoted_unknown_name_is_a_loc_line()
    {
        var parsed = Parse(Lines("building_levels b1", "  onscreen_name = \"Barracks\""));

        var text = Assert.Single(parsed.Texts);
        Assert.Equal(("onscreen_name", "Barracks", 2), (text.Column, text.Text, text.Line));
        Assert.Empty(parsed.Root);
    }

    [Theory]
    [InlineData("He said \"hi\" \\ x")]
    [InlineData("line1\nline2")]
    [InlineData("back\\nslash")]
    [InlineData("plain")]
    public void A_loc_text_with_quotes_and_backslashes_round_trips(string original)
    {
        var parsed = Parse(Lines("building_levels b1", "  short_name = " + ObjectText.QuoteText(original)));

        Assert.Equal(original, Assert.Single(parsed.Texts).Text);
        Assert.DoesNotContain(parsed.Diagnostics, d => d.Kind != DiagnosticKind.RootFieldMissing);
    }

    [Fact]
    public void An_empty_loc_text_is_a_warning()
    {
        var parsed = Parse(Lines("building_levels b1", "  short_name = \"\""));

        var diagnostic = Single(parsed, DiagnosticKind.EmptyText);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("", Assert.Single(parsed.Texts).Text);
    }

    [Theory]
    [InlineData("  name = \"abc")]
    [InlineData("  name = \"")]
    [InlineData("  name = \"a\"b\"")]
    public void An_unterminated_text_is_an_error(string line)
    {
        var parsed = Parse(Lines("building_levels b1", line));

        Assert.Equal(2, Single(parsed, DiagnosticKind.UnterminatedText).Line);
    }

    [Fact]
    public void An_unknown_name_without_quotes_is_an_error_not_a_text()
    {
        var parsed = Parse(Lines("building_levels b1", "  name = abc"));

        Assert.Equal(2, Single(parsed, DiagnosticKind.UnknownField).Line);
        Assert.Empty(parsed.Texts);
    }

    [Fact]
    public void A_value_with_a_tab_is_an_error()
    {
        var parsed = Parse(Lines("building_levels b1", "  note = a\\tb"));

        Assert.Equal(2, Single(parsed, DiagnosticKind.ControlCharacter).Line);
        Assert.Equal("a\\tb", Assert.Single(parsed.Root).Text);
    }

    [Fact]
    public void A_section_head_resolves_to_table_and_field()
    {
        var parsed = Parse(WithRoot("", "effects", "  effect = e2", "", "upgrades.to", "  t1", "", "UNITS_TABLES", "  allows u9"));

        Assert.Equal(("effects_tables", "building"), (parsed.Sections[0].SourceTable, parsed.Sections[0].FieldName));
        Assert.Equal(7, parsed.Sections[0].Line);
        Assert.Equal(("upgrades_tables", "to"), (parsed.Sections[1].SourceTable, parsed.Sections[1].FieldName));
        Assert.Equal(("units_tables", "building"), (parsed.Sections[2].SourceTable, parsed.Sections[2].FieldName));
        Assert.Empty(parsed.Diagnostics);
    }

    [Fact]
    public void A_section_of_a_table_with_two_references_needs_the_field()
    {
        var parsed = Parse(WithRoot("", "upgrades", "  t1"));

        var diagnostic = Single(parsed, DiagnosticKind.UnknownSection);
        Assert.Equal(7, diagnostic.Line);
        Assert.Contains("field required", diagnostic.Detail, StringComparison.Ordinal);
        Assert.Empty(parsed.Sections);
    }

    [Fact]
    public void An_unknown_or_hidden_section_is_an_error_and_its_lines_are_skipped()
    {
        var parsed = Parse(WithRoot("", "cultures", "  culture = x", "    colour = red", "", "cai_x", "  key = k1", "", "effects.nothing", "  effect = e2"));

        Assert.Equal(["UnknownSection@7", "HiddenSection@11", "UnknownSection@14"], Errors(parsed));
        Assert.Empty(parsed.Sections);
    }

    [Fact]
    public void A_section_that_the_baseline_does_not_have_is_allowed()
    {
        var baseline = View(RootRow());

        var parsed = Parse(WithRoot("", "effects", "  effect = e2", "    value = 3"), baseline);

        Assert.Empty(parsed.Diagnostics);
        var row = Assert.Single(Assert.Single(parsed.Sections).Rows);
        Assert.Equal(["effect", "value"], row.Fields.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void A_repeated_section_is_an_error()
    {
        var parsed = Parse(WithRoot("", "effects", "  effect = e2", "", "effects", "  effect = e3"));

        var diagnostic = Single(parsed, DiagnosticKind.UnknownSection);
        Assert.Equal(10, diagnostic.Line);
        Assert.Contains("repeated", diagnostic.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_row_head_splits_at_double_spaces_before_field_names()
    {
        var parsed = Parse(WithRoot("", "effects", "  effect = e1  scope = x = y"));

        var row = Assert.Single(Assert.Single(parsed.Sections).Rows);
        Assert.Equal([("effect", "e1"), ("scope", "x = y")], row.Fields.Select(f => (f.Name, f.Text)).ToArray());
        Assert.Equal(8, row.Line);
        Assert.Equal([8, 8], row.Fields.Select(f => f.Line).ToArray());
    }

    [Fact]
    public void A_double_space_inside_a_value_is_not_a_split_unless_a_field_follows()
    {
        var parsed = Parse(WithRoot("", "effects", "  effect = e1  with  gap", "    scope = a  value = b"));

        var row = Assert.Single(Assert.Single(parsed.Sections).Rows);
        Assert.Equal([("effect", "e1  with  gap"), ("scope", "a  value = b")], row.Fields.Select(f => (f.Name, f.Text)).ToArray());
    }

    [Fact]
    public void An_empty_head_value_followed_by_another_field_parses()
    {
        var parsed = Parse(WithRoot("", "effects", "  scope =   effect = e1"));

        var row = Assert.Single(Assert.Single(parsed.Sections).Rows);
        Assert.Equal([("scope", ""), ("effect", "e1")], row.Fields.Select(f => (f.Name, f.Text)).ToArray());
    }

    [Fact]
    public void Field_lines_belong_to_the_last_row_and_repeats_are_errors()
    {
        var parsed = Parse(WithRoot("", "effects", "  effect = e1", "    value = 1", "    value = 2"));

        Assert.Equal(["FieldRepeated@10"], Errors(parsed));
        Assert.Equal(["effect", "value"], Assert.Single(Assert.Single(parsed.Sections).Rows).Fields.Select(f => f.Name).Distinct().ToArray());
    }

    [Fact]
    public void A_field_repeated_between_head_and_field_line_is_an_error()
    {
        var parsed = Parse(WithRoot("", "effects", "  effect = e1  scope = a", "    scope = b"));

        Assert.Equal(["FieldRepeated@9"], Errors(parsed));
    }

    [Fact]
    public void A_field_line_without_a_row_is_an_error()
    {
        var parsed = Parse(WithRoot("", "effects", "    value = 1"));

        Assert.Equal(["FieldWithoutRow@8"], Errors(parsed));
    }

    [Fact]
    public void The_root_pointing_field_is_not_accepted_as_a_field_line()
    {
        var parsed = Parse(WithRoot("", "effects", "  effect = e1", "    building = b1"));

        var diagnostic = Single(parsed, DiagnosticKind.UnknownField);
        Assert.Equal(9, diagnostic.Line);
        Assert.Contains("root", diagnostic.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_loc_line_below_a_row_belongs_to_the_row()
    {
        var parsed = Parse(WithRoot("", "effects", "  effect = e1", "    description = \"More gold\""));

        var row = Assert.Single(Assert.Single(parsed.Sections).Rows);
        Assert.Equal(("description", "More gold", 9), (row.Texts[0].Column, row.Texts[0].Text, row.Texts[0].Line));
        Assert.Empty(parsed.Diagnostics);
    }

    [Fact]
    public void A_sentence_row_is_matched_against_the_pattern()
    {
        var good = Parse(WithRoot("", "units", "  allows u1"));
        var bad = Parse(WithRoot("", "units", "  forbids u1"));
        var extra = Parse(WithRoot("", "units", "  allows u1", "    xp = 2"));

        Assert.Equal([("unit", "u1")], Assert.Single(Assert.Single(good.Sections).Rows).Fields.Select(f => (f.Name, f.Text)).ToArray());
        Assert.Empty(good.Diagnostics);
        Assert.Equal(["SentenceMismatch@8"], Errors(bad));
        Assert.Equal([("unit", "u1"), ("xp", "2")], Assert.Single(Assert.Single(extra.Sections).Rows).Fields.Select(f => (f.Name, f.Text)).ToArray());
    }

    [Fact]
    public void A_sentence_with_an_empty_value_matches()
    {
        var parsed = Parse(WithRoot("", "units", "  allows "));

        Assert.Equal([("unit", "")], Assert.Single(Assert.Single(parsed.Sections).Rows).Fields.Select(f => (f.Name, f.Text)).ToArray());
        Assert.Empty(parsed.Diagnostics);
    }

    [Fact]
    public void A_pattern_with_text_after_the_last_placeholder_is_matched()
    {
        var category = new CategoryDefinition(Root, "cat", [new SectionRule("units_tables", null, null, SectionMode.Sentence, "{unit} costs {xp} gold", false)]);

        var parsed = ObjectTextParser.Parse(WithRoot("", "units", "  u1 costs 5 gold", "  u2 costs 6"), Full(), category, Graph);

        Assert.Equal(["SentenceMismatch@9"], Errors(parsed));
        Assert.Equal([("unit", "u1"), ("xp", "5")], parsed.Sections[0].Rows[0].Fields.Select(f => (f.Name, f.Text)).ToArray());
    }

    [Fact]
    public void A_junction_row_takes_the_whole_line_as_the_other_end()
    {
        var parsed = Parse(WithRoot("", "upgrades.from", "  t1  with  spaces"));

        var row = Assert.Single(Assert.Single(parsed.Sections).Rows);
        Assert.Equal([("to", "t1  with  spaces")], row.Fields.Select(f => (f.Name, f.Text)).ToArray());
        Assert.Empty(parsed.Diagnostics);
    }

    [Fact]
    public void Missing_root_fields_are_warnings()
    {
        var parsed = Parse(Lines("building_levels b1", "  level_name = b1", "  chain = chain1", "  create_cost = 100"));

        var diagnostic = Single(parsed, DiagnosticKind.RootFieldMissing);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Line);
        Assert.Contains("note", diagnostic.Detail, StringComparison.Ordinal);
        Assert.False(parsed.HasErrors);
    }

    [Fact]
    public void Lines_with_three_levels_are_errors()
    {
        var parsed = Parse(WithRoot("", "effects", "  effect = e1", "    value = 1", "      x = 1"));

        Assert.Equal(["UnknownLineForm@10"], Errors(parsed));
    }

    [Fact]
    public void A_line_without_an_equals_sign_in_a_list_section_is_an_error()
    {
        var parsed = Parse(WithRoot("", "effects", "  just text"));

        Assert.Equal(["UnknownLineForm@8"], Errors(parsed));
    }

    [Fact]
    public void Every_problem_is_collected()
    {
        var parsed = Parse(Lines("building_levels b2", "  create_cost = 1", "  colour = red", "", "cultures", "  x = 1"));

        Assert.Equal(["HeadChanged@1", "UnknownField@3", "UnknownSection@5"], Errors(parsed));
        var lines = parsed.Diagnostics.Select(d => d.Line).ToArray();
        Assert.Equal(lines.OrderBy(l => l).ToArray(), lines);
    }

    [Fact]
    public void Blank_lines_and_windows_line_ends_are_ignored()
    {
        var parsed = Parse("building_levels b1\r\n\r\n   \r\n  note = x\r\n\r\n");

        Assert.Equal("x", Assert.Single(parsed.Root).Text);
        Assert.DoesNotContain(parsed.Diagnostics, d => d.Kind != DiagnosticKind.RootFieldMissing);
    }
}
