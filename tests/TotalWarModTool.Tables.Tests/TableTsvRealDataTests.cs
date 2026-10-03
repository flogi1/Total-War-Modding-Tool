using System.Text;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables.Tests;

public class TableTsvRealDataTests
{
    private static string SchemaPath => Path.Combine(RepoRoot.Find(), "schemas", "schema_wh3.ron");

    [RealDataFact] public void Every_table_of_db_pack_round_trips_through_tsv_or_is_refused()
    {
        var schema = SchemaLoader.Load(SchemaPath).Schema!;
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "db.pack")).Index!;
        int same = 0, refused = 0;
        foreach (var e in index.Entries)
        {
            if (!DbTablePath.TryGetTableName(e.Path, out var table)) continue;
            var bytes = PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!;
            var t = DbTableDecoder.Decode(bytes, table, schema);
            if (t.Status != TableDecodeStatus.Ok) continue;
            var written = TableTsv.Write(table, t.Version, e.Path, t.Definition!, t.Rows);
            if (written.Text is null) { refused++; Assert.False(string.IsNullOrWhiteSpace(written.Detail), e.Path); continue; }
            var read = TableTsv.Read(written.Text, schema);
            Assert.True(read.Problem is null, $"{e.Path}: {read.Problem}");
            Assert.True(bytes.AsSpan().SequenceEqual(DbTableEncoder.Encode(read.Table!.Definition, read.Table.Version, DbTableDecoder.ReadGuid(bytes), read.Table.Rows)), e.Path);
            same++;
        }
        Assert.True(same > 1000);
        Assert.True(refused <= same / 100, $"{refused} tables refused");
    }

    [RealDataFact] public void Every_loc_of_local_en_pack_round_trips_through_tsv_or_is_refused()
    {
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "local_en.pack")).Index!;
        int same = 0, refused = 0;
        foreach (var e in index.Entries.Where(e => e.Path.EndsWith(".loc", StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!;
            var written = TableTsv.WriteLoc(e.Path, LocDecoder.Decode(bytes).Rows);
            if (written.Text is null) { refused++; continue; }
            var read = TableTsv.ReadLoc(written.Text);
            Assert.True(read.Problem is null, $"{e.Path}: {read.Problem}");
            Assert.True(bytes.AsSpan().SequenceEqual(LocEncoder.Encode(read.Loc!.Rows)), e.Path);
            same++;
        }
        Assert.True(same > 100);
        Assert.True(refused <= same / 100, $"{refused} loc files refused");
    }

    [RpfmFact] public void Rpfm_cli_imports_our_tsv_with_equal_content()
    {
        using var dir = new TempDir();
        var schema = SchemaLoader.Load(SchemaPath).Schema!;
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "db.pack")).Index!;
        var expected = new Dictionary<string, DecodedTable>();
        foreach (var e in index.Entries)
        {
            if (!DbTablePath.TryGetTableName(e.Path, out var table)) continue;
            var t = DbTableDecoder.Decode(PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!, table, schema);
            if (t.Status != TableDecodeStatus.Ok || TableTsv.Write(table, t.Version, e.Path, t.Definition!, t.Rows).Text is not { } text) continue;
            var file = Path.Combine(dir.Path, "tsv", e.Path.Replace('/', Path.DirectorySeparatorChar) + ".tsv");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text, new UTF8Encoding(false));
            expected[e.Path] = t;
        }

        var pack = Path.Combine(dir.Path, "built.pack");
        RealData.RpfmCreatePack(pack);
        RealData.RpfmAddFolder(pack, Path.Combine(dir.Path, "tsv", "db"), "db", SchemaPath);
        var built = PackReader.ReadIndex(pack).Index!;
        foreach (var (path, t) in expected)
        {
            var entry = built.Find(path);
            Assert.True(entry is not null, $"{path} is missing in the pack rpfm_cli built");
            var theirs = DbTableDecoder.Decode(PackReader.ReadEntry(built, entry!, CancellationToken.None).Bytes!, t.TableName, schema);
            Assert.True(theirs.Status == TableDecodeStatus.Ok && theirs.Version == t.Version, path);
            Assert.True(DbTableEncoder.Encode(t.Definition!, t.Version, null, t.Rows).AsSpan().SequenceEqual(
                DbTableEncoder.Encode(theirs.Definition!, theirs.Version, null, theirs.Rows)), path);
        }
        Assert.True(expected.Count > 1000);
    }
}
