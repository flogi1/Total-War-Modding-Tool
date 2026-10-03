namespace TotalWarModTool.Tables.Tests;

public class LocEncoderTests
{
    [Fact] public void Encodes_rows_like_the_decoder_reads_them()
    {
        LocRow[] rows = [new("key_ü", "Text 日本 😀\twith tab", true), new("", "", false)];
        var decoded = LocDecoder.Decode(LocEncoder.Encode(rows));
        Assert.Equal(LocDecodeStatus.Ok, decoded.Status);
        Assert.Equal(rows, decoded.Rows);
    }

    [Fact] public void Writes_the_exact_bytes()
    {
        var expected = new TableBytes().Raw(0xFF, 0xFE, 0x4C, 0x4F, 0x43, 0x00).I32(1).Raw(1, 0, 0, 0).Str16("k").Str16("t").Bool(true).Build();
        Assert.Equal(expected, LocEncoder.Encode([new LocRow("k", "t", true)]));
    }

    // Built from char values: a lone surrogate in an attribute argument or literal would be turned into U+FFFD by the compiler.
    private const char High = (char)0xD800;
    private const char Low = (char)0xDC00;
    private static readonly string[] Unpaired = ["a" + High + "b", "a" + High, "a" + Low + "b", "" + Low + High, "" + High + High + Low];

    [Fact] public void Unpaired_surrogates_in_key_or_text_throw()
    {
        foreach (var text in Unpaired)
        {
            Assert.Throws<ArgumentException>(() => LocEncoder.Encode([new LocRow(text, "t", false)]));
            Assert.Throws<ArgumentException>(() => LocEncoder.Encode([new LocRow("k", text, false)]));
        }
    }

    [Fact] public void A_valid_surrogate_pair_still_encodes() =>
        Assert.Equal(LocDecodeStatus.Ok, LocDecoder.Decode(LocEncoder.Encode([new LocRow("😀", "😀", false)])).Status);

    [Fact] public void Too_long_text_throws() =>
        Assert.Throws<ArgumentException>(() => LocEncoder.Encode([new LocRow("k", new string('a', 65536), false)]));
}
