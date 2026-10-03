using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables.Tests;

public class TableTsvReadTests
{
    private static FieldDefinition F(string name, FieldType type) => new(name, type, type.ToString(), false, null);
    private static GameSchema Schema(string table, int version, params FieldDefinition[] fields) =>
        new(new Dictionary<string, IReadOnlyList<TableDefinition>> { [table] = [new TableDefinition(version, fields)] });

    private static readonly FieldDefinition[] Fields =
    [
        F("key", FieldType.StringU8), F("flag", FieldType.Boolean), F("small", FieldType.I16), F("n", FieldType.I32), F("big", FieldType.I64),
        F("ratio", FieldType.F32), F("exact", FieldType.F64), F("tint", FieldType.ColourRgb), F("wide", FieldType.StringU16), F("maybe", FieldType.OptionalStringU8),
        F("col_r", FieldType.I32), F("col_g", FieldType.F64), F("col_b", FieldType.I16),
    ];
    private static readonly GameSchema TestSchema = Schema("t_tables", 2, Fields);
    private static readonly TableDefinition Def = TestSchema.Find("t_tables", 2)!;

    [Fact] public void Reads_what_Write_wrote()
    {
        object?[][] rows =
        [
            ["a\tb\nc", true, (short)-2, 70000, 5_000_000_000L, 0.1f, 0.1, "4080FF", "wide 😀", "yes", 255, 128.0, (short)0],
            [@"ui\new\table.png", false, (short)7, -1, -9L, 2.0000002E-06f, -0.5, "1FFFFFF", "", "", 0, 0.0, (short)255],
        ];
        var text = TableTsv.Write("t_tables", 2, "db/t_tables/mod", Def, rows).Text!;
        var read = TableTsv.Read(text, TestSchema);
        Assert.Null(read.Problem);
        Assert.Equal(("t_tables", 2, "db/t_tables/mod"), (read.Table!.TableName, read.Table.Version, read.Table.PackPath));
        Assert.Same(Def, read.Table.Definition);
        Assert.Equal(rows, read.Table.Rows);
    }

    [Fact] public void Extreme_values_round_trip()
    {
        var def = new TableDefinition(0, [F("a", FieldType.I16), F("b", FieldType.I32), F("c", FieldType.I64), F("d", FieldType.F32), F("e", FieldType.F64)]);
        var schema = Schema("x_tables", 0, def.Fields.ToArray());
        object?[][] rows =
        [
            [short.MinValue, int.MinValue, long.MinValue, float.NaN, double.PositiveInfinity],
            [short.MaxValue, int.MaxValue, long.MaxValue, float.Epsilon, double.MaxValue],
            [(short)0, 0, 0L, -0f, double.NegativeInfinity],
        ];
        var read = TableTsv.Read(TableTsv.Write("x_tables", 0, "p", def, rows).Text!, schema);
        var back = DbTableEncoder.Encode(def, 0, null, read.Table!.Rows);
        Assert.Equal(DbTableEncoder.Encode(def, 0, null, rows), back);   // compares bits, also for NaN and -0
    }

    [Fact] public void Read_accepts_crlf_bom_missing_final_newline_and_any_column_order()
    {
        var schema = Schema("t_tables", 1, F("a", FieldType.I32), F("s", FieldType.StringU8), F("r", FieldType.I32), F("g", FieldType.I32), F("b", FieldType.I32), F("on", FieldType.Boolean));
        var text = "﻿unnamed colour group_1\ton\ts\ta\r\n#t_tables;1;p\t\t\t\r\n0A0B0C\tTRUE\tx\t5\r\nffffff\tFalse\t\t-1";
        var read = TableTsv.Read(text, schema);
        Assert.Null(read.Problem);
        Assert.Equal(new object?[][] { [5, "x", 10, 11, 12, true], [-1, "", 255, 255, 255, false] }, read.Table!.Rows);
    }

    [Fact] public void An_empty_table_has_no_rows()
    {
        var read = TableTsv.Read(TableTsv.Write("t_tables", 2, "p", Def, []).Text!, TestSchema);
        Assert.Null(read.Problem);
        Assert.Empty(read.Table!.Rows);
    }

    private static readonly GameSchema Small = Schema("t_tables", 1, F("a", FieldType.I16), F("s", FieldType.StringU8));

    [Theory]
    [InlineData("", TsvProblem.MissingMetaLine, 2)]
    [InlineData("a\ts\n", TsvProblem.MissingMetaLine, 2)]
    [InlineData("a\ts\n1\tx\n", TsvProblem.MissingMetaLine, 2)]
    [InlineData("a\ts\n#t_tables;1\t\n", TsvProblem.InvalidMetaLine, 2)]
    [InlineData("a\ts\n#t_tables;one;p\t\n", TsvProblem.InvalidMetaLine, 2)]
    [InlineData("a\ts\n#other_tables;1;p\t\n", TsvProblem.TableNotInSchema, 2)]
    [InlineData("a\ts\n#t_tables;9;p\t\n", TsvProblem.VersionNotInSchema, 2)]
    [InlineData("a\ts\ta\n#t_tables;1;p\t\t\n", TsvProblem.DuplicateColumn, 1)]
    [InlineData("a\ts\tzzz\n#t_tables;1;p\t\t\n", TsvProblem.UnknownColumn, 1)]
    [InlineData("a\n#t_tables;1;p\n", TsvProblem.MissingColumn, 1)]
    [InlineData("a\ts\n#t_tables;1;p\t\n1\tx\n2\n", TsvProblem.WrongCellCount, 4)]
    [InlineData("a\ts\n#t_tables;1;p\t\n1\tx\ty\n", TsvProblem.WrongCellCount, 3)]
    [InlineData("a\ts\n#t_tables;1;p\t\n1\tx\n\n", TsvProblem.WrongCellCount, 4)]
    [InlineData("a\ts\n#t_tables;1;p\t\n1\tx\nabc\tx\n", TsvProblem.InvalidValue, 4)]
    [InlineData("a\ts\n#t_tables;1;p\t\n70000\tx\n", TsvProblem.InvalidValue, 3)]
    [InlineData("a\ts\n#t_tables;1;p\t\n1.5\tx\n", TsvProblem.InvalidValue, 3)]
    [InlineData("a\ts\n#t_tables;1;p\t\n\tx\n", TsvProblem.InvalidValue, 3)]
    public void Read_reports_the_problem_with_its_line(string text, TsvProblem problem, int line)
    {
        var read = TableTsv.Read(text, Small);
        Assert.Null(read.Table);
        Assert.Equal((problem, line), (read.Problem!.Problem, read.Problem.Line));
        Assert.False(string.IsNullOrWhiteSpace(read.Problem.Detail));
    }

    [Theory]
    [InlineData(FieldType.Boolean, "yes")] [InlineData(FieldType.I32, "1e3")] [InlineData(FieldType.I64, "9223372036854775808")]
    [InlineData(FieldType.F32, "1,5")] [InlineData(FieldType.F64, "abc")] [InlineData(FieldType.ColourRgb, "GG0000")]
    [InlineData(FieldType.ColourRgb, "")] [InlineData(FieldType.ColourRgb, "123456789")]
    public void Read_rejects_a_value_that_does_not_fit_the_type(FieldType type, string value)
    {
        var read = TableTsv.Read($"v\n#t_tables;1;p\n{value}\n", Schema("t_tables", 1, F("v", type)));
        Assert.Equal((TsvProblem.InvalidValue, 3), (read.Problem!.Problem, read.Problem.Line));
    }

    [Fact] public void Read_rejects_a_bad_channel_value_a_too_long_text_and_an_unsupported_field()
    {
        var colours = Schema("t_tables", 1, F("r", FieldType.I32), F("g", FieldType.I32), F("b", FieldType.I32));
        Assert.Equal(TsvProblem.InvalidValue, TableTsv.Read("unnamed colour group_1\n#t_tables;1;p\nFF80\n", colours).Problem!.Problem);
        var text = Schema("t_tables", 1, F("s", FieldType.StringU8));
        Assert.Null(TableTsv.Read($"s\n#t_tables;1;p\n{new string('a', 65535)}\n", text).Problem);
        Assert.Equal(TsvProblem.InvalidValue, TableTsv.Read($"s\n#t_tables;1;p\n{new string('ü', 32768)}\n", text).Problem!.Problem);
        var unsupported = Schema("t_tables", 1, new FieldDefinition("x", FieldType.Unsupported, "SequenceU32", false, null));
        var problem = TableTsv.Read("x\n#t_tables;1;p\n", unsupported).Problem!;
        Assert.Equal((TsvProblem.UnsupportedFieldType, 2), (problem.Problem, problem.Line));
    }

    [Fact] public void Read_rejects_a_text_with_an_unpaired_surrogate()
    {
        var read = TableTsv.Read("s\n#t_tables;1;p\na\uD800b\n", Schema("t_tables", 1, F("s", FieldType.StringU16)));
        Assert.Null(read.Table);
        Assert.Equal((TsvProblem.InvalidValue, 3), (read.Problem!.Problem, read.Problem.Line));
    }
}
