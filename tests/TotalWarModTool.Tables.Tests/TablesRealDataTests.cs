using System.Globalization;
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
        int compared = 0, colourCells = 0, mergedCells = 0;
        foreach (var e in index.Entries)
        {
            var tsvPath = Path.Combine(dir.Path, e.Path + ".tsv");
            if (!DbTablePath.TryGetTableName(e.Path, out var table) || !File.Exists(tsvPath)) continue;
            var t = DbTableDecoder.Decode(PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!, table, schema);
            Assert.True(t.Status == TableDecodeStatus.Ok, $"{e.Path}: {t.Status} {t.Detail}");
            var tsv = RpfmTsv.Read(tsvPath);
            Assert.Equal(tsv.Rows.Count, t.Rows.Count);
            // Every field must find its TSV column by name. A ColourRGB field is one column with the RRGGBB hex text and is
            // compared like every other field (case-insensitive). The r/g/b triples (any numeric type, values 0..255) have no
            // column of their own: rpfm merges them into one "<prefix>_hex" or "unnamed colour group_N" column, so the three
            // channels are merged into RRGGBB here before the comparison. rpfm keeps only the low byte of a channel (measured: a
            // channel value of 257 shows as 01), so the comparison does the same.
            var fields = t.Definition!.Fields;
            var columnNames = tsv.Columns.ToList();
            var withoutColumn = fields.Select(f => f.Name).Where(n => !columnNames.Contains(n)).ToHashSet();
            for (int field = 0; field < fields.Count; field++)
            {
                int c = columnNames.IndexOf(fields[field].Name);
                if (c < 0) continue;
                bool colour = fields[field].Type == FieldType.ColourRgb;
                for (int r = 0; r < tsv.Rows.Count; r++)
                {
                    Assert.True(RpfmTsv.CellEquals(t.Rows[r][field], tsv.Rows[r][c], ignoreCase: colour), $"{e.Path} row {r} column {tsv.Columns[c]}");
                    if (colour) colourCells++;
                }
            }

            var merged = columnNames.Where(n => !fields.Any(f => f.Name == n)).ToList();
            var unnamedGroups = merged.Count(n => n.StartsWith("unnamed colour group", StringComparison.Ordinal));
            Assert.True(unnamedGroups <= 1, $"{e.Path}: several unnamed colour groups cannot be paired by name");
            foreach (var column in merged)
            {
                bool named = column.EndsWith("_hex", StringComparison.Ordinal);
                Assert.True(named || column.StartsWith("unnamed colour group", StringComparison.Ordinal), $"{e.Path}: unexpected TSV column '{column}'");
                var prefix = named ? column[..^"_hex".Length] + "_" : string.Empty;
                var channels = new int[3];
                for (int ch = 0; ch < 3; ch++)
                {
                    var names = new[] { prefix + "rgb"[ch], prefix + new[] { "red", "green", "blue" }[ch] };
                    var found = fields.Select((f, i) => (f, i)).Where(x => names.Contains(x.f.Name) && withoutColumn.Contains(x.f.Name)).ToList();
                    Assert.True(found.Count == 1, $"{e.Path}: column '{column}' has {found.Count} candidates for channel {ch}");
                    channels[ch] = found[0].i;
                    withoutColumn.Remove(found[0].f.Name);
                }

                int tc = columnNames.IndexOf(column);
                for (int r = 0; r < tsv.Rows.Count; r++)
                {
                    var hex = string.Concat(channels.Select(i => (((int)Math.Round(Convert.ToDouble(t.Rows[r][i], CultureInfo.InvariantCulture))) & 0xFF).ToString("X2", CultureInfo.InvariantCulture)));
                    Assert.True(string.Equals(hex, tsv.Rows[r][tc], StringComparison.OrdinalIgnoreCase), $"{e.Path} row {r} column {column}: {hex} vs {tsv.Rows[r][tc]}");
                    mergedCells++;
                }
            }

            Assert.True(withoutColumn.Count == 0, $"{e.Path}: fields without a TSV column: {string.Join(",", withoutColumn)}");            compared++;
        }
        Assert.True(compared > 1000);
        Assert.True(colourCells > 0, "no ColourRGB cell was compared");
        Assert.True(mergedCells > 0, "no merged r/g/b colour cell was compared");
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
