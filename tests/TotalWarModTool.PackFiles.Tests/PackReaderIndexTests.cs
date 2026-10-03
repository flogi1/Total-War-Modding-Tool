namespace TotalWarModTool.PackFiles.Tests;

public class PackReaderIndexTests
{
    private static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Fact] public void ReadHeader_returns_type_flags_and_counts()
    {
        using var dir = new TempDir();
        var path = new PackBuilder(packType: 1).WithFlags(0x40).AddDependency("base.pack")
            .AddEntry(@"db\units_tables\data__", B("abc")).WriteTo(dir.File("a.pack"));
        var r = PackReader.ReadHeader(path);
        Assert.Equal(PackProblem.None, r.Problem);
        Assert.Equal(("PFH5", PackType.Release, true, false, false, 1L, 1L), (r.Header!.Magic, r.Header.Type,
            r.Header.HasIndexTimestamps, r.Header.HasEncryptedIndex, r.Header.HasEncryptedData, r.Header.DependencyCount, r.Header.EntryCount));
    }

    [Theory]
    [InlineData(0u)]      // without timestamps
    [InlineData(0x40u)]   // with timestamps
    public void ReadIndex_lists_entries_with_absolute_offsets(uint flags)
    {
        using var dir = new TempDir();
        var path = new PackBuilder().WithFlags(flags).AddDependency("first.pack").AddDependency("second.pack")
            .AddEntry(@"db\units_tables\data__", B("abc"))
            .AddEntry(@"script\main.lua", B("hello"), compressed: true).WriteTo(dir.File("a.pack"));
        var r = PackReader.ReadIndex(path);
        Assert.Equal(PackProblem.None, r.Problem);
        var index = r.Index!;
        Assert.Equal(["first.pack", "second.pack"], index.Dependencies);
        Assert.Equal(2, index.Entries.Count);
        var first = index.Entries[0]; var second = index.Entries[1];
        Assert.Equal(("db/units_tables/data__", 3L, false), (first.Path, first.StoredSize, first.IsCompressed));
        Assert.Equal(("script/main.lua", 5L, true), (second.Path, second.StoredSize, second.IsCompressed));
        Assert.Equal(first.Offset + 3, second.Offset);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(B("abc"), bytes[(int)first.Offset..(int)(first.Offset + 3)]);
        Assert.Equal(new FileInfo(path).Length, index.Fingerprint.Length);
    }

    [Fact] public void Paths_are_normalized_and_found_case_insensitively()
    {
        using var dir = new TempDir();
        var path = new PackBuilder()
            .AddEntry(@"db\Ünits_tables\!モッド", B("1"))
            .AddEntry(@"DB\ünits_TABLES\!モッド", B("22")).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        Assert.Equal("db/Ünits_tables/!モッド", index.Entries[0].Path);
        Assert.Same(index.Entries[0], index.Find(@"DB\ünits_tables/!モッド"));   // first of two duplicates
        Assert.Null(index.Find("db/other"));
    }

    [Fact] public void Empty_pack_has_no_entries()
    {
        using var dir = new TempDir();
        var r = PackReader.ReadIndex(new PackBuilder().WriteTo(dir.File("a.pack")));
        Assert.Equal((PackProblem.None, 0), (r.Problem, r.Index!.Entries.Count));
    }

    [Fact] public void Missing_file_is_NotFound()
    {
        Assert.Equal(PackProblem.NotFound, PackReader.ReadHeader(@"X:\none.pack").Problem);
        Assert.Equal(PackProblem.NotFound, PackReader.ReadIndex(@"X:\none.pack").Problem);
    }

    [Fact] public void Locked_file_is_Unreadable()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().WriteTo(dir.File("a.pack"));
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(PackProblem.Unreadable, PackReader.ReadIndex(path).Problem);
    }

    [Fact] public void File_shorter_than_the_header_is_Truncated()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(dir.File("a.pack"), new byte[10]);
        Assert.Equal(PackProblem.Truncated, PackReader.ReadHeader(dir.File("a.pack")).Problem);
    }

    [Theory]
    [InlineData("PFH4", 0u)]
    [InlineData("ABCD", 0u)]
    [InlineData("PFH5", 0x100u)]   // extended header
    public void Other_formats_are_UnknownFormat(string magic, uint flags)
    {
        using var dir = new TempDir();
        var path = new PackBuilder(magic).WithFlags(flags).WriteTo(dir.File("a.pack"));
        var r = PackReader.ReadIndex(path);
        Assert.Equal(PackProblem.UnknownFormat, r.Problem);
        Assert.Contains(magic, r.Detail);
    }

    [Fact] public void Encrypted_index_is_reported_by_ReadIndex_only()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().WithFlags(0x80).AddEntry("a", B("x")).WriteTo(dir.File("a.pack"));
        Assert.Equal((PackProblem.None, true), (PackReader.ReadHeader(path).Problem, PackReader.ReadHeader(path).Header!.HasEncryptedIndex));
        Assert.Equal(PackProblem.EncryptedIndex, PackReader.ReadIndex(path).Problem);
    }

    [Fact] public void Index_size_beyond_the_file_is_Truncated()
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddEntry("a", B("x")).Build();
        BitConverter.GetBytes(0x7FFFFFFFu).CopyTo(bytes, 20);          // entry index size
        File.WriteAllBytes(dir.File("a.pack"), bytes);
        Assert.Equal(PackProblem.Truncated, PackReader.ReadIndex(dir.File("a.pack")).Problem);
    }

    [Fact] public void Huge_entry_count_is_inconsistent_not_a_crash()
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddEntry("a", B("x")).Build();
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(bytes, 16);          // entry count
        File.WriteAllBytes(dir.File("a.pack"), bytes);
        Assert.Equal(PackProblem.InconsistentIndex, PackReader.ReadIndex(dir.File("a.pack")).Problem);
    }

    [Theory]
    [InlineData(1)]    // one byte too many
    [InlineData(-1)]   // one byte missing
    public void Data_that_does_not_end_at_the_file_end_is_InconsistentIndex(int delta)
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddEntry("a", B("xyz")).Build();
        File.WriteAllBytes(dir.File("a.pack"), delta > 0 ? [.. bytes, 0] : bytes[..^1]);
        Assert.Equal(PackProblem.InconsistentIndex, PackReader.ReadIndex(dir.File("a.pack")).Problem);
    }

    [Fact] public void Unknown_pack_type_is_not_a_problem()
    {
        using var dir = new TempDir();
        var r = PackReader.ReadHeader(new PackBuilder(packType: 9).WriteTo(dir.File("a.pack")));
        Assert.Equal((PackProblem.None, PackType.Unknown), (r.Problem, r.Header!.Type));
    }

    [Fact] public void Fingerprint_changes_when_the_file_changes()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().AddEntry("a", B("x")).WriteTo(dir.File("a.pack"));
        var before = FileFingerprint.TryRead(path)!;
        new PackBuilder().AddEntry("a", B("xx")).WriteTo(path);
        Assert.NotEqual(before, FileFingerprint.TryRead(path));
        Assert.Null(FileFingerprint.TryRead(dir.File("missing.pack")));
    }

    [Fact] public void Fingerprint_changes_when_only_the_modification_time_changes()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().AddEntry("a", B("x")).WriteTo(dir.File("a.pack"));
        var before = FileFingerprint.TryRead(path)!;
        File.SetLastWriteTimeUtc(path, before.LastWriteTimeUtc.AddMinutes(5));
        var after = FileFingerprint.TryRead(path)!;
        Assert.Equal(before.Length, after.Length);
        Assert.NotEqual(before, after);
    }
}
