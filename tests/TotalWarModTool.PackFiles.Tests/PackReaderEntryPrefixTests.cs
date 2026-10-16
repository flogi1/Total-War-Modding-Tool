namespace TotalWarModTool.PackFiles.Tests;

public class PackReaderEntryPrefixTests
{
    private static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    private static byte[] Zstd(byte[] data) { using var c = new ZstdSharp.Compressor(); return c.Wrap(data).ToArray(); }

    private static (PackIndex Index, PackEntry Entry) One(TempDir dir, byte[] stored, bool compressed)
    {
        var path = new PackBuilder().AddEntry("a/file.txt", stored, compressed).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        return (index, index.Entries[0]);
    }

    private static byte[] Sample() => B(string.Concat(Enumerable.Repeat("total war ", 500)));

    [Fact] public void Prefix_of_an_uncompressed_entry()
    {
        using var dir = new TempDir();
        var data = Sample();
        var (index, entry) = One(dir, data, compressed: false);
        var c = PackReader.ReadEntryPrefix(index, entry, 100, CancellationToken.None);
        Assert.Equal(EntryProblem.None, c.Problem);
        Assert.Equal(data[..100], c.Bytes);
    }

    [Fact] public void Prefix_of_a_zstd_entry()
    {
        using var dir = new TempDir();
        var data = Sample();
        var (index, entry) = One(dir, PackBuilder.Stored(Zstd(data), data.Length), compressed: true);
        var c = PackReader.ReadEntryPrefix(index, entry, 100, CancellationToken.None);
        Assert.Equal((EntryProblem.None, CompressionFormat.Zstd, (long)data.Length), (c.Problem, c.Format, c.UncompressedSize));
        Assert.Equal(data[..100], c.Bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prefix_of_a_short_entry_is_the_whole_entry(bool compressed)
    {
        using var dir = new TempDir();
        var data = B("short entry");
        var (index, entry) = One(dir, compressed ? PackBuilder.Stored(Zstd(data), data.Length) : data, compressed);
        var c = PackReader.ReadEntryPrefix(index, entry, 256, CancellationToken.None);
        Assert.Equal(EntryProblem.None, c.Problem);
        Assert.Equal(data, c.Bytes);
    }

    [Fact] public void Prefix_of_a_corrupt_zstd_entry_is_Corrupt()
    {
        using var dir = new TempDir();
        var data = Sample();
        var frame = Zstd(data);
        var (index, entry) = One(dir, PackBuilder.Stored(frame[..6], data.Length), compressed: true);
        var c = PackReader.ReadEntryPrefix(index, entry, 100, CancellationToken.None);
        Assert.Equal(EntryProblem.Corrupt, c.Problem);
        Assert.Null(c.Bytes);
    }
}
