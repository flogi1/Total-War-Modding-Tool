namespace TotalWarModTool.PackFiles.Tests;

public class PackWriterRealDataTests
{
    [RealDataFact] public void A_rebuilt_vanilla_pack_has_the_same_content()
    {
        using var dir = new TempDir();
        var source = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "local_en.pack")).Index!;
        var entries = source.Entries.Select(e => { var b = PackReader.ReadEntry(source, e, CancellationToken.None).Bytes!; return new PackWriteEntry(e.Path, b.Length, () => new MemoryStream(b)); }).ToList();
        var target = dir.File("rebuilt.pack");
        Assert.Equal(PackWriteProblem.None, PackWriter.Write(target, source.Dependencies, entries, DateTimeOffset.UnixEpoch, null, CancellationToken.None).Problem);
        var rebuilt = PackReader.ReadIndex(target).Index!;
        Assert.Equal(source.Entries.Count, rebuilt.Entries.Count);
        Assert.Equal(source.Dependencies, rebuilt.Dependencies);
        foreach (var e in source.Entries)
            Assert.True(PackReader.ReadEntry(source, e, CancellationToken.None).Bytes!.AsSpan().SequenceEqual(
                PackReader.ReadEntry(rebuilt, rebuilt.Find(e.Path)!, CancellationToken.None).Bytes), e.Path);
    }

    [RpfmFact] public void Rpfm_cli_extracts_a_pack_we_wrote()
    {
        using var dir = new TempDir();
        var source = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "local_en.pack")).Index!;
        var picked = source.Entries.Take(50).ToList();
        var bytes = picked.ToDictionary(e => e.Path, e => PackReader.ReadEntry(source, e, CancellationToken.None).Bytes!);
        var target = dir.File("ours.pack");
        PackWriter.Write(target, [], picked.Select(e => new PackWriteEntry(e.Path, bytes[e.Path].Length, () => new MemoryStream(bytes[e.Path]))).ToList(),
            DateTimeOffset.UnixEpoch, null, CancellationToken.None);
        var outDir = Path.Combine(dir.Path, "out");
        RealData.RpfmExtractFolder(target, "/", outDir);
        foreach (var (path, expected) in bytes)
            Assert.Equal(expected, File.ReadAllBytes(Path.Combine(outDir, path.Replace('/', Path.DirectorySeparatorChar))));
    }
}
