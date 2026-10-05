using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using TotalWarModTool.Editing;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.App;

/// <summary>
/// The editable tab of a table or loc file of a mod project: the grid rows over a <see cref="RowsDocument"/>, the validation of a cell
/// text and the row commands. Every change goes through the global <see cref="ActionHistory"/>; the tab follows the document by its
/// <see cref="RowsDocument.Changed"/> events and changes <see cref="Rows"/> exactly where the document changed (an undo is no reset).
/// The identity of a row is its cell array. The text view is built from <see cref="RowsDocument.ToText"/> and again after every change.
/// Listens to the document until it is disposed.
/// </summary>
public sealed class EditableRowsTabViewModel : RowsTabViewModel
{
    private readonly ActionHistory _history;
    private readonly RowCollection _rows;
    private readonly RelayCommand _deleteRowsCommand;
    private readonly RelayCommand _duplicateRowsCommand;
    private RowsDocument _document;
    private string _header;
    private IReadOnlyList<int> _selectedRows = [];

    public EditableRowsTabViewModel(TabSource source, string entryPath, AppTexts texts, Action<TabViewModel> close,
        RowsDocument document, ActionHistory history)
        : base(RequireProject(source), entryPath, texts, close)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(history);
        _history = history;
        _document = document;
        TabSource.TryGetProjectId(source.Key, out var projectId);
        ProjectId = projectId;
        RelativeFile = entryPath.Replace('/', '\\');
        _rows = new RowCollection(BuildRows(document));
        _header = HeaderText();
        _deleteRowsCommand = new RelayCommand(DeleteRows, HasSelection);
        _duplicateRowsCommand = new RelayCommand(DuplicateRows, HasSelection);
        InsertRowCommand = new RelayCommand(InsertRow);
        Subscribe(document);
    }

    /// <summary>The document the rows show. Another one after <see cref="ReplaceDocument"/>.</summary>
    public RowsDocument Document => _document;

    /// <summary>The id of the project the file belongs to, from the key of the source.</summary>
    public Guid ProjectId { get; }

    /// <summary>The entry path of the tab with '\' as the separator.</summary>
    public string RelativeFile { get; }

    /// <summary>Table, version and row count (a loc file: the row count), in the current language.</summary>
    public string Header => _header;

    public IReadOnlyList<FieldDefinition> Columns => _document.Columns;

    /// <summary>One <see cref="EditRow"/> per row of the document, in the order of the document.</summary>
    public ObservableCollection<EditRow> Rows => _rows;

    /// <summary>True while the document differs from the state at its last save. Raises <c>PropertyChanged</c>; the tab header shows a mark.</summary>
    public bool IsModified => _document.IsModified;

    /// <summary>The rows the user selected, as indexes; set by the view. Indexes outside the rows are ignored by the commands.</summary>
    public IReadOnlyList<int> SelectedRows
    {
        get => _selectedRows;
        set
        {
            _selectedRows = value ?? [];
            OnPropertyChanged();
            RaiseSelectionCommandsChanged();
        }
    }

    /// <summary>A default row after the last selected row, or at the end.</summary>
    public ICommand InsertRowCommand { get; }

    /// <summary>Deletes the selected rows. Disabled without a selection.</summary>
    public ICommand DeleteRowsCommand => _deleteRowsCommand;

    /// <summary>Duplicates the selected rows as one block after the last of them. Disabled without a selection.</summary>
    public ICommand DuplicateRowsCommand => _duplicateRowsCommand;

    internal override bool IsTableEntry => _document.Kind == DocumentKind.Table;

    /// <summary>Null when the text is a valid value of the column, else the UI message: what the column expects, plus the technical reason.</summary>
    public string? CheckCell(int column, string text)
    {
        var check = _document.Check(column, text);
        return check.Problem == CellProblem.None ? null : MessageFor(column, check);
    }

    /// <summary>
    /// Replaces the document after a reload from disk: the rows are built again (one reset of <see cref="Rows"/>) and the old document
    /// no longer reaches the tab. The caller removes the steps of the old document from the history.
    /// </summary>
    internal void ReplaceDocument(RowsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Unsubscribe(_document);
        _document = document;
        Subscribe(document);
        _rows.ReplaceAll(BuildRows(document));
        _selectedRows = [];
        InvalidateText();
        SetProperty(ref _header, HeaderText(), nameof(Header));
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(IsTableEntry));
        OnPropertyChanged(nameof(SelectedRows));
        RaiseSelectionCommandsChanged();
    }

    /// <summary>The setter of <see cref="EditRow"/>: checks the value, then changes the cell through the history.</summary>
    internal void SetCellFromGrid(EditRow row, int column, object? value)
    {
        var index = _rows.IndexOf(row);
        if (index < 0)
        {
            // The row left the tab (a reload replaced the document): there is nothing to change.
            return;
        }

        var text = value switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            null => string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
        var check = _document.Check(column, text);
        if (check.Problem != CellProblem.None)
        {
            throw new FormatException(MessageFor(column, check));
        }

        if (SameValue(row.Cells[column], check.Value))
        {
            return;
        }

        _history.Execute(_document.SetCell(index, column, check.Value));
    }

    protected override TsvWriteResult? WriteText() => _document.ToText();

    protected override void RefreshHeader() => SetProperty(ref _header, HeaderText(), nameof(Header));

    protected override void OnDispose() => Unsubscribe(_document);

    private static TabSource RequireProject(TabSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!TabSource.TryGetProjectId(source.Key, out _))
        {
            throw new ArgumentException("An editable tab belongs to a project", nameof(source));
        }

        return source;
    }

    private List<EditRow> BuildRows(RowsDocument document)
    {
        var rows = new List<EditRow>(document.Rows.Count);
        var columns = document.Columns;
        foreach (var cells in document.Rows)
        {
            rows.Add(new EditRow(cells, columns, this));
        }

        return rows;
    }

    private void Subscribe(RowsDocument document)
    {
        document.Changed += OnDocumentChanged;
        document.ModifiedChanged += OnModifiedChanged;
    }

    private void Unsubscribe(RowsDocument document)
    {
        document.Changed -= OnDocumentChanged;
        document.ModifiedChanged -= OnModifiedChanged;
    }

    // Follows the document in place. Indexes are ascending; an insert gives the final indexes (insert from the lowest up), a removal the
    // indexes before it (remove from the highest down), so that every index is right at the moment it is used.
    private void OnDocumentChanged(object? sender, DocumentChange change)
    {
        switch (change.Kind)
        {
            case DocumentChangeKind.CellsChanged:
                foreach (var index in change.RowIndexes)
                {
                    _rows[index].RaiseChanged();
                }

                break;
            case DocumentChangeKind.RowsInserted:
                foreach (var index in change.RowIndexes)
                {
                    _rows.Insert(index, new EditRow(_document.Rows[index], _document.Columns, this));
                }

                break;
            case DocumentChangeKind.RowsRemoved:
                for (var i = change.RowIndexes.Count - 1; i >= 0; i--)
                {
                    _rows.RemoveAt(change.RowIndexes[i]);
                }

                break;
        }

        if (change.Kind != DocumentChangeKind.CellsChanged)
        {
            SetProperty(ref _header, HeaderText(), nameof(Header));
        }

        InvalidateText();
    }

    private void OnModifiedChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(IsModified));

    private string HeaderText() => _document.Kind == DocumentKind.Table
        ? Texts.TabTableHeader(_document.TableName, _document.Version, _document.Rows.Count)
        : Texts.TabLocHeader(_document.Rows.Count);

    // ----- Commands -----

    private bool HasSelection() => _selectedRows.Any(IsRowIndex);

    private bool IsRowIndex(int index) => index >= 0 && index < _document.Rows.Count;

    // The selection is set by the view and can be stale after a change: only indexes of existing rows count.
    private List<int> ValidSelection() => _selectedRows.Where(IsRowIndex).ToList();

    private void RaiseSelectionCommandsChanged()
    {
        _deleteRowsCommand.RaiseCanExecuteChanged();
        _duplicateRowsCommand.RaiseCanExecuteChanged();
    }

    private void InsertRow()
    {
        var selected = ValidSelection();
        var index = selected.Count > 0 ? selected.Max() + 1 : _document.Rows.Count;
        _history.Execute(_document.InsertRows(index, [_document.NewRow()]));
    }

    private void DeleteRows()
    {
        var selected = ValidSelection();
        if (selected.Count > 0)
        {
            _history.Execute(_document.DeleteRows(selected));
        }
    }

    private void DuplicateRows()
    {
        var selected = ValidSelection();
        if (selected.Count > 0)
        {
            _history.Execute(_document.DuplicateRows(selected));
        }
    }

    // ----- Messages -----

    private string MessageFor(int column, CellCheck check)
    {
        var field = _document.Columns[column];
        var expected = check.Problem == CellProblem.ColourChannelOutOfRange || IsColourChannel(column, field.Type)
            ? Texts.EditExpectedChannel
            : field.Type switch
            {
                FieldType.Boolean => Texts.EditExpectedBoolean,
                FieldType.I16 => Texts.EditExpectedInteger(Invariant(short.MinValue), Invariant(short.MaxValue)),
                FieldType.I32 => Texts.EditExpectedInteger(Invariant(int.MinValue), Invariant(int.MaxValue)),
                FieldType.I64 => Texts.EditExpectedInteger(Invariant(long.MinValue), Invariant(long.MaxValue)),
                FieldType.F32 or FieldType.F64 => Texts.EditExpectedFloat,
                FieldType.ColourRgb => Texts.EditExpectedColour,
                FieldType.StringU8 or FieldType.StringU16 or FieldType.OptionalStringU8 => Texts.EditExpectedText,
                _ => throw new InvalidOperationException("The column has a type that cannot be edited: " + field.Type),
            };
        return Texts.EditCheckMessage(expected, DisplayText.Safe(check.Detail ?? string.Empty));
    }

    // The document does not name its colour channel columns. A number column is one when the document refuses 256 there for the range.
    private bool IsColourChannel(int column, FieldType type) =>
        type is FieldType.I16 or FieldType.I32 or FieldType.I64 or FieldType.F32 or FieldType.F64
        && _document.Check(column, "256").Problem == CellProblem.ColourChannelOutOfRange;

    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);

    // Floats are compared by their bits: 0 and -0 differ, and so do two NaNs with different payloads; everything else by value.
    private static bool SameValue(object? current, object? next) => (current, next) switch
    {
        (float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b),
        (double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b),
        _ => Equals(current, next),
    };

    // An ObservableCollection that can be replaced as a whole with one Reset instead of one event per row.
    private sealed class RowCollection(List<EditRow> rows) : ObservableCollection<EditRow>(rows)
    {
        public void ReplaceAll(List<EditRow> replacement)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (var row in replacement)
            {
                Items.Add(row);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
