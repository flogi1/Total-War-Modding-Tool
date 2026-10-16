namespace TotalWarModTool.PackFiles.Tests;

public class PackReaderEntryRealDataTests(Xunit.Abstractions.ITestOutputHelper output)
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

    [RealDataFact] public void Prefix_of_every_db_pack_table_reads_a_header()
    {
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "db.pack")).Index!;
        var tables = index.Entries.Where(e => e.Path.StartsWith("db/", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(tables);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var e in tables)
        {
            var c = PackReader.ReadEntryPrefix(index, e, TotalWarModTool.Tables.DbTableDecoder.TableHeaderBytes, CancellationToken.None);
            Assert.True(c.Problem == EntryProblem.None, $"{e.Path}: {c.Problem} {c.Detail}");
            Assert.True(TotalWarModTool.Tables.DbTableDecoder.ReadHeader(c.Bytes) is not null, $"{e.Path}: no readable header");
        }
        output.WriteLine($"Read the header of {tables.Count} db.pack tables in {watch.ElapsedMilliseconds} ms.");
    }

    private const int MaxLz4Packs = 3;
    private const int Lz4SamplesPerPack = 3;

    [RpfmFact] public void Lz4_samples_of_up_to_three_packs_match_rpfm_cli()
    {
        // The first vanilla packs by name that hold LZ4 entries (vanilla = listed in the data folder's manifest.txt, so the
        // user's own mod packs are never picked), the smallest relevant entries of each; one rpfm_cli call per pack.
        var manifest = Path.Combine(RealData.Wh3Data, "manifest.txt");
        var vanilla = File.Exists(manifest)
            ? File.ReadLines(manifest).Select(l => Path.GetFileName(l.Split('\t')[0].Replace('\\', '/'))).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        var packs = Directory.GetFiles(RealData.Wh3Data, "*.pack")
            .Where(p => vanilla is null || vanilla.Contains(Path.GetFileName(p)))
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal);
        var verifiedPacks = 0;
        foreach (var pack in packs)
        {
            var index = PackReader.ReadIndex(pack).Index!;
            if (index.Header.HasEncryptedData) continue;
            var samples = index.Entries.Where(e => e.IsCompressed && IsRelevant(e)).OrderBy(e => e.StoredSize)
                .Select(e => (Entry: e, Content: PackReader.ReadEntry(index, e, CancellationToken.None)))
                .Where(x => x.Content.Format == CompressionFormat.Lz4).Take(Lz4SamplesPerPack).ToList();
            if (samples.Count == 0) continue;
            using var dir = new TempDir();
            RealData.RpfmExtractFiles(pack, samples.Select(s => s.Entry.Path), dir.Path);
            Assert.All(samples, s => Assert.True(s.Content.Bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dir.Path, s.Entry.Path))), $"{Path.GetFileName(pack)}:{s.Entry.Path}"));
            if (++verifiedPacks == MaxLz4Packs) break;
        }
        Assert.True(verifiedPacks >= 2, $"LZ4 entries were found in only {verifiedPacks} vanilla pack(s)");
    }
}
