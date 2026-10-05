using TotalWarModTool.Editing;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.Editing.Tests;

public partial class RowsDocumentTests
{
    private const string Pack = @"db\test_tables\data__";
    private const string TableName = "test_tables";

    // Column indexes of the test definition.
    private const int Flag = 0;
    private const int Small = 1;
    private const int Num = 2;
    private const int Big = 3;
    private const int Single = 4;
    private const int Double = 5;
    private const int Name = 6;
    private const int Text = 7;
    private const int Opt = 8;
    private const int Colour = 9;
    private const int ChannelRed = 10;
    private const int ChannelGreen = 11;
    private const int ChannelBlue = 12;

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

    private static GameSchema Schema() =>
        new(new Dictionary<string, IReadOnlyList<TableDefinition>> { [TableName] = [Definition] });

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

    private static RowsDocument NewLoc() =>
        RowsDocument.ForLoc(@"text\db\test__.loc", [new LocRow("k1", "t1", false), new LocRow("k2", "t2\tx", true)]);

    // The rows as a list of independent copies, to compare a state with an earlier one.
    private static List<object?[]> Snapshot(RowsDocument document) => document.Rows.Select(r => (object?[])r.Clone()).ToList();

    private static void AssertSameState(IReadOnlyList<object?[]> expected, RowsDocument document)
    {
        Assert.Equal(expected.Count, document.Rows.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], document.Rows[i]);
        }
    }

    private static List<DocumentChange> Record(RowsDocument document)
    {
        var changes = new List<DocumentChange>();
        document.Changed += (_, c) => changes.Add(c);
        return changes;
    }

    private static string Describe(DocumentChange c) => c.Kind + ":" + string.Join(",", c.RowIndexes) + ":" + c.Column;

    [Fact]
    public void SetCell_changes_the_cell_and_undo_restores_it()
    {
        var document = NewTable(3);
        var changes = Record(document);
        var edit = document.SetCell(1, Num, 99);

        Assert.Equal(1, document.Rows[1][Num]);
        Assert.Empty(changes);

        edit.Do();
        Assert.Equal(99, document.Rows[1][Num]);

        edit.Undo();
        Assert.Equal(1, document.Rows[1][Num]);
        Assert.Equal(["CellsChanged:1:" + Num, "CellsChanged:1:" + Num], changes.Select(Describe));
        Assert.Same(document, edit.Document);
        Assert.Equal("set cell " + Pack, edit.Description);
    }

    [Fact]
    public void InsertRows_puts_the_rows_at_the_index_and_undo_removes_them()
    {
        var document = NewTable(3);
        var before = Snapshot(document);
        var changes = Record(document);
        var added = new[] { Row(10), Row(11) };
        var edit = document.InsertRows(1, added);

        Assert.Equal(3, document.Rows.Count);
        edit.Do();

        Assert.Equal(5, document.Rows.Count);
        Assert.Same(added[0], document.Rows[1]);
        Assert.Same(added[1], document.Rows[2]);
        Assert.Equal(before[1], document.Rows[3]);

        edit.Undo();
        AssertSameState(before, document);
        Assert.Equal(["RowsInserted:1,2:-1", "RowsRemoved:1,2:-1"], changes.Select(Describe));
        Assert.Equal("insert rows " + Pack, edit.Description);

        // At the end is allowed.
        document.InsertRows(3, [Row(12)]).Do();
        Assert.Equal(4, document.Rows.Count);
    }

    [Fact]
    public void DeleteRows_removes_unsorted_and_duplicate_indexes_once_and_undo_restores_the_same_arrays()
    {
        var document = NewTable(6);
        var arrays = document.Rows.ToArray();
        var changes = Record(document);
        var edit = document.DeleteRows([4, 1, 4, 2]);

        edit.Do();
        Assert.Equal(3, document.Rows.Count);
        Assert.Same(arrays[0], document.Rows[0]);
        Assert.Same(arrays[3], document.Rows[1]);
        Assert.Same(arrays[5], document.Rows[2]);

        edit.Undo();
        Assert.Equal(6, document.Rows.Count);
        for (var i = 0; i < arrays.Length; i++)
        {
            Assert.Same(arrays[i], document.Rows[i]);
        }

        Assert.Equal(["RowsRemoved:1,2,4:-1", "RowsInserted:1,2,4:-1"], changes.Select(Describe));
        Assert.Equal("delete rows " + Pack, edit.Description);

        // The first and the last row, and every row.
        var all = document.DeleteRows([5, 0]);
        all.Do();
        Assert.Equal(4, document.Rows.Count);
        all.Undo();
        for (var i = 0; i < arrays.Length; i++)
        {
            Assert.Same(arrays[i], document.Rows[i]);
        }

        var everything = document.DeleteRows([0, 1, 2, 3, 4, 5]);
        everything.Do();
        Assert.Empty(document.Rows);
        everything.Undo();
        Assert.Equal(6, document.Rows.Count);
    }

    [Fact]
    public void DuplicateRows_inserts_copies_after_the_last_row_and_the_copies_are_independent()
    {
        var document = NewTable(5);
        var arrays = document.Rows.ToArray();
        var changes = Record(document);
        var edit = document.DuplicateRows([3, 1, 1]);

        edit.Do();
        Assert.Equal(7, document.Rows.Count);
        Assert.Same(arrays[3], document.Rows[3]);
        Assert.Equal(arrays[1], document.Rows[4]);
        Assert.Equal(arrays[3], document.Rows[5]);
        Assert.Same(arrays[4], document.Rows[6]);
        Assert.NotSame(arrays[1], document.Rows[4]);
        Assert.NotSame(arrays[3], document.Rows[5]);

        // Changing a copy leaves the original alone.
        var set = document.SetCell(4, Num, 777);
        set.Do();
        Assert.Equal(1, arrays[1][Num]);
        Assert.Equal(777, document.Rows[4][Num]);

        Assert.Equal("RowsInserted:4,5:-1", Describe(changes[0]));
        Assert.Equal("duplicate rows " + Pack, edit.Description);

        set.Undo();
        edit.Undo();
        Assert.Equal(5, document.Rows.Count);
    }

    [Fact]
    public void Redo_after_undo_gives_the_same_state_for_every_command()
    {
        var edits = new Func<RowsDocument, DocumentEdit>[]
        {
            d => d.SetCell(2, Text, "changed"),
            d => d.InsertRows(2, [Row(20), Row(21)]),
            d => d.DeleteRows([0, 3]),
            d => d.DuplicateRows([1, 2]),
        };

        foreach (var make in edits)
        {
            var document = NewTable(5);
            var original = Snapshot(document);
            var edit = make(document);

            edit.Do();
            var done = Snapshot(document);
            var arrays = document.Rows.ToArray();
            edit.Undo();
            AssertSameState(original, document);
            edit.Do();
            AssertSameState(done, document);
            Assert.Equal(arrays.Length, document.Rows.Count);
            for (var i = 0; i < arrays.Length; i++)
            {
                Assert.Same(arrays[i], document.Rows[i]);
            }
        }
    }

    [Fact]
    public void Commands_refuse_bad_indexes_wrong_cell_counts_and_wrong_value_types()
    {
        var document = NewTable(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => document.SetCell(-1, Num, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.SetCell(3, Num, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.SetCell(0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.SetCell(0, Definition.Fields.Count, 1));
        Assert.Throws<ArgumentException>(() => document.SetCell(0, Num, "1"));
        Assert.Throws<ArgumentException>(() => document.SetCell(0, Num, 1L));
        Assert.Throws<ArgumentException>(() => document.SetCell(0, Num, null));
        Assert.Throws<ArgumentException>(() => document.SetCell(0, Colour, "not a colour"));
        Assert.Throws<ArgumentException>(() => document.SetCell(0, Text, "a\rb"));

        // A value the dialect cannot write in a colour channel.
        Assert.Throws<ArgumentException>(() => document.SetCell(0, ChannelRed, 256));
        Assert.Throws<ArgumentException>(() => document.SetCell(0, ChannelGreen, -1));
        Assert.Throws<ArgumentException>(() => document.InsertRows(0, [WithCell(Row(1), ChannelBlue, 300)]));
        document.SetCell(0, ChannelRed, 255).Do();
        document.SetCell(0, Num, 256).Do();

        Assert.Throws<ArgumentOutOfRangeException>(() => document.InsertRows(-1, [Row(1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.InsertRows(4, [Row(1)]));
        Assert.Throws<ArgumentException>(() => document.InsertRows(0, []));
        Assert.Throws<ArgumentException>(() => document.InsertRows(0, [new object?[] { 1 }]));
        Assert.Throws<ArgumentException>(() => document.InsertRows(0, [null!]));
        Assert.Throws<ArgumentException>(() => document.InsertRows(0, [WithCell(Row(1), Num, "x")]));
        var same = Row(1);
        Assert.Throws<ArgumentException>(() => document.InsertRows(0, [same, same]));

        Assert.Throws<ArgumentException>(() => document.DeleteRows([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.DeleteRows([0, 3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.DeleteRows([-1]));
        Assert.Throws<ArgumentException>(() => document.DuplicateRows([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.DuplicateRows([5]));

        Assert.Equal(3, document.Rows.Count);
    }

    private static object?[] WithCell(object?[] row, int column, object? value)
    {
        row[column] = value;
        return row;
    }

    [Fact]
    public void A_step_refuses_a_second_Do_and_an_Undo_before_Do()
    {
        var document = NewTable(3);
        var edit = document.SetCell(0, Num, 5);

        Assert.Throws<InvalidOperationException>(edit.Undo);
        edit.Do();
        Assert.Throws<InvalidOperationException>(edit.Do);
        edit.Undo();
        Assert.Throws<InvalidOperationException>(edit.Undo);

        // The same for a row command, and nothing changed through the refusals.
        var delete = document.DeleteRows([0]);
        Assert.Throws<InvalidOperationException>(delete.Undo);
        delete.Do();
        Assert.Throws<InvalidOperationException>(delete.Do);
        Assert.Equal(2, document.Rows.Count);
    }

    [Fact]
    public void Check_uses_the_cell_rules_and_the_channel_range()
    {
        var document = NewTable(1);

        var tooBig = document.Check(ChannelRed, "256");
        Assert.Equal(CellProblem.ColourChannelOutOfRange, tooBig.Problem);
        Assert.False(string.IsNullOrEmpty(tooBig.Detail));
        Assert.Equal(CellProblem.ColourChannelOutOfRange, document.Check(ChannelGreen, "-1").Problem);

        var plain = document.Check(Num, "256");
        Assert.Equal(CellProblem.None, plain.Problem);
        Assert.Equal(256, plain.Value);
        Assert.Null(plain.Detail);

        var channel = document.Check(ChannelBlue, "255");
        Assert.Equal(CellProblem.None, channel.Problem);
        Assert.Equal(255, channel.Value);

        var bad = document.Check(ChannelRed, "abc");
        Assert.Equal(CellProblem.InvalidValue, bad.Problem);
        Assert.Null(bad.Value);
        Assert.False(string.IsNullOrEmpty(bad.Detail));

        Assert.Equal(true, document.Check(Flag, "TRUE").Value);
        Assert.Equal("0000FF", document.Check(Colour, "0000ff").Value);
        Assert.Equal(CellProblem.InvalidValue, document.Check(Text, "a\rb").Problem);

        Assert.Throws<ArgumentOutOfRangeException>(() => document.Check(-1, "1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Check(13, "1"));
        Assert.Throws<ArgumentNullException>(() => document.Check(0, null!));

        // A loc file has no colour channels.
        var loc = NewLoc();
        Assert.Equal(CellProblem.None, loc.Check(0, "256").Problem);
        Assert.Equal(CellProblem.InvalidValue, loc.Check(2, "maybe").Problem);
    }

    [Fact]
    public void NewRow_holds_the_default_of_every_column()
    {
        var document = NewTable(0);
        var row = document.NewRow();

        Assert.Equal(Definition.Fields.Count, row.Length);
        for (var i = 0; i < row.Length; i++)
        {
            Assert.Equal(CellText.DefaultValue(Definition.Fields[i].Type), row[i]);
        }

        Assert.NotSame(row, document.NewRow());
        document.InsertRows(0, [row]).Do();
        Assert.NotNull(document.ToText().Text);

        var locRow = NewLoc().NewRow();
        Assert.Equal(["", "", false], locRow);
    }

    [Fact]
    public void Modified_follows_the_saved_point_through_undo_and_new_edits()
    {
        var document = NewTable(3);
        var raised = 0;
        document.ModifiedChanged += (_, _) => raised++;
        Assert.False(document.IsModified);

        var first = document.SetCell(0, Num, 5);
        first.Do();
        Assert.True(document.IsModified);
        Assert.Equal(1, raised);

        document.MarkSaved();
        Assert.False(document.IsModified);
        Assert.Equal(2, raised);

        document.MarkSaved();
        Assert.Equal(2, raised);

        first.Undo();
        Assert.True(document.IsModified);
        Assert.Equal(3, raised);

        first.Do();
        Assert.False(document.IsModified);
        Assert.Equal(4, raised);

        first.Undo();
        Assert.Equal(5, raised);

        // A different edit after the undo: the saved state is gone, so the document stays modified without a new event.
        document.SetCell(1, Num, 6).Do();
        Assert.True(document.IsModified);
        Assert.Equal(5, raised);

        // Saving, then undoing back to the very start and forward again.
        var other = NewTable(2);
        var step = other.DeleteRows([0]);
        other.MarkSaved();
        step.Do();
        Assert.True(other.IsModified);
        step.Undo();
        Assert.False(other.IsModified);
    }

    [Fact]
    public void Modified_works_through_the_action_history()
    {
        var document = NewTable(3);
        var history = new ActionHistory();

        history.Execute(document.SetCell(0, Num, 1));
        document.MarkSaved();
        history.Execute(document.SetCell(0, Num, 2));
        Assert.True(document.IsModified);
        history.Undo();
        Assert.False(document.IsModified);
        history.Undo();
        Assert.True(document.IsModified);
        history.Redo();
        Assert.False(document.IsModified);
    }

    [Fact]
    public void A_loc_document_has_three_columns_and_writes_a_loc_file()
    {
        var rows = new List<LocRow> { new("k1", "t1", false), new("k2", "t2\tx\ny", true) };
        var document = RowsDocument.ForLoc(@"text\db\test__.loc", rows);

        Assert.Equal(DocumentKind.Loc, document.Kind);
        Assert.Equal("Loc", document.TableName);
        Assert.Equal(1, document.Version);
        Assert.Equal(@"text\db\test__.loc", document.PackPath);
        Assert.Equal(["key", "text", "tooltip"], document.Columns.Select(c => c.Name));
        Assert.Equal([FieldType.StringU16, FieldType.StringU16, FieldType.Boolean], document.Columns.Select(c => c.Type));
        Assert.Equal(new object?[] { "k2", "t2\tx\ny", true }, document.Rows[1]);

        Assert.Equal(TableTsv.WriteLoc(@"text\db\test__.loc", rows), document.ToText());

        document.SetCell(0, 1, "changed").Do();
        var read = TableTsv.ReadLoc(document.ToText().Text!);
        Assert.Equal("changed", read.Loc!.Rows[0].Text);
        document.InsertRows(2, [document.NewRow()]).Do();
        Assert.Equal(3, TableTsv.ReadLoc(document.ToText().Text!).Loc!.Rows.Count);
    }

    [Fact]
    public void ToText_equals_TableTsv_Write_of_the_rows()
    {
        var document = NewTable(4);

        Assert.Equal(DocumentKind.Table, document.Kind);
        Assert.Equal(TableName, document.TableName);
        Assert.Equal(2, document.Version);
        Assert.Equal(Pack, document.PackPath);
        Assert.Equal(Definition.Fields, document.Columns);
        Assert.Equal(TableTsv.Write(TableName, 2, Pack, Definition, document.Rows), document.ToText());

        document.SetCell(2, Text, "x\ty").Do();
        document.DeleteRows([0]).Do();
        var result = document.ToText();
        Assert.Equal(TableTsv.Write(TableName, 2, Pack, Definition, document.Rows), result);
        Assert.NotNull(result.Text);

        // A pack path the dialect cannot carry is an argument error of the writer and is not caught.
        Assert.Throws<ArgumentException>(() => RowsDocument.ForTable(TableName, 2, "a\tb", Definition, [Row(1)]).ToText());
    }

    private static readonly Dictionary<FieldType, string[]> Texts = new()
    {
        [FieldType.Boolean] = ["true", "TRUE", "False", "false", "maybe", "1", ""],
        [FieldType.I16] = ["0", "-1", "+5", "32767", "-32768", "32768", "1.5", "abc", " 1", ""],
        [FieldType.I32] = ["0", "256", "-1", "2147483647", "2147483648", "-0", "1e3", "x"],
        [FieldType.I64] = ["0", "9223372036854775807", "9223372036854775808", "-9223372036854775808", "12x"],
        [FieldType.F32] = ["0", "-0", "1.5", "0.1", "1e10", "3.4028235E+38", "1e40", "-1e40", "NaN", "Infinity", "-Infinity", "1e-50", "abc", ","],
        [FieldType.F64] = ["0", "-0", "1.5", "0.1", "1.7976931348623157E+308", "1e309", "NaN", "-Infinity", "5e-324", "1e-400", "1,5"],
        [FieldType.ColourRgb] = ["ff0000", "00FF00", "0", "FFFFFFFF", "123456789", "xyz", "", "#ff0000"],
        [FieldType.StringU8] = ["hello", "", "a\tb", "a\nb", "tab\\\\t", "line\\\\n", "a\rb", "a\0b", "é€", "\U0001F600", "x"],
        [FieldType.StringU16] = ["hello", "", "a\tb", "a\nb", "tab\\\\t", "a\rb", "a\0b", "é€", "\U0001F600", "y"],
        [FieldType.OptionalStringU8] = ["hello", "", "a\tb", "a\rb"],
    };

    // Texts that are built from code units, so that the source file does not hold them.
    private static IEnumerable<string> SurrogateTexts()
    {
        yield return "a" + (char)0xD800 + "b";
        yield return new string((char)0xDC00, 1);
        yield return new string([(char)0xD83D, (char)0xDE00]);
        yield return new string('x', 70000);
    }

    private static void AssertRoundTrips(RowsDocument document, int column, string text, Func<RowsDocument, int, object?> readBack)
    {
        var check = document.Check(column, text);
        if (check.Problem != CellProblem.None)
        {
            Assert.Null(check.Value);
            Assert.False(string.IsNullOrEmpty(check.Detail));
            return;
        }

        var edit = document.SetCell(0, column, check.Value);
        edit.Do();
        var written = document.ToText();
        Assert.True(written.Text is not null, $"column {column}, text '{text}': {written.Detail}");
        var back = readBack(document, column);
        Assert.True(SameCell(check.Value, back), $"column {column}, text '{text}': {check.Value} came back as {back}");
        Assert.True(SameCell(check.Value, document.Rows[0][column]));
        edit.Undo();
    }

    private static bool SameCell(object? a, object? b) => (a, b) switch
    {
        (float x, float y) => BitConverter.SingleToInt32Bits(x) == BitConverter.SingleToInt32Bits(y),
        (double x, double y) => BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y),
        _ => Equals(a, b),
    };

    [Fact]
    public void Whatever_the_document_accepts_can_be_written_and_read_back()
    {
        var document = NewTable(1);
        var schema = Schema();
        object? ReadTable(RowsDocument d, int column) => TableTsv.Read(d.ToText().Text!, schema).Table!.Rows[0][column];

        var channelTexts = new[] { "0", "1", "255", "256", "-1", "1.5", "255.0", "abc", "-0" };
        for (var column = 0; column < document.Columns.Count; column++)
        {
            var type = document.Columns[column].Type;
            var texts = column is ChannelRed or ChannelGreen or ChannelBlue ? channelTexts : Texts[type];
            foreach (var text in texts)
            {
                AssertRoundTrips(document, column, text, ReadTable);
            }

            if (type is FieldType.StringU8 or FieldType.StringU16 or FieldType.OptionalStringU8)
            {
                foreach (var text in SurrogateTexts())
                {
                    AssertRoundTrips(document, column, text, ReadTable);
                }
            }
        }

        // The channels accept exactly the integers 0 to 255 for float fields as well.
        var floatColours = new TableDefinition(1, [F("c_r", FieldType.F32), F("c_g", FieldType.F64), F("c_b", FieldType.I16)]);
        var colourDocument = RowsDocument.ForTable("colours", 1, Pack, floatColours, [new object?[] { 0f, 0.0, (short)0 }]);
        var colourSchema = new GameSchema(new Dictionary<string, IReadOnlyList<TableDefinition>> { ["colours"] = [floatColours] });
        object? ReadColour(RowsDocument d, int column) => TableTsv.Read(d.ToText().Text!, colourSchema).Table!.Rows[0][column];
        foreach (var text in new[] { "0", "255", "256", "-0", "-1", "100.5", "1e2", "NaN", "Infinity" })
        {
            for (var column = 0; column < 3; column++)
            {
                AssertRoundTrips(colourDocument, column, text, ReadColour);
            }
        }

        // The same for a loc file: key and text through Check, then ToText, then ReadLoc.
        var loc = NewLoc();
        object? ReadLoc(RowsDocument d, int column)
        {
            var row = TableTsv.ReadLoc(d.ToText().Text!).Loc!.Rows[0];
            return column switch { 0 => row.Key, 1 => row.Text, _ => row.Tooltip };
        }

        foreach (var column in new[] { 0, 1 })
        {
            foreach (var text in Texts[FieldType.StringU16].Concat(SurrogateTexts()))
            {
                AssertRoundTrips(loc, column, text, ReadLoc);
            }
        }

        foreach (var text in Texts[FieldType.Boolean])
        {
            AssertRoundTrips(loc, 2, text, ReadLoc);
        }
    }

    [Fact]
    public void A_bulk_insert_is_one_step_and_does_not_copy_the_table()
    {
        var definition = new TableDefinition(1, [F("id", FieldType.I32)]);
        var rows = new List<object?[]>(100_000);
        for (var i = 0; i < 100_000; i++)
        {
            rows.Add([i]);
        }

        var document = RowsDocument.ForTable("big", 1, Pack, definition, rows);
        var before = document.Rows.ToArray();
        var added = new List<object?[]>();
        for (var i = 0; i < 1_000; i++)
        {
            added.Add([-i]);
        }

        var changes = Record(document);
        DocumentEdit edit = document.InsertRows(50_000, added);
        edit.Do();

        Assert.Equal(101_000, document.Rows.Count);
        Assert.Single(changes);
        Assert.Equal(1_000, changes[0].RowIndexes.Count);
        for (var i = 0; i < 50_000; i++)
        {
            Assert.Same(before[i], document.Rows[i]);
        }

        for (var i = 50_000; i < 100_000; i++)
        {
            Assert.Same(before[i], document.Rows[i + 1_000]);
        }

        Assert.Same(added[0], document.Rows[50_000]);

        edit.Undo();
        Assert.Equal(100_000, document.Rows.Count);
        for (var i = 0; i < before.Length; i++)
        {
            Assert.Same(before[i], document.Rows[i]);
        }
    }
}
