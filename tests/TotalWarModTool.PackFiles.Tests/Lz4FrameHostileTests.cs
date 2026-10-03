namespace TotalWarModTool.PackFiles.Tests;

public class Lz4FrameHostileTests
{
    private static byte[] H(string hex) => PackBuilder.Hex(hex);
    private static string Text(byte[] b) => System.Text.Encoding.ASCII.GetString(b);

    private const string Block = "06 00 00 00 | 35 61 62 63 03 00";   // literals "abc", 9-byte match at offset 3

    [Theory]
    // block checksum flag (FLG bit 4) with its 4 bytes present
    [InlineData("04 22 4D 18 70 40 00 | " + Block + " | AA BB CC DD | 00 00 00 00")]
    // content size (8 bytes) and dictionary id (4 bytes) present in the header
    [InlineData("04 22 4D 18 69 40 | 0C 00 00 00 00 00 00 00 | 11 22 33 44 | 00 | " + Block + " | 00 00 00 00")]
    public void Decodes_frames_with_optional_fields(string frame)
    {
        Assert.True(Lz4Frame.TryDecode(H(frame), 12, out var data, out var error), error);
        Assert.Equal("abcabcabcabc", Text(data));
    }

    [Theory]
    // extended match length (15 + 255 + 255 + 0 + 4) overruns the expected size
    [InlineData("04 22 4D 18 60 40 00 | 07 00 00 00 | 1F 61 01 00 FF FF 00 | 00 00 00 00", 10)]
    // uncompressed block larger than the remaining output
    [InlineData("04 22 4D 18 60 40 00 | 03 00 00 80 | 78 79 7A | 00 00 00 00", 2)]
    // header cut inside the content size
    [InlineData("04 22 4D 18 68 40 00 | 01 02", 0)]
    // header cut inside the dictionary id
    [InlineData("04 22 4D 18 61 40 00 | 01", 0)]
    // block checksum flag set but the checksum is missing
    [InlineData("04 22 4D 18 70 40 00 | " + Block + " | 00 00 00 00", 12)]
    // content checksum flag set but the checksum is missing
    [InlineData("04 22 4D 18 64 40 00 | " + Block + " | 00 00 00 00", 12)]
    public void Rejects_hostile_frames(string frame, int expectedSize)
    {
        Assert.False(Lz4Frame.TryDecode(H(frame), expectedSize, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact] public void A_tiny_frame_cannot_claim_a_huge_output()
    {
        var frame = H("04 22 4D 18 60 40 00 | " + Block + " | 00 00 00 00");
        Assert.False(Lz4Frame.TryDecode(frame, int.MaxValue, out var data, out var error));
        Assert.Empty(data);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact] public void A_negative_expected_size_is_rejected()
    {
        Assert.False(Lz4Frame.TryDecode(H("04 22 4D 18 60 40 00 | 00 00 00 00"), -1, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
