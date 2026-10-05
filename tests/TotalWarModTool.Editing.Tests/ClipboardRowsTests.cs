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

    private static void AssertNoRows(PasteResult result, PasteProblem problem, int line, string detail)
    {
        Assert.Equal(problem, result.Problem);
        Assert.Equal(line, result.Line);
        Assert.Empty(result.Rows);
        Assert.Empty(result.MissingColumns);
        Assert.Empty(result.ExtraColumns);
        Assert.Equal(detail, result.Detail);
    }

    // The text of a Write that must succeed.
    private static string Written(IReadOnlyList<FieldDefinition> columns, IEnumerable<object?[]> rows)
    {
        var result = ClipboardRows.Write(columns, rows);
        Assert.True(result.Text is not null, result.Detail);
        Assert.Null(result.Detail);
        return result.Text!;
    }

    private static bool SameCell(object? a, object? b) => (a, b) switch
    {
        (float x, float y) => BitConverter.SingleToInt32Bits(x) == BitConverter.SingleToInt32Bits(y),
        (double x, double y) => BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y),
        _ => Equals(a, b),
    };

    // The rule of the format: whenever Write returns text, Read into a document of the same columns gives back the same values.
    // Returns whether the rows were carried.
    private static bool AssertCarriedOrRefused(RowsDocument sameColumns, IReadOnlyList<object?[]> rows, string what)
    {
        var written = ClipboardRows.Write(sameColumns.Columns, rows);
        if (written.Text is null)
        {
            Assert.False(string.IsNullOrEmpty(written.Detail), what);
            return false;
        }

        var read = ClipboardRows.Read(written.Text, sameColumns);
        Assert.True(read.Problem == PasteProblem.None, $"{what}: {read.Problem} {read.Detail}");
        Assert.Equal(rows.Count, read.Rows.Count);
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                Assert.True(SameCell(rows[r][c], read.Rows[r][c]), $"{what}: row {r}, column {c}: '{rows[r][c]}' came back as '{read.Rows[r][c]}'");
            }
        }

        return true;
    }

    private static RowsDocument NewTextColumns(int count)
    {
        var fields = Enumerable.Range(0, count).Select(i => F("c" + i, FieldType.StringU16)).ToList();
        return RowsDocument.ForTable("texts", 1, Pack, new TableDefinition(1, fields), []);
    }

    [Fact]
    public void Write_then_Read_gives_the_same_rows()
    {
        var source = NewTable(4);
        source.SetCell(1, Text, "tab\there\nand a line break").Do();
        var text = Written(source.Columns, source.Rows);

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
        var locResult = ClipboardRows.Read(Written(loc.Columns, loc.Rows), RowsDocument.ForLoc(@"text\db\other__.loc", []));
        Assert.Equal(PasteProblem.None, locResult.Problem);
        Assert.Equal(new object?[] { "k2", "t2\tx", true }, locResult.Rows[1]);
    }

    [Fact]
    public void Write_gives_a_header_and_one_line_per_row_with_escaped_cells()
    {
        var source = NewTable(2);
        source.SetCell(0, Text, "a\tb\nc").Do();

        var text = Written(source.Columns, source.Rows);
        var lines = text.Split('\n');

        Assert.Equal(4, lines.Length);
        Assert.Equal(string.Empty, lines[3]);
        Assert.Equal(string.Join('\t', Definition.Fields.Select(f => f.Name)), lines[0]);
        Assert.Equal(Definition.Fields.Count, lines[1].Split('\t').Length);
        Assert.Contains("a\\\\tb\\\\nc", lines[1], StringComparison.Ordinal);

        Assert.Equal(string.Join('\t', Definition.Fields.Select(f => f.Name)) + "\n", Written(Definition.Fields, []));
    }

    [Fact]
    public void Write_throws_for_a_row_of_the_wrong_length_or_a_value_of_the_wrong_type()
    {
        Assert.Throws<ArgumentException>(() => ClipboardRows.Write(Definition.Fields, [new object?[] { 1 }]));
        Assert.Throws<ArgumentException>(() => ClipboardRows.Write(Definition.Fields, [WithCell(Row(1), Num, "x")]));
        Assert.Throws<ArgumentException>(() => ClipboardRows.Write(Definition.Fields, [WithCell(Row(1), Num, null)]));
    }

    [Fact]
    public void Write_refuses_a_cell_that_Read_would_return_differently_and_names_row_column_and_reason()
    {
        // The second row is the bad one: row 2 counts among the given rows.
        var carriageReturn = ClipboardRows.Write(Definition.Fields, [Row(1), WithCell(Row(2), Text, "a\r")]);
        Assert.Null(carriageReturn.Text);
        Assert.Equal(
            "Row 2, column 'text', value 'a\\u000D': the text contains a carriage return, which the TSV dialect cannot carry",
            carriageReturn.Detail);

        var literalEscape = ClipboardRows.Write(Definition.Fields, [Row(1), WithCell(Row(2), Text, "x\\\\ty")]);
        Assert.Null(literalEscape.Text);
        Assert.StartsWith("Row 2, column 'text', value 'x\\\\ty': the text contains a literal backslash backslash t or n", literalEscape.Detail, StringComparison.Ordinal);

        var surrogate = ClipboardRows.Write(Definition.Fields, [WithCell(Row(1), Text, "a" + (char)0xD800 + "b")]);
        Assert.Null(surrogate.Text);
        Assert.StartsWith("Row 1, column 'text', value 'a\\uD800b': ", surrogate.Detail, StringComparison.Ordinal);

        var nul = ClipboardRows.Write(Definition.Fields, [WithCell(Row(1), Text, "a\0b")]);
        Assert.Null(nul.Text);
        Assert.Equal("Row 1, column 'text', value 'a\\u0000b': the text contains a NUL character", nul.Detail);

        var payload = BitConverter.Int32BitsToSingle(0x7FC00001);
        var nanPayload = ClipboardRows.Write(Definition.Fields, [WithCell(Row(1), 4, payload)]);
        Assert.Null(nanPayload.Text);
        Assert.Equal("Row 1, column 'single', value 'NaN': the value would not come back as the same number, a NaN with a payload", nanPayload.Detail);

        // A huge text is shortened in the message.
        var huge = ClipboardRows.Write(Definition.Fields, [WithCell(Row(1), Text, new string('x', 5_000) + "\r")]);
        Assert.Null(huge.Text);
        Assert.True(huge.Detail!.Length < 300);
        Assert.Contains("...", huge.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Whatever_Write_returns_Read_gives_back_as_the_same_values()
    {
        var document = NewTable(0);
        var nan = float.NaN;
        var cases = new (int Column, object?[] Values, bool Carried)[]
        {
            (Text, ["", "plain", "a\\t", "\\", "end\\", "\\\\", "tab\tx", "nl\nx", "\t", "\n", " leading and trailing ", "é€", "\U0001F600", "x\\\ty"], true),
            (Text, ["a\\\\t", "a\\\\n", "a\r", "\r", "a\r\nb", "a\0b", "a" + (char)0xD800 + "b", new string((char)0xDC00, 1)], false),
            (6, ["", "a\\t", "tab\there"], true),
            (6, ["a\\\\tb", "a\rb"], false),
            (8, ["", "x"], true),
            (4, [0.1f, 0f, -0f, 1.5f, nan, float.PositiveInfinity, float.NegativeInfinity, float.Epsilon, float.MaxValue], true),
            (4, [BitConverter.Int32BitsToSingle(0x7FC00001)], false),
            (5, [0.1, -0.0, double.NaN, double.NegativeInfinity, double.Epsilon, double.MaxValue], true),
            (5, [BitConverter.Int64BitsToDouble(0x7FF8000000000001)], false),
            (0, [true, false], true),
            (Num, [0, -1, int.MaxValue, int.MinValue], true),
            (3, [0L, long.MaxValue, long.MinValue], true),
            (1, [(short)0, short.MaxValue, short.MinValue], true),
            (9, ["000000", "0000FF", "FFFFFFFF"], true),
        };

        foreach (var (column, values, carried) in cases)
        {
            foreach (var value in values)
            {
                var row = WithCell(Row(1), column, value);
                Assert.Equal(carried, AssertCarriedOrRefused(document, [row], $"column {column}, value '{value}'"));
            }
        }

        // Several rows at once, with the bad one last: nothing is carried.
        Assert.False(AssertCarriedOrRefused(document, [Row(1), Row(2), WithCell(Row(3), Text, "a\r")], "bad last row"));
        Assert.True(AssertCarriedOrRefused(document, [Row(1), Row(2), Row(3)], "good rows"));

        // Empty cells in multi-column rows, and a last row whose last cell is empty.
        var texts = NewTextColumns(3);
        Assert.True(AssertCarriedOrRefused(texts, [new object?[] { "", "", "" }], "all empty"));
        Assert.True(AssertCarriedOrRefused(texts, [new object?[] { "x", "", "" }, new object?[] { "", "", "z" }, new object?[] { "", "y", "" }], "empty cells"));
        Assert.True(AssertCarriedOrRefused(texts, [new object?[] { "a", "b", "c" }, new object?[] { "d", "e", "" }], "last cell empty"));
        Assert.False(AssertCarriedOrRefused(texts, [new object?[] { "a", "b", "c\r" }], "last cell ends on a carriage return"));
        Assert.False(AssertCarriedOrRefused(texts, [new object?[] { "a", "b", "c\\\\n" }], "last cell with a literal escape"));

        // A one-column table with empty cells at the start, in the middle and at the end.
        var single = NewTextColumns(1);
        Assert.True(AssertCarriedOrRefused(single, [new object?[] { "" }], "one empty cell"));
        Assert.True(AssertCarriedOrRefused(single, [new object?[] { "" }, new object?[] { "x" }, new object?[] { "" }], "empty cells around"));
        Assert.True(AssertCarriedOrRefused(single, [new object?[] { "x" }, new object?[] { "" }], "empty last cell"));
        Assert.False(AssertCarriedOrRefused(single, [new object?[] { "x\r" }], "one-column carriage return"));

        // A loc file with an empty text and a tooltip flag.
        var loc = RowsDocument.ForLoc(@"text\db\test__.loc", []);
        Assert.True(AssertCarriedOrRefused(loc, [new object?[] { "k", "", true }, new object?[] { "k2", "t\tu\nv", false }], "loc"));
        Assert.False(AssertCarriedOrRefused(loc, [new object?[] { "k", "t\r", true }], "loc with a carriage return"));
    }

    [Fact]
    public void Two_columns_of_one_name_in_the_table_refuse_Read_and_Write()
    {
        FieldDefinition[] twice = [F("a", FieldType.I32), F("b", FieldType.I32), F("a", FieldType.StringU16)];
        var document = RowsDocument.ForTable("twice", 1, Pack, new TableDefinition(1, twice), []);
        const string detail = "The table itself has two columns named 'a'";

        AssertNoRows(ClipboardRows.Read("a\tb\n1\t2\n", document), PasteProblem.DuplicateColumn, 0, detail);
        AssertNoRows(ClipboardRows.Read("", document), PasteProblem.DuplicateColumn, 0, detail);

        var written = ClipboardRows.Write(twice, [new object?[] { 1, 2, "x" }]);
        Assert.Null(written.Text);
        Assert.Equal(detail, written.Detail);
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

        var result = ClipboardRows.Read(Written(columns, rows), NewTable(0));

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

        AssertNoRows(result, PasteProblem.InvalidValue, 4, "Line 4, column 'num', value 'not a number': not a 32-bit integer");

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
        AssertNoRows(tooFew, PasteProblem.WrongCellCount, 3, "Line 3 has 1 cell, expected 2");

        var tooMany = ClipboardRows.Read("num\ttext\n1\ta\n2\tb\tc\n", target);
        AssertNoRows(tooMany, PasteProblem.WrongCellCount, 3, "Line 3 has 3 cells, expected 2");

        // Singular and plural of the expected count are not mixed up: one column, two cells.
        AssertNoRows(ClipboardRows.Read("text\nx\ny\tz\n", NewTextTable()), PasteProblem.WrongCellCount, 3, "Line 3 has 2 cells, expected 1");

        // An empty line in the middle is a row with one empty cell: too few for two columns, fine for one.
        AssertNoRows(ClipboardRows.Read("num\ttext\n1\ta\n\n3\tc\n", target), PasteProblem.WrongCellCount, 3, "Line 3 has 1 cell, expected 2");

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

        AssertNoRows(ClipboardRows.Read("Hello there, this is some prose.\n", target), PasteProblem.NoKnownColumn, 1,
            "The first line must contain the column names of the table, but 'Hello there, this is some prose.' contains none of them");
        AssertNoRows(ClipboardRows.Read("id\tname_other\tcost\n1\ta\t2\n", target), PasteProblem.NoKnownColumn, 1,
            "The first line must contain the column names of the table, but 'id\\u0009name_other\\u0009cost' contains none of them");

        // Names are matched ordinally: the case counts.
        AssertNoRows(ClipboardRows.Read("NUM\tTEXT\n1\ta\n", target), PasteProblem.NoKnownColumn, 1,
            "The first line must contain the column names of the table, but 'NUM\\u0009TEXT' contains none of them");

        // A long first line is shortened.
        var longLine = ClipboardRows.Read(new string('p', 500) + "\n", target);
        Assert.Equal(PasteProblem.NoKnownColumn, longLine.Problem);
        Assert.Equal("The first line must contain the column names of the table, but '" + new string('p', 50) + "...' contains none of them", longLine.Detail);

        // A table without a column knows no name.
        var noColumns = RowsDocument.ForTable("none", 1, Pack, new TableDefinition(1, []), []);
        AssertNoRows(ClipboardRows.Read("num\n1\n", noColumns), PasteProblem.NoKnownColumn, 1,
            "The first line must contain the column names of the table, but 'num' contains none of them");
    }

    [Fact]
    public void A_name_twice_in_the_header_is_refused()
    {
        var result = ClipboardRows.Read("num\ttext\tnum\n1\ta\t3\n", NewTable(0));

        AssertNoRows(result, PasteProblem.DuplicateColumn, 1, "The column 'num' occurs twice in the first line");

        // A problem of the header is reported even when there are no rows; it wins over Empty.
        AssertNoRows(ClipboardRows.Read("num\tnum\n", NewTable(0)), PasteProblem.DuplicateColumn, 1, "The column 'num' occurs twice in the first line");
        AssertNoRows(ClipboardRows.Read("hello\n", NewTable(0)), PasteProblem.NoKnownColumn, 1,
            "The first line must contain the column names of the table, but 'hello' contains none of them");
    }

    [Fact]
    public void An_unknown_name_twice_is_two_extra_columns_and_extra_names_are_shortened()
    {
        var long50 = new string('q', 60);
        var result = ClipboardRows.Read("num\tzz\tzz\t" + long50 + "\n1\ta\tb\tc\n", NewTable(0));

        Assert.Equal(PasteProblem.None, result.Problem);
        Assert.Equal(["zz", "zz", new string('q', 50) + "..."], result.ExtraColumns);
        Assert.Equal(1, result.Rows[0][Num]);
    }

    [Fact]
    public void Empty_text_and_a_header_alone_are_Empty()
    {
        var target = NewTable(0);

        AssertNoRows(ClipboardRows.Read("", target), PasteProblem.Empty, 0, "The text is empty");
        AssertNoRows(ClipboardRows.Read("num\ttext\n", target), PasteProblem.Empty, 0, "The text has a header line but no rows");
        AssertNoRows(ClipboardRows.Read("num\ttext", target), PasteProblem.Empty, 0, "The text has a header line but no rows");
        AssertNoRows(ClipboardRows.Read("num\ttext\r\n", target), PasteProblem.Empty, 0, "The text has a header line but no rows");
    }

    [Fact]
    public void A_leading_byte_order_mark_is_ignored()
    {
        var mark = (char)0xFEFF;
        var target = NewTable(0);

        var result = ClipboardRows.Read(mark + "num\ttext\n1\ta\n", target);
        Assert.Equal(PasteProblem.None, result.Problem);
        Assert.Equal(1, result.Rows[0][Num]);
        Assert.Empty(result.MissingColumns.Intersect(["num", "text"]));
        Assert.Empty(result.ExtraColumns);

        AssertNoRows(ClipboardRows.Read(mark.ToString(), target), PasteProblem.Empty, 0, "The text is empty");
        AssertNoRows(ClipboardRows.Read(mark + "num\n", target), PasteProblem.Empty, 0, "The text has a header line but no rows");

        // Only the very first character counts: a mark in a cell is part of the value.
        var inCell = ClipboardRows.Read("num\ttext\n1\t" + mark + "a\n", target);
        Assert.Equal(mark + "a", inCell.Rows[0][Text]);
    }

    [Fact]
    public void Upper_case_booleans_are_read_and_a_decimal_comma_is_refused()
    {
        var target = NewTable(0);

        // CellText reads true and false in any case.
        var booleans = ClipboardRows.Read("flag\nTRUE\nFALSE\nTrue\n", target);
        Assert.Equal(PasteProblem.None, booleans.Problem);
        Assert.Equal([true, false, true], booleans.Rows.Select(r => r[0]));

        AssertNoRows(ClipboardRows.Read("flag\nyes\n", target), PasteProblem.InvalidValue, 2, "Line 2, column 'flag', value 'yes': not true or false");

        // "1,5" is no number of the invariant culture.
        AssertNoRows(ClipboardRows.Read("single\n1\n1,5\n", target), PasteProblem.InvalidValue, 3, "Line 3, column 'single', value '1,5': not a number");
        Assert.Equal(1.5f, ClipboardRows.Read("single\n1.5\n", target).Rows[0][4]);
    }

    [Fact]
    public void Tabs_and_line_breaks_inside_cells_survive()
    {
        var source = NewTable(2);
        source.SetCell(0, Text, "a\tb\tc").Do();
        source.SetCell(1, Text, "line 1\nline 2\n\tindented\n").Do();
        source.SetCell(1, 6, "\t\n").Do();

        var text = Written(source.Columns, source.Rows);
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

        AssertNoRows(result, PasteProblem.InvalidValue, 2,
            "Line 2, column 'text', value 'ab\\u000Dcd': the text contains a carriage return, which the TSV dialect cannot carry");
    }

    [Fact]
    public void A_channel_value_above_255_is_refused()
    {
        var target = NewTable(0);

        var result = ClipboardRows.Read("x_r\tnum\n256\t300\n", target);
        AssertNoRows(result, PasteProblem.InvalidValue, 2, "Line 2, column 'x_r', value '256': a colour channel must be an integer from 0 to 255");

        AssertNoRows(ClipboardRows.Read("x_g\n-1\n", target), PasteProblem.InvalidValue, 2,
            "Line 2, column 'x_g', value '-1': a colour channel must be an integer from 0 to 255");

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
        AssertNoRows(ClipboardRows.Read("num\ttext\n1\t\"a\tb\"\n", NewTable(0)), PasteProblem.WrongCellCount, 2, "Line 2 has 3 cells, expected 2");
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
        var text = Written(source.Columns, source.Rows);

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
