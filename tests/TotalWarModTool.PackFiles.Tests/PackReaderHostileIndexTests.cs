namespace TotalWarModTool.PackFiles.Tests;

/// <summary>Synthetic broken packs: one case per InconsistentIndex branch, the path cap, and the 64 KB buffer refill boundary.</summary>
public class PackReaderHostileIndexTests
{
    private const int HeaderSize = PackReader.HeaderSize;
    private const int DependencyCountAt = 8;
    private const int DependencySizeAt = 12;
    private const int EntryCountAt = 16;
    private const int EntryIndexSizeAt = 20;

    private static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    private static void PatchU32(byte[] bytes, int position, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, position);

    private static uint U32(byte[] bytes, int position) => BitConverter.ToUInt32(bytes, position);

    private static int EntryIndexStart(byte[] bytes) => HeaderSize + (int)U32(bytes, DependencySizeAt);

    private static int EntryIndexEnd(byte[] bytes) => EntryIndexStart(bytes) + (int)U32(bytes, EntryIndexSizeAt);

    private static PackIndexResult ReadIndex(TempDir dir, byte[] bytes)
    {
        var path = dir.File("a.pack");
        File.WriteAllBytes(path, bytes);
        return PackReader.ReadIndex(path);
    }

    [Fact]
    public void Entry_path_without_terminating_zero_is_InconsistentIndex()
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddEntry("abc", B("x")).Build();
        bytes[EntryIndexEnd(bytes) - 1] = (byte)'z';   // the terminator of the last path
        var r = ReadIndex(dir, bytes);
        Assert.Equal(PackProblem.InconsistentIndex, r.Problem);
        Assert.Contains("terminating zero", r.Detail);
    }

    [Theory]
    [InlineData(0x0u, 1)]   // inside the second path (no terminator left)
    [InlineData(0x0u, 2)]   // path of the second entry is missing completely
    [InlineData(0x0u, 3)]   // compression marker is missing
    [InlineData(0x0u, 4)]   // stored size is cut after 3 bytes
    [InlineData(0x0u, 6)]   // stored size is cut after 1 byte
    [InlineData(0x40u, 1)]
    [InlineData(0x40u, 3)]
    [InlineData(0x40u, 4)]
    [InlineData(0x40u, 5)]  // timestamp is cut
    [InlineData(0x40u, 9)]  // stored size is cut after 2 bytes
    public void Entry_index_that_ends_inside_an_entry_is_InconsistentIndex(uint flags, int cut)
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().WithFlags(flags).AddEntry("a", B("x")).AddEntry("b", B("y")).Build();
        PatchU32(bytes, EntryIndexSizeAt, U32(bytes, EntryIndexSizeAt) - (uint)cut);
        Assert.Equal(PackProblem.InconsistentIndex, ReadIndex(dir, bytes).Problem);
    }

    [Fact]
    public void Entry_count_larger_than_the_entries_present_is_InconsistentIndex()
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddEntry("a", B("x")).Build();
        PatchU32(bytes, EntryCountAt, 2);
        Assert.Equal(PackProblem.InconsistentIndex, ReadIndex(dir, bytes).Problem);
    }

    [Fact]
    public void Entry_index_with_bytes_after_the_last_entry_is_InconsistentIndex()
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddEntry("a", B("x")).AddEntry("b", B("y")).Build();
        PatchU32(bytes, EntryCountAt, 1);
        var r = ReadIndex(dir, bytes);
        Assert.Equal(PackProblem.InconsistentIndex, r.Problem);
        Assert.Contains("after the last", r.Detail);
    }

    [Fact]
    public void Dependency_count_larger_than_the_names_present_is_InconsistentIndex()
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddDependency("one.pack").AddDependency("two.pack").AddEntry("a", B("x")).Build();
        PatchU32(bytes, DependencyCountAt, 3);
        Assert.Equal(PackProblem.InconsistentIndex, ReadIndex(dir, bytes).Problem);
    }

    [Fact]
    public void Dependency_index_with_bytes_after_the_last_name_is_InconsistentIndex()
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddDependency("one.pack").AddDependency("two.pack").AddEntry("a", B("x")).Build();
        PatchU32(bytes, DependencyCountAt, 1);
        var r = ReadIndex(dir, bytes);
        Assert.Equal(PackProblem.InconsistentIndex, r.Problem);
        Assert.Contains("after the last", r.Detail);
    }

    [Fact]
    public void Dependency_name_without_terminating_zero_is_InconsistentIndex()
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddDependency("one.pack").AddEntry("a", B("x")).Build();
        bytes[EntryIndexStart(bytes) - 1] = (byte)'z';   // the terminator of the last dependency name
        Assert.Equal(PackProblem.InconsistentIndex, ReadIndex(dir, bytes).Problem);
    }

    [Theory]
    [InlineData((byte)2)]
    [InlineData((byte)0xFF)]
    public void Compression_marker_above_one_is_InconsistentIndex(byte marker)
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddEntry("a", B("x")).Build();
        bytes[EntryIndexStart(bytes) + 4] = marker;   // after the 4-byte stored size
        var r = ReadIndex(dir, bytes);
        Assert.Equal(PackProblem.InconsistentIndex, r.Problem);
        Assert.Contains("compression marker", r.Detail);
    }

    [Fact]
    public void Path_of_65535_bytes_is_read_and_one_byte_more_is_InconsistentIndex()
    {
        using var dir = new TempDir();
        var longest = new string('a', 65535);
        var ok = ReadIndex(dir, new PackBuilder().AddEntry(longest, B("x")).AddEntry("b", B("y")).Build());
        Assert.Equal(PackProblem.None, ok.Problem);
        Assert.Equal([longest, "b"], ok.Index!.Entries.Select(e => e.Path));

        var tooLong = ReadIndex(dir, new PackBuilder().AddEntry(longest + "a", B("x")).AddEntry("b", B("y")).Build());
        Assert.Equal(PackProblem.InconsistentIndex, tooLong.Problem);
        Assert.Contains("longer than", tooLong.Detail);
    }

    [Fact]
    public void Path_without_terminator_that_is_longer_than_the_cap_is_InconsistentIndex()
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddEntry(new string('a', 70000), B("x")).Build();
        bytes[EntryIndexEnd(bytes) - 1] = (byte)'z';
        Assert.Equal(PackProblem.InconsistentIndex, ReadIndex(dir, bytes).Problem);
    }

    private sealed record Expected(string Path, byte[] Stored, bool Compressed);

    // Entry i: path "f/<i>" (fixed length 7, optionally padded for the first entry), 0 to 4 stored bytes, compressed when i % 3 == 0.
    private static (byte[] Bytes, List<Expected> Entries) BuildPack(int count, uint flags, int firstPathPad, Func<int, string> pathOf)
    {
        var builder = new PackBuilder().WithFlags(flags).AddDependency("first.pack");
        var expected = new List<Expected>();
        for (var i = 0; i < count; i++)
        {
            var path = pathOf(i) + (i == 0 ? new string('p', firstPathPad) : string.Empty);
            var stored = Enumerable.Repeat((byte)(i & 0xFF), i % 5).ToArray();
            var compressed = i % 3 == 0;
            builder.AddEntry(path.Replace('/', '\\'), stored, compressed);
            expected.Add(new Expected(path, stored, compressed));
        }

        return (builder.Build(), expected);
    }

    private static void AssertEveryEntry(byte[] bytes, PackIndex index, List<Expected> expected)
    {
        Assert.Equal(expected.Count, index.Entries.Count);
        Assert.Equal(["first.pack"], index.Dependencies);
        var offset = (long)EntryIndexEnd(bytes);
        for (var i = 0; i < expected.Count; i++)
        {
            var entry = index.Entries[i];
            var want = expected[i];
            Assert.True(
                (want.Path, offset, (long)want.Stored.Length, want.Compressed) == (entry.Path, entry.Offset, entry.StoredSize, entry.IsCompressed),
                $"entry {i}: expected ({want.Path}, {offset}, {want.Stored.Length}, {want.Compressed}) but got ({entry.Path}, {entry.Offset}, {entry.StoredSize}, {entry.IsCompressed})");
            Assert.True(want.Stored.AsSpan().SequenceEqual(bytes.AsSpan((int)entry.Offset, (int)entry.StoredSize)), $"entry {i}: data differs at offset {entry.Offset}");
            offset += want.Stored.Length;
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x40u)]
    public void Index_with_several_buffer_refills_and_varying_paths_is_read_exactly(uint flags)
    {
        using var dir = new TempDir();
        var (bytes, expected) = BuildPack(9000, flags, 0, i => $"dir{i % 7}/sub/file_{i}_" + new string('x', i * 7 % 11));
        Assert.True(U32(bytes, EntryIndexSizeAt) > 3 * 64 * 1024);
        var r = ReadIndex(dir, bytes);
        Assert.Equal(PackProblem.None, r.Problem);
        AssertEveryEntry(bytes, r.Index!, expected);
    }

    [Theory]
    [MemberData(nameof(BoundaryAlignments))]
    public void Every_field_of_an_entry_can_straddle_the_64KB_buffer_edge(uint flags, int pad)
    {
        using var dir = new TempDir();
        // Fixed-length entries and a padded first path move the buffer edge through every byte of an entry as pad grows.
        var (bytes, expected) = BuildPack(6000, flags, pad, i => $"f/{i:D5}");
        Assert.InRange(U32(bytes, EntryIndexSizeAt), 64u * 1024 + 1, 128u * 1024);
        var r = ReadIndex(dir, bytes);
        Assert.Equal(PackProblem.None, r.Problem);
        AssertEveryEntry(bytes, r.Index!, expected);
    }

    public static IEnumerable<object[]> BoundaryAlignments()
    {
        foreach (var flags in new[] { 0u, 0x40u })
        {
            for (var pad = 0; pad < 17; pad++)
            {
                yield return [flags, pad];
            }
        }
    }
}
