using TotalWarModTool.Objects;

namespace TotalWarModTool.App.Tests;

public class LineSyntaxTests
{
    private static string Joined(IReadOnlyList<TextSpan> spans) => string.Concat(spans.Select(s => s.Text));

    [Fact]
    public void A_field_line_splits_into_name_assignment_and_value()
    {
        var spans = LineSyntax.Classify("  create_cost = 100", false);

        Assert.Equal([SpanKind.Normal, SpanKind.FieldName, SpanKind.Normal, SpanKind.Value], spans.Select(s => s.Kind));
        Assert.Equal(["  ", "create_cost", " = ", "100"], spans.Select(s => s.Text));
        Assert.Equal("  create_cost = 100", Joined(spans));
    }

    [Fact]
    public void A_quoted_value_is_a_loc_text()
    {
        var spans = LineSyntax.Classify("    name = \"Kaserne\"", false);

        Assert.Equal(SpanKind.LocText, spans[^1].Kind);
        Assert.Equal("\"Kaserne\"", spans[^1].Text);
    }

    [Fact]
    public void A_head_line_has_table_and_key()
    {
        var spans = LineSyntax.Classify("building_levels b1", true);

        Assert.Equal([SpanKind.TableName, SpanKind.Normal, SpanKind.Key], spans.Select(s => s.Kind));
        Assert.Equal(["building_levels", " ", "b1"], spans.Select(s => s.Text));
    }

    [Fact]
    public void A_section_head_and_a_sentence_row_are_told_by_indent()
    {
        Assert.Equal([SpanKind.TableName], LineSyntax.Classify("effects", false).Select(s => s.Kind));

        var sentence = LineSyntax.Classify("  allows u1", false);
        Assert.Equal([SpanKind.Normal, SpanKind.Value], sentence.Select(s => s.Kind));
        Assert.Equal(["  ", "allows u1"], sentence.Select(s => s.Text));

        Assert.Empty(LineSyntax.Classify("", false));

        var empty = LineSyntax.Classify("  note = ", false);
        Assert.Equal([SpanKind.Normal, SpanKind.FieldName, SpanKind.Normal], empty.Select(s => s.Kind));
        Assert.Equal("  note = ", Joined(empty));
    }

    [Fact]
    public void Spans_always_cover_the_line_completely()
    {
        foreach (var text in new[] { "\tx = 1", "a", "  ", "k =", "    a b c", "  x = \"q = r\"" })
        {
            Assert.Equal(text, Joined(LineSyntax.Classify(text, false)));
        }
    }
}
