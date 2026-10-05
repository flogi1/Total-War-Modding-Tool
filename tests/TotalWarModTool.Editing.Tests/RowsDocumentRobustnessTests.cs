using TotalWarModTool.Editing;
using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.Editing.Tests;

// The same class as RowsDocumentTests (helpers, test definition); this part covers misbehaving handlers and callers and several documents.
public partial class RowsDocumentTests
{
    private sealed class HandlerFailure : Exception
    {
    }

    private static readonly (string Name, Func<RowsDocument, DocumentEdit> Make)[] Commands =
    [
        ("set cell", d => d.SetCell(2, Text, "changed")),
        ("insert rows", d => d.InsertRows(1, [Row(30), Row(31)])),
        ("delete rows", d => d.DeleteRows([0, 3])),
        ("duplicate rows", d => d.DuplicateRows([1, 2])),
    ];

    // A handler that throws while fail() is true: for Changed, or for ModifiedChanged (which only fires when a step changes IsModified).
    private static void Throwing(RowsDocument document, bool onModified, Func<bool> fail)
    {
        void Handler()
        {
            if (fail())
            {
                throw new HandlerFailure();
            }
        }

        if (onModified)
        {
            document.ModifiedChanged += (_, _) => Handler();
        }
        else
        {
            document.Changed += (_, _) => Handler();
        }
    }

    [Fact]
    public void A_handler_that_throws_during_Do_rolls_the_edit_back_and_leaves_the_history_usable()
    {
        foreach (var onModified in new[] { false, true })
        {
            foreach (var (name, make) in Commands)
            {
                var document = NewTable(5);
                var history = new ActionHistory();
                var original = Snapshot(document);
                history.Execute(document.SetCell(4, Num, 41));
                document.MarkSaved();
                var saved = Snapshot(document);

                // The counter comes first, so it sees every Changed event that is raised.
                var calls = 0;
                document.Changed += (_, _) => calls++;
                var failing = false;
                Throwing(document, onModified, () => failing);
                var step = make(document);
                failing = true;
                Assert.Throws<HandlerFailure>(() => history.Execute(step));
                failing = false;

                // The state is as before the Do, the history did not record the step, and the rollback raised no second Changed.
                AssertSameState(saved, document);
                Assert.False(document.IsModified, name);
                Assert.Equal(1, calls);
                Assert.NotSame(step, history.NextUndo);

                // The step can run again, and everything still undoes in order.
                calls = 0;
                history.Execute(step);
                Assert.Equal(1, calls);
                Assert.True(document.IsModified, name);
                history.Undo();
                AssertSameState(saved, document);
                Assert.False(document.IsModified, name);
                history.Undo();
                AssertSameState(original, document);
                Assert.True(document.IsModified, name);
                Assert.False(history.CanUndo);
            }
        }
    }

    [Fact]
    public void A_handler_that_throws_during_a_redo_rolls_the_redo_back()
    {
        foreach (var (name, make) in Commands)
        {
            var document = NewTable(5);
            var history = new ActionHistory();
            var original = Snapshot(document);
            history.Execute(document.SetCell(4, Num, 41));
            document.MarkSaved();
            var saved = Snapshot(document);
            history.Execute(make(document));
            history.Undo();

            var failing = true;
            Throwing(document, false, () => failing);
            Assert.Throws<HandlerFailure>(history.Redo);
            failing = false;

            AssertSameState(saved, document);
            Assert.False(document.IsModified, name);
            Assert.False(history.CanRedo);
            history.Undo();
            AssertSameState(original, document);
            Assert.True(document.IsModified, name);
        }
    }

    [Fact]
    public void A_handler_that_throws_during_Undo_leaves_the_state_undone()
    {
        foreach (var (name, make) in Commands)
        {
            var document = NewTable(5);
            var history = new ActionHistory();
            var original = Snapshot(document);
            history.Execute(document.SetCell(4, Num, 41));
            document.MarkSaved();
            var saved = Snapshot(document);
            history.Execute(make(document));

            var failing = true;
            Throwing(document, false, () => failing);
            Assert.Throws<HandlerFailure>(history.Undo);
            failing = false;

            // The history dropped the step and the document is undone, so both agree.
            AssertSameState(saved, document);
            Assert.False(document.IsModified, name);
            Assert.False(history.CanRedo);
            history.Undo();
            AssertSameState(original, document);
            Assert.True(document.IsModified, name);
            Assert.False(history.CanUndo);
        }
    }

    [Fact]
    public void InsertRows_refuses_an_array_that_is_already_a_row_of_the_document()
    {
        var document = NewTable(3);

        Assert.Throws<ArgumentException>(() => document.InsertRows(0, [document.Rows[1]]));
        Assert.Throws<ArgumentException>(() => document.InsertRows(0, [Row(40), document.Rows[2]]));

        // A row that left the document can come back, and a copy made by DuplicateRows is its own array.
        var gone = document.Rows[1];
        document.DeleteRows([1]).Do();
        Assert.Equal(2, document.Rows.Count);
        document.InsertRows(0, [gone]).Do();
        Assert.Same(gone, document.Rows[0]);
        Assert.Throws<ArgumentException>(() => document.InsertRows(0, [gone]));
        document.DuplicateRows([0]).Do();
        Assert.Equal(4, document.Rows.Count);

        // Two steps made for the same array cannot both run, and the refused one changes nothing.
        var fresh = Row(50);
        var first = document.InsertRows(0, [fresh]);
        var second = document.InsertRows(0, [fresh]);
        first.Do();
        Assert.Throws<InvalidOperationException>(second.Do);
        Assert.Equal(5, document.Rows.Count);

        // The same rule when the document is made.
        var twice = Row(1);
        Assert.Throws<ArgumentException>(() => RowsDocument.ForTable(TableName, 2, Pack, Definition, [twice, twice]));
    }

    [Fact]
    public void The_row_indexes_of_a_change_are_read_only_and_not_the_array_of_the_step()
    {
        var document = NewTable(5);
        var seen = new List<IReadOnlyList<int>>();
        document.Changed += (_, c) => seen.Add(c.RowIndexes);
        var edit = document.DeleteRows([3, 1]);

        edit.Do();
        Assert.IsNotType<int[]>(seen[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)seen[0])[0] = 4);
        edit.Undo();
        edit.Do();

        Assert.Equal([1, 3], seen[0]);
        Assert.Equal([1, 3], seen[1]);
        Assert.Equal([1, 3], seen[2]);
        Assert.Equal(3, document.Rows.Count);
    }

    [Fact]
    public void A_float_must_come_back_as_the_same_bits()
    {
        var document = NewTable(2);
        var payloadSingle = BitConverter.Int32BitsToSingle(0x7FC00001);
        var payloadDouble = BitConverter.Int64BitsToDouble(0x7FF8000000000001);

        Assert.Throws<ArgumentException>(() => document.SetCell(0, Single, payloadSingle));
        Assert.Throws<ArgumentException>(() => document.SetCell(0, Double, payloadDouble));
        Assert.Throws<ArgumentException>(() => document.InsertRows(0, [WithCell(Row(60), Single, payloadSingle)]));
        Assert.Throws<ArgumentException>(() => document.InsertRows(0, [WithCell(Row(61), Double, payloadDouble)]));

        // The standard NaN, the infinities and negative zero are what the writer reads back.
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0f, float.Epsilon })
        {
            document.SetCell(0, Single, value).Do();
        }

        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.0, double.Epsilon })
        {
            document.SetCell(0, Double, value).Do();
        }

        Assert.NotNull(document.ToText().Text);
    }

    [Fact]
    public void Redo_raises_the_same_exact_change_as_the_first_run_for_every_command()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["set cell"] = ["CellsChanged:2:" + Text, "CellsChanged:2:" + Text, "CellsChanged:2:" + Text],
            ["insert rows"] = ["RowsInserted:1,2:-1", "RowsRemoved:1,2:-1", "RowsInserted:1,2:-1"],
            ["delete rows"] = ["RowsRemoved:0,3:-1", "RowsInserted:0,3:-1", "RowsRemoved:0,3:-1"],
            ["duplicate rows"] = ["RowsInserted:3,4:-1", "RowsRemoved:3,4:-1", "RowsInserted:3,4:-1"],
        };

        foreach (var (name, make) in Commands)
        {
            var document = NewTable(5);
            var history = new ActionHistory();
            var changes = Record(document);

            history.Execute(make(document));
            history.Undo();
            history.Redo();

            Assert.Equal(expected[name], changes.Select(Describe));
        }
    }

    [Fact]
    public void Insert_at_the_start_and_at_the_end_give_exact_indexes()
    {
        var document = NewTable(3);
        var original = document.Rows.ToArray();
        var changes = Record(document);
        var atStart = new[] { Row(70), Row(71) };
        var atEnd = new[] { Row(72), Row(73) };
        var first = document.InsertRows(0, atStart);

        first.Do();
        var second = document.InsertRows(5, atEnd);
        second.Do();
        Assert.Equal(7, document.Rows.Count);
        Assert.Same(atStart[0], document.Rows[0]);
        Assert.Same(atStart[1], document.Rows[1]);
        Assert.Same(original[0], document.Rows[2]);
        Assert.Same(original[2], document.Rows[4]);
        Assert.Same(atEnd[0], document.Rows[5]);
        Assert.Same(atEnd[1], document.Rows[6]);

        second.Undo();
        first.Undo();
        Assert.Equal(original, document.Rows);
        Assert.Equal(
            ["RowsInserted:0,1:-1", "RowsInserted:5,6:-1", "RowsRemoved:5,6:-1", "RowsRemoved:0,1:-1"],
            changes.Select(Describe));
    }

    [Fact]
    public void Two_documents_in_one_history_undo_in_reverse_order_and_RemoveWhere_leaves_the_other_alone()
    {
        var a = NewTable(3);
        var b = NewTable(3);
        var history = new ActionHistory();

        history.Execute(a.SetCell(0, Num, 100));
        a.MarkSaved();
        history.Execute(b.SetCell(0, Num, 200));
        history.Execute(a.SetCell(1, Num, 101));
        Assert.True(a.IsModified);
        Assert.True(b.IsModified);

        history.Undo();
        Assert.Equal(1, a.Rows[1][Num]);
        Assert.Equal(200, b.Rows[0][Num]);
        Assert.False(a.IsModified);
        Assert.True(b.IsModified);

        history.Undo();
        Assert.Equal(0, b.Rows[0][Num]);
        Assert.Equal(100, a.Rows[0][Num]);
        Assert.False(b.IsModified);
        Assert.False(a.IsModified);

        history.Undo();
        Assert.Equal(0, a.Rows[0][Num]);
        Assert.True(a.IsModified);
        Assert.False(b.IsModified);

        history.Redo();
        history.Redo();
        history.Redo();
        Assert.Equal(101, a.Rows[1][Num]);
        Assert.Equal(200, b.Rows[0][Num]);
        Assert.True(a.IsModified);
        Assert.True(b.IsModified);

        // The steps of A leave the history; the step of B stays undoable and works.
        var removed = history.RemoveWhere(step => step is DocumentEdit edit && ReferenceEquals(edit.Document, a));
        Assert.Equal(2, removed);
        Assert.True(history.CanUndo);
        history.Undo();
        Assert.Equal(0, b.Rows[0][Num]);
        Assert.False(b.IsModified);
        Assert.False(history.CanUndo);
        Assert.Equal(101, a.Rows[1][Num]);
    }
}
