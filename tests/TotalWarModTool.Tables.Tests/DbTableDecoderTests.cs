using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables.Tests;

public class DbTableDecoderTests
{
    private static FieldDefinition F(string name, FieldType type, bool key = false) => new(name, type, type.ToString(), key, null);
    private static GameSchema Schema(params (string Table, int Version, FieldDefinition[] Fields)[] defs) =>
        new(defs.GroupBy(d => d.Table).ToDictionary(g => g.Key, g => (IReadOnlyList<TableDefinition>)g.Select(d => new TableDefinition(d.Version, d.Fields)).ToList()));

    private static readonly FieldDefinition[] AllTypes =
    [
        F("key", FieldType.StringU8, key: true), F("flag", FieldType.Boolean), F("small", FieldType.I16), F("count", FieldType.I32),
        F("big", FieldType.I64), F("ratio", FieldType.F32), F("exact", FieldType.F64), F("tint", FieldType.ColourRgb),
        F("wide", FieldType.StringU16), F("maybe", FieldType.OptionalStringU8),
    ];

    [Fact] public void Decodes_every_supported_field_type()
    {
        var data = new TableBytes().Guid().Version(3).Rows(2)
            .Str8("ünit_a").Bool(true).I16(-2).I32(70000).I64(5_000_000_000).F32(1.5f).F64(2.25).Colour(0x004080FF).Str16("wide ü").OptStr8("yes")
            .Str8("unit_b").Bool(false).I16(7).I32(-1).I64(-9).F32(0f).F64(-0.5).Colour(0).Str16("").OptStr8(null)
            .Build();
        var t = DbTableDecoder.Decode(data, "units_tables", Schema(("units_tables", 3, AllTypes)));
        Assert.Equal((TableDecodeStatus.Ok, "units_tables", 3, true), (t.Status, t.TableName, t.Version, t.HasGuid));
        Assert.Equal(new object?[] { "ünit_a", true, (short)-2, 70000, 5_000_000_000L, 1.5f, 2.25, "4080FF", "wide ü", "yes" }, t.Rows[0]);
        Assert.Equal(new object?[] { "unit_b", false, (short)7, -1, -9L, 0f, -0.5, "000000", "", "" }, t.Rows[1]);
        Assert.Equal(10, t.Definition!.Fields.Count);
    }

    [Fact] public void Without_markers_the_version_is_zero()
    {
        var data = new TableBytes().Rows(1).Str8("a").Build();
        var t = DbTableDecoder.Decode(data, "x_tables", Schema(("x_tables", 0, [F("key", FieldType.StringU8)])));
        Assert.Equal((TableDecodeStatus.Ok, 0, false, 1), (t.Status, t.Version, t.HasGuid, t.Rows.Count));
    }

    [Fact] public void Picks_the_definition_of_the_file_version()
    {
        var schema = Schema(("x_tables", 1, [F("key", FieldType.StringU8)]), ("x_tables", 2, [F("key", FieldType.StringU8), F("n", FieldType.I32)]));
        var t = DbTableDecoder.Decode(new TableBytes().Version(2).Rows(1).Str8("a").I32(5).Build(), "X_TABLES", schema);
        Assert.Equal((TableDecodeStatus.Ok, 2), (t.Status, t.Definition!.Fields.Count));
    }

    [Fact] public void Empty_table_is_ok()
    {
        var t = DbTableDecoder.Decode(new TableBytes().Guid().Version(1).Rows(0).Build(), "x_tables", Schema(("x_tables", 1, [F("key", FieldType.StringU8)])));
        Assert.Equal((TableDecodeStatus.Ok, 0), (t.Status, t.Rows.Count));
    }

    [Fact] public void Unknown_table_and_unknown_version()
    {
        var schema = Schema(("x_tables", 1, [F("key", FieldType.StringU8)]));
        var data = new TableBytes().Version(9).Rows(0).Build();
        Assert.Equal(TableDecodeStatus.TableNotInSchema, DbTableDecoder.Decode(data, "y_tables", schema).Status);
        var t = DbTableDecoder.Decode(data, "x_tables", schema);
        Assert.Equal((TableDecodeStatus.VersionNotInSchema, 9), (t.Status, t.Version));
        Assert.Contains("9", t.Detail); Assert.Empty(t.Rows);
    }

    [Fact] public void Unsupported_field_type_is_reported_by_name()
    {
        var schema = Schema(("x_tables", 0, [F("key", FieldType.StringU8), new FieldDefinition("maybe", FieldType.Unsupported, "OptionalI32", false, null)]));
        var t = DbTableDecoder.Decode(new TableBytes().Rows(0).Build(), "x_tables", schema);
        Assert.Equal(TableDecodeStatus.UnsupportedFieldType, t.Status);
        Assert.Contains("maybe", t.Detail); Assert.Contains("OptionalI32", t.Detail);
    }

    [Fact] public void Wrong_layout_fails_without_partial_rows()
    {
        // file was written with (string, bool); the schema expects (string, i32, bool)
        var schema = Schema(("x_tables", 1, [F("key", FieldType.StringU8), F("n", FieldType.I32), F("flag", FieldType.Boolean)]));
        var data = new TableBytes().Version(1).Rows(2).Str8("first").Bool(true).Str8("second").Bool(false).Build();
        var t = DbTableDecoder.Decode(data, "x_tables", schema);
        Assert.Equal(TableDecodeStatus.Failed, t.Status);
        Assert.Empty(t.Rows); Assert.NotNull(t.Definition); Assert.False(string.IsNullOrWhiteSpace(t.Detail));
    }

    [Fact] public void Boolean_that_is_not_0_or_1_fails()
    {
        var schema = Schema(("x_tables", 0, [F("flag", FieldType.Boolean)]));
        var t = DbTableDecoder.Decode(new TableBytes().Rows(1).Raw(2).Build(), "x_tables", schema);
        Assert.Equal(TableDecodeStatus.Failed, t.Status);
        Assert.Contains("flag", t.Detail);
    }

    [Fact] public void Bytes_after_the_last_row_fail()
    {
        var schema = Schema(("x_tables", 0, [F("flag", FieldType.Boolean)]));
        var t = DbTableDecoder.Decode(new TableBytes().Rows(1).Bool(true).Raw(0xAA).Build(), "x_tables", schema);
        Assert.Equal(TableDecodeStatus.Failed, t.Status);
        Assert.Empty(t.Rows);
    }

    [Fact] public void String_that_overruns_the_table_fails()
    {
        var schema = Schema(("x_tables", 0, [F("key", FieldType.StringU8)]));
        var data = new TableBytes().Rows(1).Raw(0xFF, 0xFF, 0x41).Build();   // claims 65535 bytes, has 1
        Assert.Equal(TableDecodeStatus.Failed, DbTableDecoder.Decode(data, "x_tables", schema).Status);
    }

    [Fact] public void Huge_row_count_fails_without_allocating()
    {
        var schema = Schema(("x_tables", 0, [F("key", FieldType.StringU8)]));
        var t = DbTableDecoder.Decode(new TableBytes().Rows(uint.MaxValue).Build(), "x_tables", schema);
        Assert.Equal(TableDecodeStatus.Failed, t.Status);
    }

    [Fact] public void Huge_row_count_of_a_definition_without_fields_fails_without_allocating()
    {
        var t = DbTableDecoder.Decode(new TableBytes().Rows(uint.MaxValue).Build(), "x_tables", Schema(("x_tables", 0, [])));
        Assert.Equal(TableDecodeStatus.Failed, t.Status);
        Assert.Empty(t.Rows);
    }

    [Fact] public void Row_count_that_the_minimum_row_size_cannot_hold_fails_without_rows()
    {
        // 3 rows of (i64, i64) need at least 48 bytes; 20 remain. 20 bytes would pass a one-byte-per-row check.
        var schema = Schema(("x_tables", 0, [F("a", FieldType.I64), F("b", FieldType.I64)]));
        var t = DbTableDecoder.Decode(new TableBytes().Rows(3).Raw(new byte[20]).Build(), "x_tables", schema);
        Assert.Equal(TableDecodeStatus.Failed, t.Status);
        Assert.Empty(t.Rows);
        Assert.Contains("Row count 3", t.Detail);
        Assert.Contains("16 bytes per row", t.Detail);
    }

    [Fact] public void Row_count_above_the_cell_budget_fails_without_rows()
    {
        var schema = Schema(("x_tables", 0, [F("a", FieldType.Boolean), F("b", FieldType.Boolean)]));
        var data = new TableBytes().Rows(3).Raw(new byte[6]).Build();   // would decode fine: 3 rows x 2 fields = 6 cells
        Assert.Equal(TableDecodeStatus.Ok, DbTableDecoder.Decode(data, "x_tables", schema, maxCells: 6).Status);
        var t = DbTableDecoder.Decode(data, "x_tables", schema, maxCells: 5);
        Assert.Equal(TableDecodeStatus.Failed, t.Status);
        Assert.Empty(t.Rows);
        Assert.Contains("3", t.Detail); Assert.Contains("2 fields", t.Detail); Assert.Contains("5", t.Detail);
    }

    [Fact] public void The_cell_budget_is_50_million() => Assert.Equal(50_000_000L, DbTableDecoder.MaxCells);

    [Fact] public void Rows_of_a_definition_without_fields_share_the_empty_array()
    {
        var t = DbTableDecoder.Decode(new TableBytes().Rows(2).Build(), "x_tables", Schema(("x_tables", 0, [])));
        Assert.Equal(TableDecodeStatus.Ok, t.Status);
        Assert.Equal(2, t.Rows.Count);
        Assert.Same(Array.Empty<object?>(), t.Rows[0]);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFD, 0xFE, 0xFC, 0xFF, 0x10 })]              // GUID marker cut off
    [InlineData(new byte[] { 0xFC, 0xFD, 0xFE, 0xFF, 0x01, 0x00 })]        // version marker cut off
    [InlineData(new byte[] { 0x01, 0x05, 0x00 })]                          // row count cut off
    public void Truncated_header_fails(byte[] data) =>
        Assert.Equal(TableDecodeStatus.Failed, DbTableDecoder.Decode(data, "x_tables", new GameSchema(new Dictionary<string, IReadOnlyList<TableDefinition>>
            { ["x_tables"] = [new TableDefinition(0, [new FieldDefinition("key", FieldType.StringU8, "StringU8", true, null)])] })).Status);

    [Theory]
    [InlineData("db/units_tables/data__", true, "units_tables")]
    [InlineData("DB/Units_Tables/my mod file", true, "Units_Tables")]
    [InlineData("db/units_tables", false, "")]
    [InlineData("text/db/units.loc", false, "")]
    [InlineData("db/a/b/c", false, "")]
    public void TryGetTableName(string path, bool expected, string table)
    {
        Assert.Equal(expected, DbTablePath.TryGetTableName(path, out var name));
        Assert.Equal(table, name);
    }
}
