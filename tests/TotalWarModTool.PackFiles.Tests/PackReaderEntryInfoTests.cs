namespace TotalWarModTool.PackFiles.Tests;

public class PackReaderEntryInfoTests
{
    private static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    private const string Lz4Simple = "04 22 4D 18 60 40 00 | 06 00 00 00 | 35 61 62 63 03 00 | 00 00 00 00";

    private static (PackIndex Index, PackEntry Entry) One(TempDir dir, byte[] stored, bool compressed, uint flags = 0)
    {
        var path = new PackBuilder().WithFlags(flags).AddEntry("a/file.bin", stored, compressed).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        return (index, index.Entries[0]);
    }

    [Fact] public void Uncompressed_entry_reports_its_stored_size()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, B("hello"), compressed: false);
        Assert.Equal(new EntryInfo(CompressionFormat.None, 5, EntryProblem.None, null), PackReader.ReadEntryInfo(index, entry, CancellationToken.None));
    }

    [Theory]
    [InlineData(Lz4Simple, CompressionFormat.Lz4)]
    [InlineData("28 B5 2F FD 00 00 00 00 00", CompressionFormat.Zstd)]
    [InlineData("5D 00 00 80 00 FF FF FF FF", CompressionFormat.Lzma)]
    [InlineData("01 02 03 04 05 06 07 08 09", CompressionFormat.Unknown)]
    public void Compressed_entry_reports_format_and_claimed_size_without_a_problem(string frame, CompressionFormat format)
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, PackBuilder.Stored(PackBuilder.Hex(frame), 1234), compressed: true);
        var info = PackReader.ReadEntryInfo(index, entry, CancellationToken.None);
        Assert.Equal((format, 1234L, EntryProblem.None), (info.Format, info.UncompressedSize, info.Problem));
    }

    [Theory]
    [InlineData(0, EntryProblem.None)]
    [InlineData(5, EntryProblem.Corrupt)]
    public void Tiny_compressed_entries(int storedLength, EntryProblem expected)
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, new byte[storedLength], compressed: true);
        Assert.Equal(expected, PackReader.ReadEntryInfo(index, entry, CancellationToken.None).Problem);
    }

    [Fact] public void Changed_pack_and_encrypted_data_are_reported()
    {
        using var dir = new TempDir();
        var (encIndex, encEntry) = One(dir, B("hello"), compressed: false, flags: 0x10);
        Assert.Equal(EntryProblem.Encrypted, PackReader.ReadEntryInfo(encIndex, encEntry, CancellationToken.None).Problem);
        using var dir2 = new TempDir();
        var (index, entry) = One(dir2, B("hello"), compressed: false);
        new PackBuilder().AddEntry("a/file.bin", B("hello!!")).WriteTo(dir2.File("a.pack"));
        Assert.Equal(EntryProblem.PackChanged, PackReader.ReadEntryInfo(index, entry, CancellationToken.None).Problem);
    }

    [Fact] public void Cancelled_token_throws()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, B("hello"), compressed: false);
        Assert.Throws<OperationCanceledException>(() => PackReader.ReadEntryInfo(index, entry, new CancellationToken(true)));
    }
}
