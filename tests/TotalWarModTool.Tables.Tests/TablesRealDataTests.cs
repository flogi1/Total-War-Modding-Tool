using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables.Tests;

public class TablesRealDataTests
{
    private static GameSchema Wh3Schema() => SchemaLoader.Load(Path.Combine(RepoRoot.Find(), "schemas", "schema_wh3.ron")).Schema!;
    private static string SchemaPath => Path.Combine(RepoRoot.Find(), "schemas", "schema_wh3.ron");

    [RealDataFact] public void Every_table_of_db_pack_is_ok_or_has_a_diagnosis()
    {
        var schema = Wh3Schema();
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "db.pack")).Index!;
        int ok = 0, total = 0;
        foreach (var e in index.Entries)
        {
            if (!DbTablePath.TryGetTableName(e.Path, out var table)) continue;
            total++;
            var t = DbTableDecoder.Decode(PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!, table, schema);
            if (t.Status == TableDecodeStatus.Ok) { ok++; Assert.All(t.Rows, row => Assert.Equal(t.Definition!.Fields.Count, row.Length)); }
            else { Assert.Empty(t.Rows); if (t.Status != TableDecodeStatus.TableNotInSchema) Assert.False(string.IsNullOrWhiteSpace(t.Detail), e.Path); }
        }
        Assert.True(total > 1000);
        Assert.True(ok >= total * 0.95, $"{ok} of {total} tables decoded");
    }

    [RpfmFact] public void Db_pack_tables_match_rpfm_cli_tsv()
    {
        using var dir = new TempDir();
        var pack = Path.Combine(RealData.Wh3Data, "db.pack");
        RealData.RpfmExtractFolder(pack, "db", dir.Path, SchemaPath);
        var schema = Wh3Schema();
        var index = PackReader.ReadIndex(pack).Index!;
        int compared = 0;
        foreach (var e in index.Entries)
        {
            var tsvPath = Path.Combine(dir.Path, e.Path + ".tsv");
            if (!DbTablePath.TryGetTableName(e.Path, out var table) || !File.Exists(tsvPath)) continue;
            var t = DbTableDecoder.Decode(PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!, table, schema);
            Assert.True(t.Status == TableDecodeStatus.Ok, $"{e.Path}: {t.Status} {t.Detail}");
            var tsv = RpfmTsv.Read(tsvPath);
            Assert.Equal(tsv.Rows.Count, t.Rows.Count);
            // rpfm merges colour columns into one display column; compare every TSV column that exists in the definition
            for (int c = 0; c < tsv.Columns.Count; c++)
            {
                int field = t.Definition!.Fields.ToList().FindIndex(f => f.Name == tsv.Columns[c]);
                if (field < 0) continue;
                bool colour = t.Definition.Fields[field].Type == FieldType.ColourRgb;
                for (int r = 0; r < tsv.Rows.Count; r++)
                    Assert.True(RpfmTsv.CellEquals(t.Rows[r][field], tsv.Rows[r][c], ignoreCase: colour), $"{e.Path} row {r} column {tsv.Columns[c]}");
            }
            compared++;
        }
        Assert.True(compared > 1000);
    }

    [RpfmFact] public void Loc_files_match_rpfm_cli_tsv()
    {
        using var dir = new TempDir();
        var pack = Path.Combine(RealData.Wh3Data, "local_en.pack");
        RealData.RpfmExtractFolder(pack, "/", dir.Path, SchemaPath);
        var index = PackReader.ReadIndex(pack).Index!;
        int compared = 0;
        foreach (var e in index.Entries.Where(e => e.Path.EndsWith(".loc", StringComparison.OrdinalIgnoreCase)))
        {
            var d = LocDecoder.Decode(PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!);
            Assert.True(d.Status == LocDecodeStatus.Ok, $"{e.Path}: {d.Detail}");
            var tsv = RpfmTsv.Read(Path.Combine(dir.Path, e.Path + ".tsv"));
            Assert.Equal(tsv.Rows.Count, d.Rows.Count);
            for (int r = 0; r < d.Rows.Count; r++)
                Assert.True(tsv.Rows[r][0] == d.Rows[r].Key && RpfmTsv.CellEquals(d.Rows[r].Text, tsv.Rows[r][1]) && RpfmTsv.CellEquals(d.Rows[r].Tooltip, tsv.Rows[r][2]), $"{e.Path} row {r}");
            compared++;
        }
        Assert.True(compared > 100);
    }
}
