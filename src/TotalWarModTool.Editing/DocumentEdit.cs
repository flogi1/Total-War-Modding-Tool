using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.Editing;

/// <summary>
/// One undo step of one <see cref="RowsDocument"/>, created by one of its commands and not yet run. <see cref="Do"/> and
/// <see cref="Undo"/> raise <see cref="RowsDocument.Changed"/>. A step holds only its own rows and indexes, never a copy of the table.
/// It must be undone in the reverse order of its execution, which is what <see cref="ActionHistory"/> does.
/// </summary>
public sealed class DocumentEdit : IUndoableAction
{
    private enum Operation { SetCell, Insert, Delete }

    private readonly Operation _operation;

    // SetCell: the one row. Insert: the rows to insert. Delete: the rows to remove, as they were at the indexes.
    private readonly object?[][] _rows;

    // SetCell: the row index. Insert: the indexes the rows get. Delete: the indexes the rows have. Always ascending.
    private readonly int[] _indexes;

    private readonly int _column;
    private readonly object? _value;
    private object? _oldValue;
    private bool _done;

    private DocumentEdit(RowsDocument document, string description, Operation operation, object?[][] rows, int[] indexes, int column, object? value)
    {
        Document = document;
        Description = description + " " + document.PackPath;
        _operation = operation;
        _rows = rows;
        _indexes = indexes;
        _column = column;
        _value = value;
    }

    /// <summary>The document this step belongs to.</summary>
    public RowsDocument Document { get; }

    /// <summary>"set cell", "insert rows", "delete rows" or "duplicate rows", then a space and the pack path.</summary>
    public string Description { get; }

    internal static DocumentEdit ForSetCell(RowsDocument document, int row, object?[] rowArray, int column, object? value) =>
        new(document, "set cell", Operation.SetCell, [rowArray], [row], column, value);

    internal static DocumentEdit ForInsert(RowsDocument document, string description, int index, object?[][] rows)
    {
        var indexes = new int[rows.Length];
        for (var i = 0; i < indexes.Length; i++)
        {
            indexes[i] = index + i;
        }

        return new DocumentEdit(document, description, Operation.Insert, rows, indexes, -1, null);
    }

    internal static DocumentEdit ForDelete(RowsDocument document, int[] indexes, object?[][] rows) =>
        new(document, "delete rows", Operation.Delete, rows, indexes, -1, null);

    /// <summary>Performs the step. Throws InvalidOperationException when it has already run, or when the document no longer is in the state the step was made for.</summary>
    public void Do()
    {
        if (_done)
        {
            throw new InvalidOperationException("The step has already been done");
        }

        DocumentChange change;
        switch (_operation)
        {
            case Operation.SetCell:
                _oldValue = SetCellValue(_value);
                change = new DocumentChange(DocumentChangeKind.CellsChanged, _indexes, _column);
                break;
            case Operation.Insert:
                Document.InsertAt(_indexes, _rows);
                change = new DocumentChange(DocumentChangeKind.RowsInserted, _indexes, -1);
                break;
            default:
                Document.RemoveAt(_indexes, _rows);
                change = new DocumentChange(DocumentChangeKind.RowsRemoved, _indexes, -1);
                break;
        }

        _done = true;
        Document.StepApplied(this, change);
    }

    /// <summary>Reverts the step. Throws InvalidOperationException when it has not run, or when it is not the step the document applied last.</summary>
    public void Undo()
    {
        if (!_done)
        {
            throw new InvalidOperationException("The step has not been done");
        }

        Document.RequireLastApplied(this);
        DocumentChange change;
        switch (_operation)
        {
            case Operation.SetCell:
                SetCellValue(_oldValue);
                change = new DocumentChange(DocumentChangeKind.CellsChanged, _indexes, _column);
                break;
            case Operation.Insert:
                Document.RemoveAt(_indexes, _rows);
                change = new DocumentChange(DocumentChangeKind.RowsRemoved, _indexes, -1);
                break;
            default:
                Document.InsertAt(_indexes, _rows);
                change = new DocumentChange(DocumentChangeKind.RowsInserted, _indexes, -1);
                break;
        }

        _done = false;
        _oldValue = null;
        Document.StepReverted(this, change);
    }

    // Writes the value into the cell and returns the value that was there. The row must still be at its index.
    private object? SetCellValue(object? value)
    {
        var row = _rows[0];
        if (_indexes[0] >= Document.RowList.Count || !ReferenceEquals(Document.RowList[_indexes[0]], row))
        {
            throw new InvalidOperationException("The row of the step is no longer at its index");
        }

        var old = row[_column];
        row[_column] = value;
        return old;
    }
}
