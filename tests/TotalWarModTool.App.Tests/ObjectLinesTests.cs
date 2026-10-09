using TotalWarModTool.Localization;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App.Tests;

public class ObjectLinesTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly Dictionary<string, string> Titles = new() { ["title_effects"] = "Effects" };

    private static string Translate(string key) => Titles.TryGetValue(key, out var text) ? text : key;

    private static TextLine Line(int indent, params TextSpan[] spans) => new(spans, indent);

    private static IReadOnlyList<ObjectLine> Build(params TextLine[] lines) => ObjectLines.Build(lines, Texts, Translate);

    [Fact] public void Titles_are_translated_and_keep_their_section()
    {
        var lines = Build(Line(0, new TextSpan("title_effects", SpanKind.Title, Section: 3), new TextSpan("  ", SpanKind.Normal), new TextSpan("(2)", SpanKind.Normal)));
        var title = lines[0].Spans[0];
        Assert.Equal(("Effects", SpanKind.Title, 3), (title.Text, title.Kind, title.Section));
        Assert.Equal("title_other", Build(Line(0, new TextSpan("title_other", SpanKind.Title, Section: 0)))[0].Spans[0].Text);   // no translation: the key
    }

    [Fact] public void Collapsed_duplicate_and_older_layout_get_their_texts()
    {
        var lines = Build(
            Line(1, new TextSpan("+7", SpanKind.Collapsed, Section: 2, Count: 7)),
            Line(1, new TextSpan("duplicate", SpanKind.Duplicate), new TextSpan("older layout", SpanKind.OlderLayout)));
        var collapsed = lines[0].Spans[0];
        Assert.Equal(("+ 7 more rows", SpanKind.Collapsed, 2), (collapsed.Text, collapsed.Kind, collapsed.Section));
        Assert.Equal((Texts.ObjectsDuplicate, SpanKind.Duplicate), (lines[1].Spans[0].Text, lines[1].Spans[0].Kind));
        Assert.Equal((Texts.ObjectsOlderLayout, SpanKind.OlderLayout), (lines[1].Spans[1].Text, lines[1].Spans[1].Kind));
    }

    [Fact] public void A_not_found_value_gets_the_target_unknown_tool_tip()
    {
        var lines = Build(
            Line(1, new TextSpan("effect", SpanKind.FieldName), new TextSpan(" = ", SpanKind.Normal), new TextSpan("missing", SpanKind.Warning)),
            Line(0, new TextSpan("SourceProblem: x_tables: unreadable", SpanKind.Warning)));
        Assert.Equal(Texts.ObjectsTargetUnknown, lines[0].Spans[2].ToolTip);
        Assert.Null(lines[0].Spans[0].ToolTip);
        Assert.Null(lines[1].Spans[0].ToolTip);                          // a warning line of the view is data, no value
    }

    [Fact] public void Other_spans_pass_through_unchanged()
    {
        var target = new ObjectId("effects_tables", ["e1"]);
        var input = Line(2, new TextSpan("name", SpanKind.FieldName), new TextSpan("e1", SpanKind.Link, target), new TextSpan("\"Text\"", SpanKind.LocText),
            new TextSpan("[mod.pack]", SpanKind.ProvenanceMod), new TextSpan("[old.pack]", SpanKind.Overridden), new TextSpan("v", SpanKind.Value));
        var line = Build(input)[0];
        Assert.Equal(2, line.Indent);
        Assert.Equal(input.Spans.Select(s => (s.Text, s.Kind, s.Target)), line.Spans.Select(s => (s.Text, s.Kind, s.Target)));
        Assert.All(line.Spans, span => Assert.Null(span.ToolTip));
        Assert.Empty(Build(Line(0)).Single().Spans);
    }
}
