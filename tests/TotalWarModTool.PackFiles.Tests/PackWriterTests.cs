namespace TotalWarModTool.PackFiles.Tests;

public class PackWriterTests
{
    private static readonly DateTimeOffset BuildTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private static PackWriteEntry E(string path, params byte[] bytes) => new(path, bytes.Length, () => new MemoryStream(bytes));

    [Fact] public void Written_pack_is_read_back()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        var big = new byte[5_000_000];
        new Random(1).NextBytes(big);
        var entries = new[] { E("text/db/mod.loc", 1, 2, 3), E(@"db\units_tables\ünit_日本", 9), E("db/Units_tables/a"), E("script/big.bin", big) };
        var result = PackWriter.Write(target, ["first.pack", "second.pack"], entries, BuildTime, null, CancellationToken.None);
        Assert.Equal(PackWriteProblem.None, result.Problem);

        var index = PackReader.ReadIndex(target).Index!;
        Assert.Equal((PackType.Mod, false, false), (index.Header.Type, index.Header.HasIndexTimestamps, index.Header.HasEncryptedIndex));
        Assert.Equal(["first.pack", "second.pack"], index.Dependencies);
        Assert.Equal(["db/Units_tables/a", "db/units_tables/ünit_日本", "script/big.bin", "text/db/mod.loc"], index.Entries.Select(e => e.Path));
        Assert.All(index.Entries, e => Assert.False(e.IsCompressed));
        Assert.Equal(big, PackReader.ReadEntry(index, index.Find("script/big.bin")!, CancellationToken.None).Bytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, PackReader.ReadEntry(index, index.Find("text/db/mod.loc")!, CancellationToken.None).Bytes);
        Assert.Empty(PackReader.ReadEntry(index, index.Find("db/Units_tables/a")!, CancellationToken.None).Bytes!);
    }

    [Fact] public void Writes_the_exact_bytes_of_the_format()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        PackWriter.Write(target, ["dep.pack"], [E("b/y", 7, 8), E("a/x", 5)], BuildTime, null, CancellationToken.None);
        var expected = new PackBuilder().AddDependency("dep.pack").AddEntry(@"a\x", [5]).AddEntry(@"b\y", [7, 8]).Build();
        BitConverter.GetBytes((uint)1_700_000_000).CopyTo(expected, 24);
        Assert.Equal(expected, File.ReadAllBytes(target));
    }

    [Fact] public void An_empty_pack_is_valid()
    {
        using var dir = new TempDir();
        var target = dir.File("empty.pack");
        Assert.Equal(PackWriteProblem.None, PackWriter.Write(target, [], [], BuildTime, null, CancellationToken.None).Problem);
        Assert.Empty(PackReader.ReadIndex(target).Index!.Entries);
    }

    [Theory]
    [InlineData("")] [InlineData("/a")] [InlineData("a/")] [InlineData("a//b")] [InlineData(@"a\\b")] [InlineData("a\0b")]
    public void Validate_rejects_an_invalid_path(string path) =>
        Assert.Equal(PackWriteProblem.InvalidPath, PackWriter.Validate([], [E(path)]).Problem);

    [Fact] public void Validate_reports_limits_duplicates_and_bad_dependencies()
    {
        Assert.Equal(PackWriteProblem.None, PackWriter.Validate(["a.pack"], [E(new string('a', 65535))]).Problem);
        Assert.Equal(PackWriteProblem.PathTooLong, PackWriter.Validate([], [E(new string('a', 65536))]).Problem);
        Assert.Equal(PackWriteProblem.PathTooLong, PackWriter.Validate([], [E(new string('ü', 32768))]).Problem);
        var duplicate = PackWriter.Validate([], [E("db/T/x"), E("other"), E(@"DB\t\X")]);
        Assert.Equal(PackWriteProblem.DuplicatePath, duplicate.Problem);
        Assert.Contains("db/T/x", duplicate.Detail!.Replace('\\', '/'));
        Assert.Equal(PackWriteProblem.InvalidDependency, PackWriter.Validate([""], []).Problem);
        Assert.Equal(PackWriteProblem.InvalidDependency, PackWriter.Validate(["a\0b"], []).Problem);
        Assert.Equal(PackWriteProblem.EntryTooLarge, PackWriter.Validate([], [new PackWriteEntry("a", (long)uint.MaxValue + 1, () => Stream.Null)]).Problem);
        Assert.Equal(PackWriteProblem.None, PackWriter.Validate([], [new PackWriteEntry("a", uint.MaxValue, () => Stream.Null)]).Problem);
        Assert.Throws<ArgumentException>(() => PackWriter.Validate([], [new PackWriteEntry("a", -1, () => Stream.Null)]));
    }

    [Fact] public void A_validation_problem_leaves_an_existing_target_untouched()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        File.WriteAllBytes(target, [1, 2, 3]);
        Assert.Equal(PackWriteProblem.InvalidPath, PackWriter.Write(target, [], [E("")], BuildTime, null, CancellationToken.None).Problem);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
    }

    [Theory]
    [InlineData(2)] [InlineData(4)]
    public void A_source_with_the_wrong_length_fails_and_leaves_no_file(int declared)
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        var entries = new[] { E("a", 1), new PackWriteEntry("b", declared, () => new MemoryStream([1, 2, 3])) };
        var result = PackWriter.Write(target, [], entries, BuildTime, null, CancellationToken.None);
        Assert.Equal(PackWriteProblem.SourceChanged, result.Problem);
        Assert.Contains("b", result.Detail);
        Assert.False(File.Exists(target));
    }

    [Fact] public void An_unreadable_source_fails_closes_streams_and_leaves_no_file()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        var first = new MemoryStream([1]);
        var entries = new[] { new PackWriteEntry("a", 1, () => first), new PackWriteEntry("b", 1, () => throw new IOException("locked")) };
        var result = PackWriter.Write(target, [], entries, BuildTime, null, CancellationToken.None);
        Assert.Equal(PackWriteProblem.SourceUnreadable, result.Problem);
        Assert.False(File.Exists(target));
        Assert.False(first.CanRead);   // disposed
    }

    [Fact] public void A_missing_target_folder_is_reported()
    {
        using var dir = new TempDir();
        var result = PackWriter.Write(Path.Combine(dir.Path, "missing", "mod.pack"), [], [E("a", 1)], BuildTime, null, CancellationToken.None);
        Assert.Equal(PackWriteProblem.TargetUnwritable, result.Problem);
    }

    [Fact] public void Cancel_leaves_no_file()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        using var cts = new CancellationTokenSource();
        var entries = new[] { E("a", 1), new PackWriteEntry("b", 1, () => { cts.Cancel(); return new MemoryStream([2]); }), E("c", 3) };
        Assert.ThrowsAny<OperationCanceledException>(() => PackWriter.Write(target, [], entries, BuildTime, null, cts.Token));
        Assert.False(File.Exists(target));
    }

    [Fact] public void Progress_counts_the_entries_written()
    {
        using var dir = new TempDir();
        var seen = new List<int>();
        PackWriter.Write(dir.File("mod.pack"), [], [E("a", 1), E("b", 2), E("c", 3)], BuildTime, new SyncProgress(seen.Add), CancellationToken.None);
        Assert.Equal([1, 2, 3], seen);
    }

    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
