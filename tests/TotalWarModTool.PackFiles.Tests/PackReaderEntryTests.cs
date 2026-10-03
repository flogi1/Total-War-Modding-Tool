namespace TotalWarModTool.PackFiles.Tests;

public class PackReaderEntryTests
{
    private static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    private static byte[] Zstd(byte[] data) { using var c = new ZstdSharp.Compressor(); return c.Wrap(data).ToArray(); }
    private const string Lz4Simple = "04 22 4D 18 60 40 00 | 06 00 00 00 | 35 61 62 63 03 00 | 00 00 00 00";

    private static (PackIndex Index, PackEntry Entry) One(TempDir dir, byte[] stored, bool compressed, uint flags = 0)
    {
        var path = new PackBuilder().WithFlags(flags).AddEntry("a/file.txt", stored, compressed).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        return (index, index.Entries[0]);
    }

    [Fact] public void Reads_an_uncompressed_entry()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, B("hello"), compressed: false);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal((EntryProblem.None, CompressionFormat.None, 5L), (c.Problem, c.Format, c.UncompressedSize));
        Assert.Equal(B("hello"), c.Bytes);
    }

    [Fact] public void Reads_the_second_of_two_entries()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().AddEntry("one", B("first")).AddEntry("two", B("second")).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        Assert.Equal(B("second"), PackReader.ReadEntry(index, index.Find("two")!, CancellationToken.None).Bytes);
    }

    [Fact] public void Decompresses_zstd()
    {
        using var dir = new TempDir();
        var data = B(string.Concat(Enumerable.Repeat("total war ", 500)));
        var (index, entry) = One(dir, PackBuilder.Stored(Zstd(data), data.Length), compressed: true);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal((EntryProblem.None, CompressionFormat.Zstd, (long)data.Length), (c.Problem, c.Format, c.UncompressedSize));
        Assert.Equal(data, c.Bytes);
    }

    [Fact] public void Decompresses_lz4()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, PackBuilder.Stored(PackBuilder.Hex(Lz4Simple), 12), compressed: true);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal((EntryProblem.None, CompressionFormat.Lz4), (c.Problem, c.Format));
        Assert.Equal(B("abcabcabcabc"), c.Bytes);
    }

    [Theory]
    [InlineData("5D 00 00 80 00 FF FF FF FF", CompressionFormat.Lzma)]
    [InlineData("01 02 03 04 05 06 07 08 09", CompressionFormat.Unknown)]
    public void Other_compression_is_unsupported(string frame, CompressionFormat format)
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, PackBuilder.Stored(PackBuilder.Hex(frame), 100), compressed: true);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal((EntryProblem.UnsupportedCompression, format, 100L), (c.Problem, c.Format, c.UncompressedSize));
        Assert.Null(c.Bytes);
    }

    [Theory]
    [InlineData(13)]   // prefix claims more than the frame yields
    [InlineData(11)]   // prefix claims less
    public void Wrong_size_prefix_is_corrupt(int claimed)
    {
        using var dir = new TempDir();
        var (lz4Index, lz4Entry) = One(dir, PackBuilder.Stored(PackBuilder.Hex(Lz4Simple), claimed), compressed: true);
        Assert.Equal(EntryProblem.Corrupt, PackReader.ReadEntry(lz4Index, lz4Entry, CancellationToken.None).Problem);
        using var dir2 = new TempDir();
        var (zIndex, zEntry) = One(dir2, PackBuilder.Stored(Zstd(B("abcabcabcabc")), claimed), compressed: true);
        Assert.Equal(EntryProblem.Corrupt, PackReader.ReadEntry(zIndex, zEntry, CancellationToken.None).Problem);
    }

    [Fact] public void Damaged_zstd_frame_is_corrupt()
    {
        using var dir = new TempDir();
        var frame = Zstd(B(string.Concat(Enumerable.Repeat("total war ", 500))));
        var (index, entry) = One(dir, PackBuilder.Stored(frame[..(frame.Length / 2)], 5000), compressed: true);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal(EntryProblem.Corrupt, c.Problem); Assert.Null(c.Bytes);
    }

    [Theory]
    [InlineData(0, EntryProblem.None)]
    [InlineData(5, EntryProblem.Corrupt)]
    public void Tiny_compressed_entries(int storedLength, EntryProblem expected)
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, new byte[storedLength], compressed: true);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal(expected, c.Problem);
        if (expected == EntryProblem.None) Assert.Empty(c.Bytes!);
    }

    [Fact] public void Size_prefix_above_the_limit_is_too_large()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, PackBuilder.Stored(PackBuilder.Hex(Lz4Simple), int.MaxValue), compressed: true);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal((EntryProblem.TooLarge, (long)int.MaxValue), (c.Problem, c.UncompressedSize));
        using var dir2 = new TempDir();
        var (plainIndex, plainEntry) = One(dir2, B("hello"), compressed: false);
        Assert.Equal(EntryProblem.TooLarge, PackReader.ReadEntry(plainIndex, plainEntry, CancellationToken.None, maxBytes: 4).Problem);
    }

    [Fact] public void Encrypted_data_is_reported()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, B("hello"), compressed: false, flags: 0x10);
        Assert.Equal(EntryProblem.Encrypted, PackReader.ReadEntry(index, entry, CancellationToken.None).Problem);
    }

    [Fact] public void Changed_pack_is_reported()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, B("hello"), compressed: false);
        new PackBuilder().AddEntry("a/file.txt", B("hello!!")).WriteTo(dir.File("a.pack"));     // same path, other length
        Assert.Equal(EntryProblem.PackChanged, PackReader.ReadEntry(index, entry, CancellationToken.None).Problem);
        File.Delete(dir.File("a.pack"));
        Assert.Equal(EntryProblem.PackChanged, PackReader.ReadEntry(index, entry, CancellationToken.None).Problem);
    }

    [Fact] public void Cancelled_token_throws()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, B("hello"), compressed: false);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PackReader.ReadEntry(index, entry, cts.Token));
    }
}
