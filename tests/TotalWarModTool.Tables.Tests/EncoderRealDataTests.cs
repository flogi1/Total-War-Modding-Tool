using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables.Tests;

public class EncoderRealDataTests
{
    [RealDataFact] public void Every_table_of_db_pack_encodes_back_to_its_bytes()
    {
        var schema = SchemaLoader.Load(Path.Combine(RepoRoot.Find(), "schemas", "schema_wh3.ron")).Schema!;
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "db.pack")).Index!;
        int same = 0;
        foreach (var e in index.Entries)
        {
            if (!DbTablePath.TryGetTableName(e.Path, out var table)) continue;
            var bytes = PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!;
            var t = DbTableDecoder.Decode(bytes, table, schema);
            if (t.Status != TableDecodeStatus.Ok) continue;
            Assert.True(bytes.AsSpan().SequenceEqual(DbTableEncoder.Encode(t.Definition!, t.Version, DbTableDecoder.ReadGuid(bytes), t.Rows)), e.Path);
            same++;
        }
        Assert.True(same > 1000);
    }

    [RealDataFact] public void Every_loc_of_local_en_pack_encodes_back_to_its_bytes()
    {
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "local_en.pack")).Index!;
        int same = 0;
        foreach (var e in index.Entries.Where(e => e.Path.EndsWith(".loc", StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!;
            var d = LocDecoder.Decode(bytes);
            Assert.True(d.Status == LocDecodeStatus.Ok, e.Path);
            Assert.True(bytes.AsSpan().SequenceEqual(LocEncoder.Encode(d.Rows)), e.Path);
            same++;
        }
        Assert.True(same > 100);
    }
}
