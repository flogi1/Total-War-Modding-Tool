using TotalWarModTool.Objects;

namespace TotalWarModTool.App.Tests;

public class SpanBrushesTests
{
    [Theory]
    [InlineData(SpanKind.ProvenanceVanilla, "Theme.ObjectVanilla")]
    [InlineData(SpanKind.Link, "Theme.Accent")]
    [InlineData(SpanKind.FieldName, "Theme.TextMuted")]
    [InlineData(SpanKind.Value, "Theme.Text")]
    [InlineData(SpanKind.LocText, "Theme.ObjectLocText")]
    [InlineData(SpanKind.Warning, "Theme.Warning")]
    public void Every_span_kind_maps_to_a_token(SpanKind kind, string key) => Assert.Equal(key, SpanBrushes.ResourceKeyOf(kind));

    [Fact]
    public void Every_kind_maps_to_a_known_palette_token()
    {
        var keys = ThemePalette.All.Select(t => t.Key).ToHashSet();
        foreach (var kind in Enum.GetValues<SpanKind>())
        {
            var key = SpanBrushes.ResourceKeyOf(kind);
            Assert.StartsWith(ThemePalette.ResourcePrefix, key);
            Assert.Contains(key[ThemePalette.ResourcePrefix.Length..], keys);
        }
    }

    [Fact]
    public void Bold_and_strikethrough_follow_the_kind()
    {
        Assert.True(SpanBrushes.IsBold(SpanKind.TableName));
        Assert.True(SpanBrushes.IsBold(SpanKind.Key));
        Assert.True(SpanBrushes.IsBold(SpanKind.Title));
        Assert.False(SpanBrushes.IsBold(SpanKind.Value));
        Assert.True(SpanBrushes.IsStrikethrough(SpanKind.Overridden));
        Assert.False(SpanBrushes.IsStrikethrough(SpanKind.Warning));
    }
}
