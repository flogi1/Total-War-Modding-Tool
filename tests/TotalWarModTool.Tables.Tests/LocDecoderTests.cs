namespace TotalWarModTool.Tables.Tests;

public class LocDecoderTests
{
    private static byte[] Loc(int version, uint count, Action<TableBytes> rows)
    {
        var b = new TableBytes().Raw(0xFF, 0xFE, 0x4C, 0x4F, 0x43, 0x00).I32(version).Raw(BitConverter.GetBytes(count));
        rows(b); return b.Build();
    }

    [Fact] public void Decodes_rows()
    {
        var d = LocDecoder.Decode(Loc(1, 2, b => b.Str16("units_name_a").Str16("Schwertkämpfer").Bool(true).Str16("units_name_b").Str16("").Bool(false)));
        Assert.Equal(LocDecodeStatus.Ok, d.Status);
        Assert.Equal([new LocRow("units_name_a", "Schwertkämpfer", true), new LocRow("units_name_b", "", false)], d.Rows);
    }

    [Fact] public void Empty_loc_is_ok() => Assert.Equal((LocDecodeStatus.Ok, 0), (LocDecoder.Decode(Loc(1, 0, _ => { })).Status, LocDecoder.Decode(Loc(1, 0, _ => { })).Rows.Count));

    [Fact] public void Row_count_that_the_minimum_row_size_cannot_hold_fails_without_rows()
    {
        // 4 rows need at least 20 bytes; 19 remain.
        var d = LocDecoder.Decode(Loc(1, 4, b => b.Raw(new byte[19])));
        Assert.Equal(LocDecodeStatus.Failed, d.Status);
        Assert.Empty(d.Rows);
        Assert.Contains("Row count 4", d.Detail);
    }

    [Fact] public void Row_count_above_the_cell_budget_fails_without_rows()
    {
        var data = Loc(1, 2, b => b.Str16("a").Str16("b").Bool(false).Str16("c").Str16("d").Bool(true));   // 2 rows = 6 cells
        Assert.Equal(LocDecodeStatus.Ok, LocDecoder.Decode(data, maxCells: 6).Status);
        var d = LocDecoder.Decode(data, maxCells: 5);
        Assert.Equal(LocDecodeStatus.Failed, d.Status);
        Assert.Empty(d.Rows);
        Assert.Contains("2", d.Detail); Assert.Contains("5", d.Detail);
    }

    [Fact] public void Other_version_wrong_marker_bad_flag_leftover_and_huge_count_fail()
    {
        Assert.Equal(LocDecodeStatus.Failed, LocDecoder.Decode(Loc(2, 0, _ => { })).Status);
        Assert.Equal(LocDecodeStatus.Failed, LocDecoder.Decode(new TableBytes().Raw(0xFF, 0xFE, 0x4C, 0x4F, 0x58, 0x00).I32(1).Raw(0, 0, 0, 0).Build()).Status);
        Assert.Equal(LocDecodeStatus.Failed, LocDecoder.Decode(Loc(1, 1, b => b.Str16("k").Str16("t").Raw(7))).Status);
        Assert.Equal(LocDecodeStatus.Failed, LocDecoder.Decode(Loc(1, 1, b => b.Str16("k").Str16("t").Bool(true).Raw(0))).Status);
        Assert.Equal(LocDecodeStatus.Failed, LocDecoder.Decode(Loc(1, uint.MaxValue, _ => { })).Status);
        Assert.Equal(LocDecodeStatus.Failed, LocDecoder.Decode([]).Status);
        Assert.All(new[] { LocDecoder.Decode([]), LocDecoder.Decode(Loc(2, 0, _ => { })) }, d => { Assert.Empty(d.Rows); Assert.False(string.IsNullOrWhiteSpace(d.Detail)); });
    }
}
