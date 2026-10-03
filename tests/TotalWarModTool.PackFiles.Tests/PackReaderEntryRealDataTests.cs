namespace TotalWarModTool.PackFiles.Tests;

public class PackReaderEntryRealDataTests
{
    private static readonly string[] Relevant = [".loc", ".lua", ".xml", ".txt", ".json", ".csv", ".tsv"];
    private static bool IsRelevant(PackEntry e) =>
        e.Path.StartsWith("db/", StringComparison.OrdinalIgnoreCase) || Relevant.Contains(Path.GetExtension(e.Path).ToLowerInvariant());

    [RpfmFact] public void Zstd_entries_of_db_pack_match_rpfm_cli()
    {
        using var dir = new TempDir();
        var pack = Path.Combine(RealData.Wh3Data, "db.pack");
        RealData.RpfmExtractFolder(pack, "db", dir.Path);
        var index = PackReader.ReadIndex(pack).Index!;
        Assert.NotEmpty(index.Entries);
        Assert.All(index.Entries, e =>
        {
            var c = PackReader.ReadEntry(index, e, CancellationToken.None);
            Assert.True(c.Problem == EntryProblem.None, $"{e.Path}: {c.Problem} {c.Detail}");
            Assert.True(c.Bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dir.Path, e.Path))), e.Path);
        });
    }

    [RealDataFact] public void Every_relevant_compressed_entry_in_the_data_folder_decompresses()
    {
        var formats = new HashSet<CompressionFormat>();
        foreach (var pack in Directory.GetFiles(RealData.Wh3Data, "*.pack"))
        {
            var index = PackReader.ReadIndex(pack).Index!;
            if (index.Header.HasEncryptedData) continue;
            foreach (var e in index.Entries.Where(e => e.IsCompressed && IsRelevant(e)))
            {
                var c = PackReader.ReadEntry(index, e, CancellationToken.None);
                Assert.True(c.Problem == EntryProblem.None, $"{Path.GetFileName(pack)}:{e.Path}: {c.Problem} {c.Detail}");
                Assert.Equal(c.UncompressedSize, c.Bytes!.LongLength);
                formats.Add(c.Format);
            }
        }
        Assert.Contains(CompressionFormat.Zstd, formats);
        Assert.Contains(CompressionFormat.Lz4, formats);
    }

    [RpfmFact] public void Lz4_samples_match_rpfm_cli()
    {
        foreach (var pack in Directory.GetFiles(RealData.Wh3Data, "*.pack"))
        {
            var index = PackReader.ReadIndex(pack).Index!;
            var samples = index.Entries.Where(e => e.IsCompressed && IsRelevant(e))
                .Select(e => (Entry: e, Content: PackReader.ReadEntry(index, e, CancellationToken.None)))
                .Where(x => x.Content.Format == CompressionFormat.Lz4).Take(5).ToList();
            if (samples.Count == 0) continue;
            using var dir = new TempDir();
            RealData.RpfmExtractFiles(pack, samples.Select(s => s.Entry.Path), dir.Path);
            Assert.All(samples, s => Assert.True(s.Content.Bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dir.Path, s.Entry.Path))), s.Entry.Path));
            return;
        }
        Assert.Fail("no LZ4 entry found in the data folder");
    }
}
