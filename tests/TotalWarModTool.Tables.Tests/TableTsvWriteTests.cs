using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables.Tests;

public class TableTsvWriteTests
{
    private static FieldDefinition F(string name, FieldType type) => new(name, type, type.ToString(), false, null);

    [Fact] public void Writes_header_meta_line_and_rows()
    {
        var def = new TableDefinition(3, [F("key", FieldType.StringU8), F("flag", FieldType.Boolean), F("n", FieldType.I32), F("tint", FieldType.ColourRgb), F("maybe", FieldType.OptionalStringU8)]);
        var r = TableTsv.Write("units_tables", 3, "db/units_tables/mod", def, [["unit_a", true, -5, "4080FF", "yes"], ["ünit", false, 0, "000000", ""]]);
        Assert.Null(r.Detail);
        Assert.Equal("key\tflag\tn\ttint\tmaybe\n#units_tables;3;db/units_tables/mod\t\t\t\t\nunit_a\ttrue\t-5\t4080FF\tyes\nünit\tfalse\t0\t000000\t\n", r.Text);
    }

    [Fact] public void Floats_use_the_shortest_exact_form()
    {
        var def = new TableDefinition(0, [F("a", FieldType.F32), F("b", FieldType.F64)]);
        var text = TableTsv.Write("t_tables", 0, "db/t_tables/x", def, [[0.1f, 0.1], [2.0000002E-06f, 1.0], [375f, -0.5]]).Text!;
        Assert.Equal("a\tb\n#t_tables;0;db/t_tables/x\t\n0.1\t0.1\n2.0000002E-06\t1\n375\t-0.5\n", text);
    }

    [Fact] public void Tab_and_line_break_are_escaped_and_a_backslash_stays()
    {
        var def = new TableDefinition(0, [F("s", FieldType.StringU8)]);
        var text = TableTsv.Write("t_tables", 0, "p", def, [["a\tb\nc"], [@"ui\new\table.png"]]).Text!;
        Assert.Equal("s\n#t_tables;0;p\na\\\\tb\\\\nc\nui\\new\\table.png\n", text);
    }

    [Fact] public void Colour_groups_become_one_hex_column_at_the_end()
    {
        var def = new TableDefinition(1, [F("name", FieldType.StringU8), F("col_r", FieldType.I32), F("col_g", FieldType.I32), F("col_b", FieldType.I32),
            F("r", FieldType.F64), F("g", FieldType.F64), F("b", FieldType.F64), F("bg_red", FieldType.I16), F("bg_green", FieldType.I16), F("bg_blue", FieldType.I16), F("other", FieldType.I32)]);
        var text = TableTsv.Write("t_tables", 1, "p", def, [["x", 255, 128, 0, 1.0, 2.0, 3.0, (short)16, (short)32, (short)48, 7]]).Text!;
        Assert.Equal("name\tother\tcol_hex\tunnamed colour group_1\tbg_hex\n#t_tables;1;p\t\t\t\t\nx\t7\tFF8000\t010203\t102030\n", text);
    }

    [Fact] public void An_incomplete_or_non_numeric_triple_stays_separate()
    {
        var def = new TableDefinition(1, [F("col_r", FieldType.I32), F("col_g", FieldType.I32), F("x_r", FieldType.StringU8), F("x_g", FieldType.I32), F("x_b", FieldType.I32)]);
        Assert.StartsWith("col_r\tcol_g\tx_r\tx_g\tx_b\n", TableTsv.Write("t_tables", 1, "p", def, []).Text!);
    }

    [Theory]
    [InlineData(256)] [InlineData(-1)]
    public void A_channel_outside_0_to_255_cannot_be_written(int channel)
    {
        var def = new TableDefinition(1, [F("r", FieldType.I32), F("g", FieldType.I32), F("b", FieldType.I32)]);
        var r = TableTsv.Write("t_tables", 1, "p", def, [[1, 2, 3], [channel, 0, 0]]);
        Assert.Null(r.Text);
        Assert.Contains("row 1", r.Detail);
    }

    [Fact] public void A_fractional_channel_cannot_be_written()
    {
        var def = new TableDefinition(1, [F("r", FieldType.F64), F("g", FieldType.F64), F("b", FieldType.F64)]);
        Assert.Null(TableTsv.Write("t_tables", 1, "p", def, [[0.5, 0.0, 0.0]]).Text);
    }

    [Theory]
    [InlineData("a\rb")] [InlineData(@"a\\tb")] [InlineData(@"a\\nb")]
    public void A_text_the_dialect_cannot_carry_cannot_be_written(string value)
    {
        var def = new TableDefinition(1, [F("s", FieldType.StringU8)]);
        var r = TableTsv.Write("t_tables", 1, "p", def, [[value]]);
        Assert.Null(r.Text);
        Assert.False(string.IsNullOrWhiteSpace(r.Detail));
    }

    [Fact] public void A_text_with_an_unpaired_surrogate_cannot_be_written()
    {
        var def = new TableDefinition(1, [F("s", FieldType.StringU16)]);
        var r = TableTsv.Write("t_tables", 1, "p", def, [["a\uD800b"]]);
        Assert.Null(r.Text);
        Assert.False(string.IsNullOrWhiteSpace(r.Detail));
    }
}
