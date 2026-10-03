namespace TotalWarModTool.Workspace.Tests;

public class EntryTextTests
{
    [Theory]
    [InlineData("68 C3 A4", "hä", "UTF-8")]
    [InlineData("EF BB BF 68 C3 A4", "hä", "UTF-8")]
    [InlineData("FF FE 68 00 E4 00", "hä", "UTF-16 LE")]
    [InlineData("FE FF 00 68 00 E4", "hä", "UTF-16 BE")]
    [InlineData("", "", "UTF-8")]
    public void Decodes_by_bom(string hex, string expected, string encoding)
    {
        var bytes = PackBuilder.Hex(hex);
        Assert.Equal(new TextContent(expected, encoding, false, bytes.Length), EntryText.Decode(bytes));
    }

    [Fact] public void Invalid_bytes_become_the_replacement_character() =>
        Assert.Equal("a�b", EntryText.Decode(PackBuilder.Hex("61 FF 62")).Text);

    [Theory]
    [InlineData("61 C3 A4 62", 2, "a")]          // cut would split the two-byte character
    [InlineData("61 C3 A4 62", 3, "aä")]
    [InlineData("F0 9F 98 80 61", 3, "")]        // cut inside a four-byte character
    [InlineData("FF FE 61 00 62 00", 5, "a")]    // odd byte count in UTF-16
    [InlineData("FF FE 3D D8 00 DE", 4, "")]     // cut after a high surrogate
    public void Truncation_never_splits_a_character(string hex, int maxBytes, string expected)
    {
        var bytes = PackBuilder.Hex(hex);
        var text = EntryText.Decode(bytes, maxBytes);
        Assert.Equal((expected, true, (long)bytes.Length), (text.Text, text.Truncated, text.TotalBytes));
    }
}
