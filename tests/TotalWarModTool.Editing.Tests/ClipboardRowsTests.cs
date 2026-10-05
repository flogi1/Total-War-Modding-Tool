using TotalWarModTool.Editing;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Editing.Tests;

public class ClipboardRowsTests
{
    private const string Pack = @"db\test_tables\data__";
    private const string TableName = "test_tables";

    // Column indexes of the test definition.
    private const int Num = 2;
    private const int Text = 7;
    private const int ChannelRed = 10;

    private static FieldDefinition F(string name, FieldType type) => new(name, type, type.ToString(), false, null);

    // Every field type once, plus a colour group x_r, x_g, x_b of int fields.
    private static readonly TableDefinition Definition = new(2,
    [
        F("flag", FieldType.Boolean),
        F("small", FieldType.I16),
        F("num", FieldType.I32),
        F("big", FieldType.I64),
        F("single", FieldType.F32),
        F("double", FieldType.F64),
        F("name", FieldType.StringU8),
        F("text", FieldType.StringU16),
        F("opt", FieldType.OptionalStringU8),
        F("colour", FieldType.ColourRgb),
        F("x_r", FieldType.I32),
        F("x_g", FieldType.I32),
        F("x_b", FieldType.I32),
    ]);

    private static object?[] Row(int seed) =>
    [
        seed % 2 == 0, (short)seed, seed, (long)seed * 1000, (float)seed + 0.5f, seed + 0.25, "name" + seed, "text" + seed, "opt" + seed,
        "0000FF", seed % 256, 1, 2,
    ];

    private static RowsDocument NewTable(int rowCount)
    {
        var rows = new List<object?[]>();
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(Row(i));
        }

        return RowsDocument.ForTable(TableName, 2, Pack, Definition, rows);
    }

    private static RowsDocument NewTextTable()
    {
        var definition = new TableDefinition(1, [F("text", FieldType.StringU16)]);
        return RowsDocument.ForTable("single", 1, Pack, definition, []);
    }

    private static void AssertNoRows(PasteResult result, PasteProblem problem, int line)
    {
        Assert.Equal(problem, result.Problem);
        Assert.Equal(line, result.Line);
        Assert.Empty(result.Rows);
        Assert.Empty(result.MissingColumns);
        Assert.Empty(result.ExtraColumns);
        Assert.False(string.IsNullOrEmpty(result.Detail));
    }

    [Fact]
    public void Write_then_Read_gives_the_same_rows()
    {
        var source = NewTable(4);
        source.SetCell(1, Text, "tab\there\nand a line break").Do();
        var text = ClipboardRows.Write(source.Columns, source.Rows);

        var target = NewTable(0);
        var result = ClipboardRows.Read(text, target);

        Assert.Equal(PasteProblem.None, result.Problem);
        Assert.Null(result.Detail);
        Assert.Empty(result.MissingColumns);
        Assert.Empty(result.ExtraColumns);
        Assert.Equal(source.Rows.Count, result.Rows.Count);
        for (var i = 0; i < source.Rows.Count; i++)
        {
            Assert.Equal(source.Rows[i], result.Rows[i]);
        }

        // The rows are fresh arrays that the target takes over without a complaint.
        target.InsertRows(0, result.Rows).Do();
        Assert.Equal(4, target.Rows.Count);

        // A loc file travels the same way.
        var loc = RowsDocument.ForLoc(@"text\db\test__.loc", [new LocRow("k1", "t1", false), new LocRow("k2", "t2\tx", true)]);
        var locResult = ClipboardRows.Read(ClipboardRows.Write(loc.Columns, loc.Rows), RowsDocument.ForLoc(@"text\db\other__.loc", []));
        Assert.Equal(PasteProblem.None, locResult.Problem);
        Assert.Equal(new object?[] { "k2", "t2\tx", true }, locResult.Rows[1]);
    }

    [Fact]
    public void Write_gives_a_header_and_one_line_per_row_with_escaped_cells()
    {
        var source = NewTable(2);
        source.SetCell(0, Text, "a\tb\nc").Do();

        var text = ClipboardRows.Write(source.Columns, source.Rows);
        var lines = text.Split('\n');

        Assert.Equal(4, lines.Length);
        Assert.Equal(string.Empty, lines[3]);
        Assert.Equal(string.Join('\t', Definition.Fields.Select(f => f.Name)), lines[0]);
        Assert.Equal(Definition.Fields.Count, lines[1].Split('\t').Length);
        Assert.Contains("a\\\\tb\\\\nc", lines[1], StringComparison.Ordinal);

        Assert.Equal(string.Join('\t', Definition.Fields.Select(f => f.Name)) + "\n", ClipboardRows.Write(Definition.Fields, []));
    }

    [Fact]
    public void Write_refuses_a_row_of_the_wrong_length_or_a_value_the_column_cannot_format()
    {
        Assert.Throws<ArgumentException>(() => ClipboardRows.Write(Definition.Fields, [new object?[] { 1 }]));
        Assert.Throws<ArgumentException>(() => ClipboardRows.Write(Definition.Fields, [WithCell(Row(1), Num, "x")]));
    }

    private static object?[] WithCell(object?[] row, int column, object? value)
    {
        row[column] = value;
        return row;
    }

    [Fact]
    public void Columns_are_matched_by_name_in_any_order()
    {
        var source = NewTable(3);
        var columns = source.Columns.Reverse().ToList();
        var rows = source.Rows.Select(r => r.Reverse().ToArray()).ToList();

        var result = ClipboardRows.Read(ClipboardRows.Write(columns, rows), NewTable(0));

        Assert.Equal(PasteProblem.None, result.Problem);
        Assert.Empty(result.MissingColumns);
        Assert.Empty(result.ExtraColumns);
        for (var i = 0; i < source.Rows.Count; i++)
        {
            Assert.Equal(source.Rows[i], result.Rows[i]);
        }
    }

    [Fact]
    public void Missing_columns_get_defaults_and_extra_columns_are_dropped_and_both_are_named()
    {
        var target = NewTable(0);
        var result = ClipboardRows.Read("num\tbogus\ttext\textra\n7\tx\thello\ty\n8\tx\tworld\ty\n", target);

        Assert.Equal(PasteProblem.None, result.Problem);
        Assert.Equal(["bogus", "extra"], result.ExtraColumns);
        Assert.Equal(["flag", "small", "big", "single", "double", "name", "opt", "colour", "x_r", "x_g", "x_b"], result.MissingColumns);
        Assert.Equal(2, result.Rows.Count);

        var expected = target.NewRow();
        expected[Num] = 8;
        expected[Text] = "world";
        Assert.Equal(expected, result.Rows[1]);
        Assert.Equal(7, result.Rows[0][Num]);
    }

    [Fact]
    public void A_paste_with_one_bad_cell_changes_nothing()
    {
        var target = NewTable(2);
        var before = target.Rows.Select(r => (object?[])r.Clone()).ToList();

        var result = ClipboardRows.Read("num\ttext\n1\ta\n2\tb\nnot a number\tc\n4\td\n", target);

        Assert.Equal(PasteProblem.InvalidValue, result.Problem);
        Assert.Equal(4, result.Line);
        Assert.Empty(result.Rows);
        Assert.Empty(result.MissingColumns);
        Assert.Empty(result.ExtraColumns);
        Assert.Contains("num", result.Detail, StringComparison.Ordinal);
        Assert.Contains("not a number", result.Detail, StringComparison.Ordinal);
        Assert.Contains('4', result.Detail!);

        Assert.Equal(2, target.Rows.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i], target.Rows[i]);
        }
    }

    [Fact]
    public void A_wrong_cell_count_names_its_line()
    {
        var target = NewTable(0);

        var tooFew = ClipboardRows.Read("num\ttext\n1\ta\n2\n3\tc\n", target);
        AssertNoRows(tooFew, PasteProblem.WrongCellCount, 3);

        var tooMany = ClipboardRows.Read("num\ttext\n1\ta\n2\tb\tc\n", target);
        AssertNoRows(tooMany, PasteProblem.WrongCellCount, 3);

        // An empty line in the middle is a row with one empty cell: too few for two columns, fine for one.
        AssertNoRows(ClipboardRows.Read("num\ttext\n1\ta\n\n3\tc\n", target), PasteProblem.WrongCellCount, 3);

        var single = ClipboardRows.Read("text\n\nx\n", NewTextTable());
        Assert.Equal(PasteProblem.None, single.Problem);
        Assert.Equal(2, single.Rows.Count);
        Assert.Equal("", single.Rows[0][0]);
        Assert.Equal("x", single.Rows[1][0]);
    }

    [Fact]
    public void Text_without_a_known_column_is_refused()
    {
        var target = NewTable(0);

        AssertNoRows(ClipboardRows.Read("Hello there, this is some prose.\n", target), PasteProblem.NoKnownColumn, 1);
        AssertNoRows(ClipboardRows.Read("id\tname_other\tcost\n1\ta\t2\n", target), PasteProblem.NoKnownColumn, 1);

        // Names are matched ordinally: the case counts.
        AssertNoRows(ClipboardRows.Read("NUM\tTEXT\n1\ta\n", target), PasteProblem.NoKnownColumn, 1);
    }

    [Fact]
    public void A_name_twice_in_the_header_is_refused()
    {
        var result = ClipboardRows.Read("num\ttext\tnum\n1\ta\t3\n", NewTable(0));

        AssertNoRows(result, PasteProblem.DuplicateColumn, 1);
        Assert.Contains("num", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_text_and_a_header_alone_are_Empty()
    {
        var target = NewTable(0);

        AssertNoRows(ClipboardRows.Read("", target), PasteProblem.Empty, 0);
        AssertNoRows(ClipboardRows.Read("num\ttext\n", target), PasteProblem.Empty, 0);
        AssertNoRows(ClipboardRows.Read("num\ttext", target), PasteProblem.Empty, 0);
        AssertNoRows(ClipboardRows.Read("num\ttext\r\n", target), PasteProblem.Empty, 0);
    }

    [Fact]
    public void Tabs_and_line_breaks_inside_cells_survive()
    {
        var source = NewTable(2);
        source.SetCell(0, Text, "a\tb\tc").Do();
        source.SetCell(1, Text, "line 1\nline 2\n\tindented\n").Do();
        source.SetCell(1, 6, "\t\n").Do();

        var text = ClipboardRows.Write(source.Columns, source.Rows);
        Assert.Equal(4, text.Split('\n').Length);

        var result = ClipboardRows.Read(text, NewTable(0));

        Assert.Equal(PasteProblem.None, result.Problem);
        Assert.Equal("a\tb\tc", result.Rows[0][Text]);
        Assert.Equal("line 1\nline 2\n\tindented\n", result.Rows[1][Text]);
        Assert.Equal("\t\n", result.Rows[1][6]);
    }

    [Fact]
    public void Windows_line_ends_are_read()
    {
        var target = NewTable(0);

        var result = ClipboardRows.Read("num\ttext\r\n1\tfirst\r\n2\tsecond\r\n", target);
        Assert.Equal(PasteProblem.None, result.Problem);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("first", result.Rows[0][Text]);
        Assert.Equal("second", result.Rows[1][Text]);

        // The last line without a line end, with and without a carriage return; an empty last line is no row.
        var noEnd = ClipboardRows.Read("num\ttext\r\n1\tfirst\r\n2\tsecond", target);
        Assert.Equal("second", noEnd.Rows[1][Text]);
        var loneReturn = ClipboardRows.Read("num\ttext\n1\tfirst\n2\tsecond\r", target);
        Assert.Equal(2, loneReturn.Rows.Count);
        Assert.Equal("second", loneReturn.Rows[1][Text]);
        var emptyLast = ClipboardRows.Read("num\ttext\n1\tfirst\n2\tsecond\n\r", target);
        Assert.Equal(PasteProblem.None, emptyLast.Problem);
        Assert.Equal(2, emptyLast.Rows.Count);

        // Mixed line ends are read as well.
        Assert.Equal(2, ClipboardRows.Read("num\r\n1\n2\r\n", target).Rows.Count);
    }

    [Fact]
    public void A_carriage_return_inside_a_cell_is_refused()
    {
        var result = ClipboardRows.Read("num\ttext\n1\tab\rcd\n", NewTable(0));

        AssertNoRows(result, PasteProblem.InvalidValue, 2);
        Assert.Contains("text", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_channel_value_above_255_is_refused()
    {
        var target = NewTable(0);

        var result = ClipboardRows.Read("x_r\tnum\n256\t300\n", target);
        AssertNoRows(result, PasteProblem.InvalidValue, 2);
        Assert.Contains("x_r", result.Detail, StringComparison.Ordinal);

        Assert.Equal(PasteProblem.InvalidValue, ClipboardRows.Read("x_g\n-1\n", target).Problem);

        // The same value is fine in a column that is no colour channel, and 255 is fine in a channel.
        var fine = ClipboardRows.Read("x_r\tnum\n255\t300\n", target);
        Assert.Equal(PasteProblem.None, fine.Problem);
        Assert.Equal(300, fine.Rows[0][Num]);
        Assert.Equal(255, fine.Rows[0][ChannelRed]);
    }

    [Fact]
    public void A_quoted_cell_is_taken_literally()
    {
        // Spreadsheet programs may put a cell in double quotes; the app's own format escapes instead, so the quotes stay part of the value.
        var result = ClipboardRows.Read("num\ttext\n1\t\"hello\"\n", NewTable(0));

        Assert.Equal(PasteProblem.None, result.Problem);
        Assert.Equal("\"hello\"", result.Rows[0][Text]);

        // A quoted cell with a tab inside is split at the tab.
        AssertNoRows(ClipboardRows.Read("num\ttext\n1\t\"a\tb\"\n", NewTable(0)), PasteProblem.WrongCellCount, 2);
    }

    [Fact]
    public void A_value_in_a_message_is_shortened()
    {
        var huge = new string('7', 20_000) + "x";

        var result = ClipboardRows.Read("num\n" + huge + "\n", NewTable(0));

        Assert.Equal(PasteProblem.InvalidValue, result.Problem);
        Assert.True(result.Detail!.Length < 300, $"The detail has {result.Detail.Length} characters");
        Assert.DoesNotContain(huge, result.Detail, StringComparison.Ordinal);

        // A control character in the value does not end up raw in the message.
        var control = ClipboardRows.Read("text\n\u0001\n", NewTable(0));
        Assert.Equal(PasteProblem.None, control.Problem);
        var bad = ClipboardRows.Read("num\n1\u0001\n", NewTable(0));
        Assert.Equal(PasteProblem.InvalidValue, bad.Problem);
        Assert.DoesNotContain('\u0001', bad.Detail!);
    }

    [Fact]
    public void A_hundred_thousand_pasted_rows_are_read()
    {
        var source = NewTable(100_000);
        var text = ClipboardRows.Write(source.Columns, source.Rows);

        var result = ClipboardRows.Read(text, NewTable(0));

        Assert.Equal(PasteProblem.None, result.Problem);
        Assert.Equal(100_000, result.Rows.Count);
        Assert.Equal(source.Rows[99_999], result.Rows[99_999]);
        Assert.Equal(source.Rows[12_345], result.Rows[12_345]);
    }

    [Fact]
    public void Read_refuses_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => ClipboardRows.Read(null!, NewTable(0)));
        Assert.Throws<ArgumentNullException>(() => ClipboardRows.Read("a", null!));
        Assert.Throws<ArgumentNullException>(() => ClipboardRows.Write(null!, []));
        Assert.Throws<ArgumentNullException>(() => ClipboardRows.Write(Definition.Fields, null!));
    }
}
