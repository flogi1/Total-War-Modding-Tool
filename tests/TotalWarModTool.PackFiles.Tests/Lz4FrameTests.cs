namespace TotalWarModTool.PackFiles.Tests;

public class Lz4FrameTests
{
    private static byte[] H(string hex) => PackBuilder.Hex(hex);
    private static string Text(byte[] b) => System.Text.Encoding.ASCII.GetString(b);

    // one block, literals "abc" and an overlapping match of 9 bytes at offset 3
    private const string Simple = "04 22 4D 18 60 40 00 | 06 00 00 00 | 35 61 62 63 03 00 | 00 00 00 00";
    // two dependent blocks, the second block's match reaches into the first; content checksum present
    private const string Dependent = "04 22 4D 18 44 40 00 | 04 00 00 00 | 30 61 62 63 | 03 00 00 00 | 02 03 00 | 00 00 00 00 | DE AD BE EF";
    // extended literal length (15 + 5 = 20), then an uncompressed block "xyz"
    private const string LongLiteralsAndRawBlock = "04 22 4D 18 60 70 00 | 16 00 00 00 | F0 05 30 31 32 33 34 35 36 37 38 39 41 42 43 44 45 46 47 48 49 4A | 03 00 00 80 | 78 79 7A | 00 00 00 00";

    [Theory]
    [InlineData(Simple, "abcabcabcabc")]
    [InlineData(Dependent, "abcabcabc")]
    [InlineData(LongLiteralsAndRawBlock, "0123456789ABCDEFGHIJxyz")]
    public void Decodes_valid_frames(string frame, string expected)
    {
        Assert.True(Lz4Frame.TryDecode(H(frame), expected.Length, out var data, out var error), error);
        Assert.Equal(expected, Text(data));
    }

    [Theory]
    [InlineData("28 B5 2F FD 60 40 00 | 00 00 00 00", 0)]                                     // wrong magic
    [InlineData("04 22 4D 18 A0 40 00 | 00 00 00 00", 0)]                                     // version 2
    [InlineData("04 22 4D 18 60 40 00 | 03 00 00 00 | 00 00 00 | 00 00 00 00", 4)]            // offset 0
    [InlineData("04 22 4D 18 60 40 00 | 04 00 00 00 | 10 61 05 00 | 00 00 00 00", 5)]         // offset beyond the output
    [InlineData("04 22 4D 18 60 40 00 | 06 00 00 00 | 35 61 62", 12)]                         // cut inside a block
    [InlineData("04 22 4D 18 60 40 00 | 06 00 00 00 | 35 61 62 63 03 00", 12)]                // end mark missing
    [InlineData("04 22 4D 18 60 40 00 | FF FF FF 7F | 35 61 62 63 03 00 | 00 00 00 00", 12)]  // block size beyond the frame
    [InlineData(Simple, 13)]                                                                  // produces less than expected
    [InlineData(Simple, 11)]                                                                  // produces more than expected
    [InlineData(Simple + " | 00", 12)]                                                        // bytes after the frame
    public void Rejects_malformed_frames(string frame, int expectedSize)
    {
        Assert.False(Lz4Frame.TryDecode(H(frame), expectedSize, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
