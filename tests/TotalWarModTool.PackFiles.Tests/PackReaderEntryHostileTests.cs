namespace TotalWarModTool.PackFiles.Tests;

public class PackReaderEntryHostileTests
{
    private static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Fact] public void Huge_stored_frame_with_a_small_claimed_size_is_too_large()
    {
        using var dir = new TempDir();
        var frame = new byte[70_000];
        PackBuilder.Hex("04 22 4D 18").CopyTo(frame, 0);       // LZ4 magic, the rest is filler
        var path = new PackBuilder().AddEntry("a/file.txt", PackBuilder.Stored(frame, 3), compressed: true).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        var c = PackReader.ReadEntry(index, index.Entries[0], CancellationToken.None, maxBytes: 4);
        Assert.Equal((EntryProblem.TooLarge, 3L), (c.Problem, c.UncompressedSize));
        Assert.Null(c.Bytes);
        Assert.Contains("70000", c.Detail);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void Non_positive_max_bytes_is_an_argument_error(long maxBytes)
    {
        using var dir = new TempDir();
        var path = new PackBuilder().AddEntry("a/file.txt", B("hello")).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        Assert.Throws<ArgumentOutOfRangeException>(() => PackReader.ReadEntry(index, index.Entries[0], CancellationToken.None, maxBytes));
    }

    [Fact] public void Mutated_and_truncated_zstd_frames_never_throw()
    {
        var original = B(string.Concat(Enumerable.Range(0, 400).Select(i => $"unit_{i * 7919 % 1000} total war {i}\n")));
        byte[] frame;
        using (var compressor = new ZstdSharp.Compressor()) { frame = compressor.Wrap(original).ToArray(); }

        var random = new Random(20261002);
        var variants = new List<byte[]>();
        for (var i = 0; i < 2400; i++)
        {
            var copy = (byte[])frame.Clone();
            switch (i % 3)
            {
                case 0:   // bit flips behind the magic number (a changed magic would be another format, not a damaged zstd frame)
                    for (var n = random.Next(1, 5); n > 0; n--) copy[random.Next(4, copy.Length)] ^= (byte)(1 << random.Next(8));
                    break;
                case 1:   // truncation
                    copy = copy[..random.Next(1, copy.Length)];
                    break;
                default:  // cut somewhere behind the magic number and append random bytes
                    var keep = random.Next(4, copy.Length);
                    var tail = new byte[random.Next(1, 64)];
                    random.NextBytes(tail);
                    copy = [.. copy[..keep], .. tail];
                    break;
            }

            variants.Add(copy);
        }

        using var dir = new TempDir();
        var builder = new PackBuilder();
        for (var i = 0; i < variants.Count; i++)
        {
            builder.AddEntry($"v/{i}", PackBuilder.Stored(variants[i], original.Length), compressed: true);
        }

        var index = PackReader.ReadIndex(builder.WriteTo(dir.File("a.pack"))).Index!;
        var results = new List<EntryContent>();
        foreach (var entry in index.Entries)
        {
            results.Add(PackReader.ReadEntry(index, entry, CancellationToken.None));
        }

        Assert.Equal(variants.Count, results.Count);
        Assert.All(results, c =>
        {
            Assert.True(c.Problem is EntryProblem.None or EntryProblem.Corrupt, $"{c.Problem} {c.Detail}");
            if (c.Problem == EntryProblem.None)
            {
                Assert.Equal(original.Length, c.Bytes!.Length);
            }
        });
    }
}
