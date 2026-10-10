using TotalWarModTool.Localization;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App.Tests;

public class TrailingTextTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));

    private static BlockLine Line(params TextSpan[] trailing) => new(BlockLineKind.SectionHead, [new TextSpan("effects", SpanKind.TableName)], trailing, 0);

    private static string Translate(string key) => "T:" + key;

    [Fact]
    public void A_title_is_translated_and_the_gap_comes_first()
    {
        var line = Line(
            new TextSpan("objects.section.effects", SpanKind.Title),
            new TextSpan("  ", SpanKind.Normal),
            new TextSpan("(3)", SpanKind.Normal),
            new TextSpan(" ", SpanKind.Normal),
            new TextSpan("[db.pack]", SpanKind.ProvenanceVanilla));

        var pieces = TrailingText.Of(line, Texts, Translate);

        Assert.Equal(["  ", "T:objects.section.effects", "  ", "(3)", " ", "[db.pack]"], pieces.Select(p => p.Text));
        Assert.Equal(
            [SpanKind.Normal, SpanKind.Title, SpanKind.Normal, SpanKind.Normal, SpanKind.Normal, SpanKind.ProvenanceVanilla],
            pieces.Select(p => p.Kind));
    }

    [Fact]
    public void Duplicate_and_older_layout_use_the_texts_of_the_app()
    {
        var pieces = TrailingText.Of(Line(new TextSpan("duplicate", SpanKind.Duplicate), new TextSpan("older layout", SpanKind.OlderLayout)), Texts, Translate);

        Assert.Equal(["  ", Texts.ObjectsDuplicate, Texts.ObjectsOlderLayout], pieces.Select(p => p.Text));
        Assert.Equal([SpanKind.Normal, SpanKind.Duplicate, SpanKind.OlderLayout], pieces.Select(p => p.Kind));
    }

    [Fact]
    public void An_empty_trailing_gives_no_pieces() => Assert.Empty(TrailingText.Of(Line(), Texts, Translate));

    [Fact]
    public void Control_characters_in_data_are_escaped()
    {
        var pieces = TrailingText.Of(Line(new TextSpan("\"a\nb\"", SpanKind.LocText)), Texts, Translate);

        Assert.DoesNotContain(pieces, p => p.Text.Contains('\n'));
        Assert.Contains(pieces, p => p.Text.Contains("\\u000A", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(SpanKind.Normal, "Theme.ObjectTrailing")]
    [InlineData(SpanKind.Title, "Theme.ObjectTrailing")]
    [InlineData(SpanKind.LocText, "Theme.ObjectTrailing")]
    [InlineData(SpanKind.ProvenanceMod, "Theme.ObjectMod")]
    [InlineData(SpanKind.Warning, "Theme.Warning")]
    [InlineData(SpanKind.Overridden, "Theme.TextMuted")]
    public void Annotations_are_muted_except_provenance_and_warnings(SpanKind kind, string key) => Assert.Equal(key, TrailingText.BrushKeyOf(kind));
}
