using System.Collections.ObjectModel;
using System.Globalization;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Editing;

/// <summary>
/// The document of one table or loc file: its rows, the validation of a cell text, the modified state and the commands. Every
/// command returns a <see cref="DocumentEdit"/> that the caller runs through the global undo history; nothing here changes the rows
/// directly. The document never holds a cell that <see cref="ToText"/> could not write, as far as the commands and
/// <see cref="Check"/> go: the cell rules are those of <see cref="CellText"/>, shared with <see cref="TableTsv"/>.
/// Not thread-safe; it belongs to the UI thread.
/// </summary>
public sealed class RowsDocument
{
    private const string LocTableName = "Loc";

    // The version of every loc file; the same value as the decoder and the writer use.
    private const int LocVersion = 1;

    private readonly TableDefinition? _definition;
    private readonly IReadOnlySet<int> _channelColumns;
    private readonly List<object?[]> _rows;
    private readonly ReadOnlyCollection<object?[]> _rowsView;

    // The steps that are applied now, in order. A step leaves the list when it is undone.
    private readonly List<DocumentEdit> _applied = [];

    // The step that was applied last when MarkSaved was called; null before the first step.
    private DocumentEdit? _savedStep;

    private RowsDocument(DocumentKind kind, string tableName, int version, string packPath, TableDefinition? definition,
        IReadOnlyList<FieldDefinition> columns, IReadOnlySet<int> channelColumns, List<object?[]> rows)
    {
        Kind = kind;
        TableName = tableName;
        Version = version;
        PackPath = packPath;
        _definition = definition;
        Columns = columns;
        _channelColumns = channelColumns;
        _rows = rows;
        _rowsView = new ReadOnlyCollection<object?[]>(rows);
    }

    /// <summary>The kind of file.</summary>
    public DocumentKind Kind { get; }

    /// <summary>The path of the file in the pack; passed on unchanged to the writer.</summary>
    public string PackPath { get; }

    /// <summary>The table name; "Loc" for a loc file.</summary>
    public string TableName { get; }

    /// <summary>The version of the table definition; 1 for a loc file.</summary>
    public int Version { get; }

    /// <summary>The columns, one per field. A loc file has "key" (StringU16), "text" (StringU16) and "tooltip" (Boolean).</summary>
    public IReadOnlyList<FieldDefinition> Columns { get; }

    /// <summary>The rows as a live view. A row array is the identity of a row; its cells are those of DecodedTable.</summary>
    public IReadOnlyList<object?[]> Rows => _rowsView;

    /// <summary>False exactly when the step that was applied last is the one that was applied at the last <see cref="MarkSaved"/> (initially none).</summary>
    public bool IsModified => !ReferenceEquals(LastApplied, _savedStep);

    /// <summary>Raised after every Do and Undo of an edit of this document.</summary>
    public event EventHandler<DocumentChange>? Changed;

    /// <summary>Raised when <see cref="IsModified"/> changes its value.</summary>
    public event EventHandler? ModifiedChanged;

    internal List<object?[]> RowList => _rows;

    private DocumentEdit? LastApplied => _applied.Count == 0 ? null : _applied[^1];

    /// <summary>
    /// A DB table. The rows are copied into a list; the row arrays themselves are taken over and owned by the document. The cells of
    /// the rows are not validated (ToText reports what the dialect cannot write); the cell count and the field types are.
    /// Throws ArgumentException for a field of the type Unsupported or a row with the wrong number of cells.
    /// </summary>
    public static RowsDocument ForTable(string tableName, int version, string packPath, TableDefinition definition, IReadOnlyList<object?[]> rows)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(packPath);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(rows);

        foreach (var field in definition.Fields)
        {
            if (field.Type == FieldType.Unsupported)
            {
                throw new ArgumentException($"The field '{field.Name}' has the unsupported type '{field.RawType}'", nameof(definition));
            }
        }

        var list = new List<object?[]>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row is null || row.Length != definition.Fields.Count)
            {
                throw new ArgumentException(
                    $"Row {i.ToString(CultureInfo.InvariantCulture)} has {row?.Length.ToString(CultureInfo.InvariantCulture) ?? "no"} cells, " +
                    $"the definition has {definition.Fields.Count.ToString(CultureInfo.InvariantCulture)} fields", nameof(rows));
            }

            list.Add(row);
        }

        return new RowsDocument(DocumentKind.Table, tableName, version, packPath, definition, definition.Fields,
            CellText.ColourChannelFields(definition), list);
    }

    /// <summary>A loc file: three columns "key" (StringU16), "text" (StringU16), "tooltip" (Boolean).</summary>
    public static RowsDocument ForLoc(string packPath, IReadOnlyList<LocRow> rows)
    {
        ArgumentNullException.ThrowIfNull(packPath);
        ArgumentNullException.ThrowIfNull(rows);

        FieldDefinition[] columns =
        [
            new("key", FieldType.StringU16, nameof(FieldType.StringU16), true, null),
            new("text", FieldType.StringU16, nameof(FieldType.StringU16), false, null),
            new("tooltip", FieldType.Boolean, nameof(FieldType.Boolean), false, null),
        ];
        var list = new List<object?[]>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i] ?? throw new ArgumentException($"Row {i.ToString(CultureInfo.InvariantCulture)} is null", nameof(rows));
            list.Add([row.Key, row.Text, row.Tooltip]);
        }

        return new RowsDocument(DocumentKind.Loc, LocTableName, LocVersion, packPath, null, columns, new HashSet<int>(), list);
    }

    /// <summary>
    /// Parses a cell text by the type of the column with the rules of <see cref="CellText.TryParse"/>; a column of a colour group
    /// must in addition hold an integer from 0 to 255 (<see cref="CellText.IsColourChannelValue"/>).
    /// </summary>
    public CellCheck Check(int column, string text)
    {
        RequireColumn(column);
        ArgumentNullException.ThrowIfNull(text);

        if (!CellText.TryParse(Columns[column].Type, text, out var value, out var reason))
        {
            return new CellCheck(null, CellProblem.InvalidValue, reason);
        }

        if (_channelColumns.Contains(column) && !CellText.IsColourChannelValue(value))
        {
            return new CellCheck(null, CellProblem.ColourChannelOutOfRange, "a colour channel must be an integer from 0 to 255");
        }

        return new CellCheck(value, CellProblem.None, null);
    }

    /// <summary>A new row with <see cref="CellText.DefaultValue"/> in every column. Not part of the document until it is inserted.</summary>
    public object?[] NewRow()
    {
        var row = new object?[Columns.Count];
        for (var i = 0; i < row.Length; i++)
        {
            row[i] = CellText.DefaultValue(Columns[i].Type);
        }

        return row;
    }

    /// <summary>
    /// Sets one cell. The value must have the cell type of the column, be a text the dialect can carry and, in a colour channel, an
    /// integer from 0 to 255; ArgumentException otherwise. Setting the value the cell already has still gives a step.
    /// </summary>
    public DocumentEdit SetCell(int row, int column, object? value)
    {
        if (row < 0 || row >= _rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, "The row does not exist");
        }

        RequireColumn(column);
        RequireWritableValue(column, value, nameof(value));
        return DocumentEdit.ForSetCell(this, row, _rows[row], column, value);
    }

    /// <summary>
    /// Inserts rows so that the first gets <paramref name="index"/> (0 to the row count). The row arrays are taken over by the document:
    /// each needs one cell per column, writable values, and no array may occur twice. The list itself is copied.
    /// </summary>
    public DocumentEdit InsertRows(int index, IReadOnlyList<object?[]> rows)
    {
        if (index < 0 || index > _rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The index is outside the rows");
        }

        return DocumentEdit.ForInsert(this, "insert rows", index, CopyOfNewRows(rows));
    }

    /// <summary>Deletes rows. The indexes may be unsorted and may repeat; a repeated index counts once. ArgumentException for none.</summary>
    public DocumentEdit DeleteRows(IReadOnlyList<int> rowIndexes)
    {
        var indexes = DistinctRowIndexes(rowIndexes);
        var rows = new object?[indexes.Length][];
        for (var i = 0; i < indexes.Length; i++)
        {
            rows[i] = _rows[indexes[i]];
        }

        return DocumentEdit.ForDelete(this, indexes, rows);
    }

    /// <summary>
    /// Inserts a copy of each given row (in the order of the indexes, repeated indexes once) as one block after the last given row.
    /// The copies are made now and are independent of the originals. ArgumentException for no index.
    /// </summary>
    public DocumentEdit DuplicateRows(IReadOnlyList<int> rowIndexes)
    {
        var indexes = DistinctRowIndexes(rowIndexes);
        var copies = new object?[indexes.Length][];
        for (var i = 0; i < indexes.Length; i++)
        {
            copies[i] = (object?[])_rows[indexes[i]].Clone();
        }

        return DocumentEdit.ForInsert(this, "duplicate rows", indexes[^1] + 1, copies);
    }

    /// <summary>The saved point is the state after the step that was applied last.</summary>
    public void MarkSaved()
    {
        var before = IsModified;
        _savedStep = LastApplied;
        if (before != IsModified)
        {
            ModifiedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The TSV text of the file (<see cref="TableTsv.Write"/> or <see cref="TableTsv.WriteLoc"/>). Text is null when the dialect refuses
    /// it; Detail says why. A pack path the dialect cannot carry throws ArgumentException, as the writer does.
    /// </summary>
    public TsvWriteResult ToText()
    {
        if (_definition is not null)
        {
            return TableTsv.Write(TableName, Version, PackPath, _definition, _rows);
        }

        var rows = new List<LocRow>(_rows.Count);
        foreach (var row in _rows)
        {
            rows.Add(new LocRow((string)row[0]!, (string)row[1]!, (bool)row[2]!));
        }

        return TableTsv.WriteLoc(PackPath, rows);
    }

    // ----- For DocumentEdit -----

    // Puts the rows at the final indexes (ascending) in one pass from the back; the other rows keep their order.
    internal void InsertAt(int[] indexes, object?[][] rows)
    {
        var oldCount = _rows.Count;
        var count = indexes.Length;
        if (indexes[^1] >= oldCount + count)
        {
            throw new InvalidOperationException("The rows no longer fit the document");
        }

        for (var i = 0; i < count; i++)
        {
            _rows.Add(null!);
        }

        var source = oldCount - 1;
        var target = oldCount + count - 1;
        var next = count - 1;
        while (next >= 0)
        {
            if (target == indexes[next])
            {
                _rows[target] = rows[next];
                next--;
            }
            else
            {
                _rows[target] = _rows[source];
                source--;
            }

            target--;
        }
    }

    // Removes the rows at the indexes (ascending) in one pass; they must be the given arrays.
    internal void RemoveAt(int[] indexes, object?[][] rows)
    {
        for (var i = 0; i < indexes.Length; i++)
        {
            if (indexes[i] >= _rows.Count || !ReferenceEquals(_rows[indexes[i]], rows[i]))
            {
                throw new InvalidOperationException("The rows of the step are no longer at their indexes");
            }
        }

        var target = indexes[0];
        var next = 0;
        for (var source = indexes[0]; source < _rows.Count; source++)
        {
            if (next < indexes.Length && indexes[next] == source)
            {
                next++;
            }
            else
            {
                _rows[target++] = _rows[source];
            }
        }

        _rows.RemoveRange(target, _rows.Count - target);
    }

    internal void RequireLastApplied(DocumentEdit edit)
    {
        if (!ReferenceEquals(LastApplied, edit))
        {
            throw new InvalidOperationException("Only the step that was applied last can be undone");
        }
    }

    internal void StepApplied(DocumentEdit edit, DocumentChange change)
    {
        var before = IsModified;
        _applied.Add(edit);
        Raise(change, before);
    }

    internal void StepReverted(DocumentEdit edit, DocumentChange change)
    {
        var before = IsModified;
        _applied.RemoveAt(_applied.Count - 1);
        Raise(change, before);
    }

    // ----- Checks -----

    private void Raise(DocumentChange change, bool modifiedBefore)
    {
        Changed?.Invoke(this, change);
        if (modifiedBefore != IsModified)
        {
            ModifiedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RequireColumn(int column)
    {
        if (column < 0 || column >= Columns.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "The column does not exist");
        }
    }

    // The type must fit (CellText.Format throws), the cell must be accepted by CellText.TryParse in its written form, and a colour
    // channel must be an integer from 0 to 255: what a command accepts is what ToText can write.
    private void RequireWritableValue(int column, object? value, string parameter)
    {
        var field = Columns[column];
        try
        {
            var text = CellText.Format(field.Type, value);
            if (!CellText.TryParse(field.Type, text, out _, out var reason))
            {
                throw new ArgumentException(reason, parameter);
            }

            if (_channelColumns.Contains(column) && !CellText.IsColourChannelValue(value))
            {
                throw new ArgumentException("A colour channel must be an integer from 0 to 255", parameter);
            }
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Column '{field.Name}': {ex.Message}", parameter, ex);
        }
    }

    private object?[][] CopyOfNewRows(IReadOnlyList<object?[]> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            throw new ArgumentException("There are no rows", nameof(rows));
        }

        var copy = new object?[rows.Count][];
        var seen = new HashSet<object?[]>(rows.Count, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < copy.Length; i++)
        {
            var row = rows[i];
            if (row is null || row.Length != Columns.Count)
            {
                throw new ArgumentException(
                    $"Row {i.ToString(CultureInfo.InvariantCulture)} has {row?.Length.ToString(CultureInfo.InvariantCulture) ?? "no"} cells, " +
                    $"the document has {Columns.Count.ToString(CultureInfo.InvariantCulture)} columns", nameof(rows));
            }

            if (!seen.Add(row))
            {
                throw new ArgumentException($"Row {i.ToString(CultureInfo.InvariantCulture)} is the same array as an earlier row", nameof(rows));
            }

            for (var c = 0; c < row.Length; c++)
            {
                RequireWritableValue(c, row[c], nameof(rows));
            }

            copy[i] = row;
        }

        return copy;
    }

    // Ascending and without repeats; ArgumentException for an empty list, ArgumentOutOfRangeException for an index outside the rows.
    private int[] DistinctRowIndexes(IReadOnlyList<int> rowIndexes)
    {
        ArgumentNullException.ThrowIfNull(rowIndexes);
        if (rowIndexes.Count == 0)
        {
            throw new ArgumentException("There are no row indexes", nameof(rowIndexes));
        }

        var indexes = rowIndexes.Distinct().Order().ToArray();
        if (indexes[0] < 0 || indexes[^1] >= _rows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndexes), "An index is outside the rows");
        }

        return indexes;
    }
}
