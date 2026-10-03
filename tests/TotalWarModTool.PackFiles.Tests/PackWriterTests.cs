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
        var opened = new List<MemoryStream>();
        var entries = new[] { Tracked("a", opened, [1]), Tracked("b", opened, [1, 2, 3], declared) };
        var result = PackWriter.Write(target, [], entries, BuildTime, null, CancellationToken.None);
        Assert.Equal(PackWriteProblem.SourceChanged, result.Problem);
        Assert.Contains("'b'", result.Detail);
        Assert.False(File.Exists(target));
        Assert.Equal(2, opened.Count);
        Assert.All(opened, s => Assert.False(s.CanRead));   // disposed
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
        var opened = new List<MemoryStream>();
        var entries = new[]
        {
            Tracked("a", opened, [1]),
            new PackWriteEntry("b", 1, () => { cts.Cancel(); var s = new MemoryStream([2]); opened.Add(s); return s; }),
            Tracked("c", opened, [3]),
        };
        Assert.ThrowsAny<OperationCanceledException>(() => PackWriter.Write(target, [], entries, BuildTime, null, cts.Token));
        Assert.False(File.Exists(target));
        Assert.Equal(2, opened.Count);
        Assert.All(opened, s => Assert.False(s.CanRead));   // disposed
    }

    [Fact] public void A_token_cancelled_before_write_throws_without_opening_or_creating_anything()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var opened = new List<MemoryStream>();
        var entries = Enumerable.Range(0, 1000).Select(i => Tracked($"db/t/{i}", opened, [1])).ToList();
        Assert.ThrowsAny<OperationCanceledException>(() => PackWriter.Write(target, ["a.pack"], entries, BuildTime, null, cts.Token));
        Assert.False(File.Exists(target));
        Assert.Empty(opened);
    }

    [Fact] public void Cancel_by_the_first_open_stops_before_any_later_open()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        using var cts = new CancellationTokenSource();
        var opens = new List<string>();
        PackWriteEntry Cancelling(string path) => new(path, 1, () => { opens.Add(path); cts.Cancel(); return new MemoryStream([1]); });
        PackWriteEntry Plain(string path) => new(path, 1, () => { opens.Add(path); return new MemoryStream([1]); });
        var entries = new[] { Cancelling("a"), Plain("b"), Plain("c") };
        Assert.ThrowsAny<OperationCanceledException>(() => PackWriter.Write(target, [], entries, BuildTime, null, cts.Token));
        Assert.Equal(["a"], opens);
        Assert.False(File.Exists(target));
    }

    [Fact] public void An_unexpected_exception_of_a_source_leaves_after_cleanup()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        var opened = new List<MemoryStream>();
        var entries = new[] { Tracked("a", opened, [1]), new PackWriteEntry("b", 1, () => { var s = new NotReadableStream(); opened.Add(s); return s; }) };
        Assert.Throws<NotSupportedException>(() => PackWriter.Write(target, [], entries, BuildTime, null, CancellationToken.None));
        Assert.False(File.Exists(target));
        Assert.Equal(2, opened.Count);
        Assert.All(opened, s => Assert.False(s.CanRead));   // disposed
    }

    [Fact] public void A_target_that_cannot_be_removed_is_named_in_the_detail()
    {
        // A read-only file cannot be deleted on Windows. The attribute is set while the target is open, from the source.
        if (!OperationSystemSupportsReadOnlyDelete())
        {
            return;
        }

        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        try
        {
            var entries = new[] { new PackWriteEntry("a", 2, () => { File.SetAttributes(target, FileAttributes.ReadOnly); return new MemoryStream([1]); }) };
            var result = PackWriter.Write(target, [], entries, BuildTime, null, CancellationToken.None);
            Assert.Equal(PackWriteProblem.SourceChanged, result.Problem);
            Assert.Contains("could not be removed", result.Detail);
            Assert.Contains(target, result.Detail);
            Assert.True(File.Exists(target));
        }
        finally
        {
            if (File.Exists(target))
            {
                File.SetAttributes(target, FileAttributes.Normal);
            }
        }
    }

    [Fact] public void A_target_that_can_be_removed_adds_nothing_to_the_detail()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        var result = PackWriter.Write(target, [], [new PackWriteEntry("a", 5, () => new MemoryStream([1]))], BuildTime, null, CancellationToken.None);
        Assert.Equal(PackWriteProblem.SourceChanged, result.Problem);
        Assert.DoesNotContain("removed", result.Detail);
        Assert.False(File.Exists(target));
    }

    [Fact] public void Unicode_paths_beyond_the_bmp_are_written_as_utf8()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        var path = "db/" + char.ConvertFromUtf32(0x1F600) + "/x";
        Assert.Equal(PackWriteProblem.None, PackWriter.Write(target, [char.ConvertFromUtf32(0x1F600) + ".pack"], [E(path, 1)], BuildTime, null, CancellationToken.None).Problem);
        var index = PackReader.ReadIndex(target).Index!;
        Assert.Equal([path], index.Entries.Select(e => e.Path));
        Assert.Equal([char.ConvertFromUtf32(0x1F600) + ".pack"], index.Dependencies);
    }

    [Theory]
    [InlineData("a/../b")] [InlineData("./a")] [InlineData("a/..")] [InlineData(".")] [InlineData("..")] [InlineData(@"a\..\b")] [InlineData("a/./b")]
    public void Validate_rejects_dot_segments(string path) =>
        Assert.Equal(PackWriteProblem.InvalidPath, PackWriter.Validate([], [E(path)]).Problem);

    [Fact] public void Validate_accepts_names_that_only_look_like_dot_segments()
    {
        Assert.Equal(PackWriteProblem.None, PackWriter.Validate([], [E("a/.../.b/..c/d.")]).Problem);
    }

    [Fact] public void Validate_rejects_unpaired_surrogates_in_paths_and_dependencies()
    {
        var high = ((char)0xD800).ToString();
        var low = ((char)0xDC00).ToString();
        Assert.Equal(PackWriteProblem.InvalidPath, PackWriter.Validate([], [E("a" + high)]).Problem);
        Assert.Equal(PackWriteProblem.InvalidPath, PackWriter.Validate([], [E(low + "a")]).Problem);
        Assert.Equal(PackWriteProblem.InvalidPath, PackWriter.Validate([], [E("a" + high + "b/c")]).Problem);
        Assert.Equal(PackWriteProblem.InvalidPath, PackWriter.Validate([], [E("a" + low + high)]).Problem);
        Assert.Equal(PackWriteProblem.InvalidDependency, PackWriter.Validate(["a" + high], []).Problem);
        Assert.Equal(PackWriteProblem.InvalidDependency, PackWriter.Validate([low], []).Problem);
        Assert.Equal(PackWriteProblem.None, PackWriter.Validate([high + low + ".pack"], [E("a/" + high + low)]).Problem);
    }

    [Fact] public void Validate_limits_the_length_of_a_dependency_name_in_utf8_bytes()
    {
        Assert.Equal(PackWriteProblem.None, PackWriter.Validate([new string('a', 65535)], []).Problem);
        Assert.Equal(PackWriteProblem.InvalidDependency, PackWriter.Validate([new string('a', 65536)], []).Problem);
        Assert.Equal(PackWriteProblem.InvalidDependency, PackWriter.Validate([new string('ü', 32768)], []).Problem);
    }

    [Fact] public void A_detail_names_long_paths_shortened_and_never_splits_a_surrogate_pair()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        var invalid = PackWriter.Validate([], [E(new string('a', 199) + emoji + "//b")]);
        Assert.Equal(PackWriteProblem.InvalidPath, invalid.Problem);
        Assert.True(invalid.Detail!.Length < 400);
        Assert.Contains("(shortened)", invalid.Detail);
        Assert.DoesNotContain("\\u", invalid.Detail);

        var huge = PackWriter.Validate([], [E(new string('a', 100_000) + "//b")]);
        Assert.True(huge.Detail!.Length < 400);

        var tooLong = PackWriter.Validate([], [E(new string('a', 70_000))]);
        Assert.Equal(PackWriteProblem.PathTooLong, tooLong.Problem);
        Assert.Contains("70000", tooLong.Detail);
        Assert.True(tooLong.Detail!.Length < 400);

        var duplicate = PackWriter.Validate([], [E(new string('a', 5000)), E(new string('A', 5000))]);
        Assert.Equal(PackWriteProblem.DuplicatePath, duplicate.Problem);
        Assert.True(duplicate.Detail!.Length < 600);
    }

    [Fact] public void A_detail_shows_zero_characters_and_surrogates_as_escapes()
    {
        var result = PackWriter.Validate([], [E("a\0b")]);
        Assert.Contains("a\\0b", result.Detail);
        Assert.DoesNotContain('\0', result.Detail!);
        var lone = PackWriter.Validate([], [E("a" + (char)0xD800)]);
        Assert.Contains("\\uD800", lone.Detail);
    }

    [Fact] public void Progress_counts_the_entries_written()
    {
        using var dir = new TempDir();
        var seen = new List<int>();
        PackWriter.Write(dir.File("mod.pack"), [], [E("a", 1), E("b", 2), E("c", 3)], BuildTime, new SyncProgress(seen.Add), CancellationToken.None);
        Assert.Equal([1, 2, 3], seen);
    }

    // Records every stream it opens so a test can check that the writer closed it.
    private static PackWriteEntry Tracked(string path, List<MemoryStream> opened, byte[] bytes, long? declaredLength = null) =>
        new(path, declaredLength ?? bytes.Length, () => Open(bytes, opened));
    private static MemoryStream Open(byte[] bytes, List<MemoryStream> opened)
    {
        var stream = new MemoryStream(bytes);
        opened.Add(stream);
        return stream;
    }

    private static bool OperationSystemSupportsReadOnlyDelete() => OperatingSystem.IsWindows();

    private sealed class NotReadableStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("no read");
    }

    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
