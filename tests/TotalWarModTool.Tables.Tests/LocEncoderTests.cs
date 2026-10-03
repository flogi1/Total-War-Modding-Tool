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

    [Fact] public void Too_long_text_throws() =>
        Assert.Throws<ArgumentException>(() => LocEncoder.Encode([new LocRow("k", new string('a', 65536), false)]));
}
