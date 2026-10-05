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
    private readonly RelayCommand _pasteRowsCommand;
    private readonly RelayCommand _reloadCommand;
    private RowsDocument _document;
    private string _header;
    private bool _pasting;
    private bool _hasPendingEdit;
    private Func<string?>? _validation;
    private bool _disposed;

    /// <summary>The number of rows from which a change of the document rebuilds <see cref="Rows"/> in one pass (see <see cref="LargeChangeRows"/>).</summary>
    internal const int DefaultLargeChangeRows = 2_000;

    /// <summary>The longest text that a paste reads, in characters; a longer one is refused before it is parsed.</summary>
    public const int DefaultMaxPasteChars = 20_000_000;

    /// <param name="clipboard">Where copy and paste go; null disables both commands.</param>
    public EditableRowsTabViewModel(TabSource source, string entryPath, AppTexts texts, Action<TabViewModel> close,
        RowsDocument document, ActionHistory history, IClipboard? clipboard = null)
        : base(RequireProject(source), entryPath, texts, close, clipboard)
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
        _pasteRowsCommand = new RelayCommand(PasteRows, () => Clipboard is not null);
        InsertRowCommand = new RelayCommand(InsertRow);
        _reloadCommand = new RelayCommand(() => ReloadHandler?.Invoke(this), () => CanReload);
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
    public override bool IsModified => _document.IsModified;

    /// <summary>What the reload button does; set by <see cref="DocumentsController"/>. Without it the command does nothing.</summary>
    internal Action<EditableRowsTabViewModel>? ReloadHandler { get; set; }

    /// <summary>Reads the file again and replaces the content of the tab. Enabled when <see cref="CanReload"/>.</summary>
    public ICommand ReloadCommand => _reloadCommand;

    /// <summary>
    /// True when the file was changed or deleted outside and the tab has no changes of its own: only then is the reload button offered. A tab with
    /// changes is reloaded only through the question that comes with saving it.
    /// </summary>
    public bool CanReload => IsStaleOutside && !IsModified;

    /// <summary>A default row after the last selected row, or at the end.</summary>
    public ICommand InsertRowCommand { get; }

    /// <summary>Deletes the selected rows. Disabled without a selection.</summary>
    public ICommand DeleteRowsCommand => _deleteRowsCommand;

    /// <summary>Duplicates the selected rows as one block after the last of them. Disabled without a selection.</summary>
    public ICommand DuplicateRowsCommand => _duplicateRowsCommand;

    /// <summary>
    /// Inserts the rows of the text on the clipboard (the format that <see cref="RowsTabViewModel.CopyRowsCommand"/> writes) as one step after the
    /// last selected row, or at the end. A text that is refused changes nothing and says why in <see cref="RowsTabViewModel.Notice"/>.
    /// Disabled without a clipboard.
    /// </summary>
    public ICommand PasteRowsCommand => _pasteRowsCommand;

    /// <summary>
    /// Raised after a row command (insert, duplicate, paste, delete) with the rows that the view should select: the new rows, or after a
    /// delete the row that took the place of the first deleted one (none when no row is left). View wiring: the grid selects them and
    /// scrolls the first into view.
    /// </summary>
    public event EventHandler<IReadOnlyList<int>>? SelectionRequested;

    /// <summary>
    /// Commits the cell that is being edited in the grid and returns whether that worked; set by the view. False means the cell stays in
    /// edit mode because its value is not valid. Without a view nothing is pending.
    /// </summary>
    public Func<bool>? CommitPendingEdit { get; set; }

    /// <summary>
    /// Drops the cell that is being edited in the grid without writing its text anywhere and without a notice; set by the view. For the
    /// moment in which the app replaces the document on purpose (the user chose "Reload"): no editor of the old document may write into the
    /// new one. Without a view nothing is pending.
    /// </summary>
    public Action? CancelPendingEdit { get; set; }

    /// <summary>
    /// True while a cell is in edit mode in the grid and its text differs from the value of the cell (set by the view). The text that is
    /// typed is not part of the document yet, but saving has to take it along, so the save commands are enabled by it. A cell that is opened
    /// and not changed is no change.
    /// </summary>
    public bool HasPendingEdit => _hasPendingEdit;

    /// <summary>
    /// The message of the cell that holds an invalid value, shown in a line under the header, in the current language; null while no cell has
    /// an error. Set by the view.
    /// </summary>
    public string? ValidationMessage => _validation?.Invoke();

    /// <summary>
    /// Commits the pending cell edit before an action that needs the document as the user sees it (save, close, build). True when nothing is
    /// pending or the commit worked; false when the cell stays in edit mode because its value is not valid, and the action must not go on.
    /// </summary>
    public bool TryCommitPendingEdit() => CommitPendingEdit?.Invoke() ?? true;

    /// <summary>
    /// Drops the pending cell edit on purpose (see <see cref="CancelPendingEdit"/>): its text is written nowhere and no notice is set. Afterwards
    /// no cell is pending and no cell message shows, also without a view.
    /// </summary>
    internal void DiscardPendingEdit()
    {
        CancelPendingEdit?.Invoke();
        SetPendingEdit(false);
    }

    /// <summary>The view tells whether a cell is in edit mode.</summary>
    internal void SetPendingEdit(bool pending)
    {
        if (SetProperty(ref _hasPendingEdit, pending, nameof(HasPendingEdit)) && !pending)
        {
            SetValidationMessage(null);
        }
    }

    /// <summary>The view tells the message of the cell with an error, or null when there is none any more.</summary>
    internal void SetValidationMessage(string? message) => SetValidationCheck(message is null ? null : () => message);

    /// <summary>
    /// The view tells how to work out the message of the cell with an error (null: no error any more). The function is asked again when
    /// the language changes, like the notice.
    /// </summary>
    internal void SetValidationCheck(Func<string?>? message)
    {
        var before = ValidationMessage;
        _validation = message;
        if (before != ValidationMessage)
        {
            OnPropertyChanged(nameof(ValidationMessage));
        }
    }

    /// <summary>The view dropped an edit that was not committed (the row went away under it, the tab was left some other way): the user is told.</summary>
    internal void NoteDiscardedEdit() => SetNotice(t => t.GridEditDiscarded);

    /// <summary>Writes a typed text to a cell the way the grid does; false when the text is not a value of the column (nothing changes).</summary>
    internal bool TryApplyText(EditRow row, int column, string text)
    {
        if (!_rows.Contains(row))
        {
            return false;                                                                   // the row is no longer part of the tab
        }

        try
        {
            SetCellFromGrid(row, column, text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    protected override void RefreshMoreTexts()
    {
        if (_validation is not null)
        {
            OnPropertyChanged(nameof(ValidationMessage));
        }
    }

    // The cell is committed before the text is built from the document; a value that cannot be committed keeps the grid on the screen.
    protected override bool CanShowText() => TryCommitPendingEdit();

    /// <summary>
    /// A change of more than this many rows (an insert, a removal) rebuilds <see cref="Rows"/> in one pass with a single reset, instead
    /// of one collection operation per row (which costs the number of changed rows times the number of rows). Settable for tests.
    /// </summary>
    internal int LargeChangeRows { get; set; } = DefaultLargeChangeRows;

    /// <summary>The limit of a paste in characters; settable so that tests need no twenty million characters.</summary>
    internal int MaxPasteChars { get; set; } = DefaultMaxPasteChars;

    /// <summary>The limit of a paste in rows (the same number as the copy limit); settable so that tests need no hundred thousand rows.</summary>
    internal int MaxPasteRows { get; set; } = DefaultMaxCopyRows;

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
        if (_disposed)
        {
            return;
        }

        Unsubscribe(_document);
        _document = document;
        Subscribe(document);
        _rows.ReplaceAll(BuildRows(document));
        InvalidateText();
        SetProperty(ref _header, HeaderText(), nameof(Header));
        SetNotice(null);
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(IsModified));
        RaiseReloadChanged();
        OnPropertyChanged(nameof(IsTableEntry));
        SelectedRows = [];
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

    protected override void OnDispose()
    {
        _disposed = true;
        Unsubscribe(_document);
    }

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
        // What the last copy or paste said is about the rows of that moment; the paste sets its notice after its own change.
        if (!_pasting)
        {
            SetNotice(null);
        }

        switch (change.Kind)
        {
            case DocumentChangeKind.CellsChanged:
                foreach (var index in change.RowIndexes)
                {
                    _rows[index].RaiseChanged();
                }

                break;
            case DocumentChangeKind.RowsInserted when change.RowIndexes.Count > LargeChangeRows:
            case DocumentChangeKind.RowsRemoved when change.RowIndexes.Count > LargeChangeRows:
                RebuildRows();
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

            // The selection may point at other rows now, and the row count decides whether a command has a row to act on.
            RaiseSelectionCommandsChanged();
        }

        InvalidateText();
    }

    // One pass for a large change: every row array that is still in the document keeps its EditRow (found by reference in O(n)),
    // the others get one, and the collection is replaced with a single reset.
    private void RebuildRows()
    {
        var known = new Dictionary<object?[], EditRow>(_rows.Count, ReferenceEqualityComparer.Instance);
        foreach (var row in _rows)
        {
            known[row.Cells] = row;
        }

        var rows = new List<EditRow>(_document.Rows.Count);
        foreach (var cells in _document.Rows)
        {
            rows.Add(known.TryGetValue(cells, out var existing) ? existing : new EditRow(cells, _document.Columns, this));
        }

        _rows.ReplaceAll(rows);
    }

    private void OnModifiedChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsModified));
        RaiseReloadChanged();
    }

    protected override void OnStaleChanged() => RaiseReloadChanged();

    private void RaiseReloadChanged()
    {
        OnPropertyChanged(nameof(CanReload));
        _reloadCommand.RaiseCanExecuteChanged();
    }

    private string HeaderText() => _document.Kind == DocumentKind.Table
        ? Texts.TabTableHeader(_document.TableName, _document.Version, _document.Rows.Count)
        : Texts.TabLocHeader(_document.Rows.Count);

    // ----- Commands -----

    protected override int RowCount => _document.Rows.Count;

    protected override CopySource? GetCopySource() => new(_document.Columns, index => _document.Rows[index]);

    protected override void OnSelectionChanged()
    {
        base.OnSelectionChanged();
        RaiseSelectionCommandsChanged();
    }

    private void RaiseSelectionCommandsChanged()
    {
        _deleteRowsCommand.RaiseCanExecuteChanged();
        _duplicateRowsCommand.RaiseCanExecuteChanged();
    }

    private void RequestSelection(IReadOnlyList<int> rows) => SelectionRequested?.Invoke(this, rows);

    // A row index after the last selected row, or the end of the rows.
    private int InsertionIndex(List<int> selected) => selected.Count > 0 ? selected[^1] + 1 : _document.Rows.Count;

    private void InsertRow()
    {
        var index = InsertionIndex(ValidSelection());
        _history.Execute(_document.InsertRows(index, [_document.NewRow()]));
        RequestSelection([index]);
    }

    private void DeleteRows()
    {
        var selected = ValidSelection();
        if (selected.Count == 0)
        {
            return;
        }

        _history.Execute(_document.DeleteRows(selected));

        // The row that took the place of the first deleted one, or the last row when the deleted ones were at the end.
        var left = _document.Rows.Count;
        RequestSelection(left == 0 ? [] : [Math.Min(selected[0], left - 1)]);
    }

    private void DuplicateRows()
    {
        var selected = ValidSelection();
        if (selected.Count == 0)
        {
            return;
        }

        _history.Execute(_document.DuplicateRows(selected));
        RequestSelection(Enumerable.Range(selected[^1] + 1, selected.Count).ToList());
    }

    private void PasteRows()
    {
        if (Clipboard is not { } clipboard)
        {
            return;
        }

        if (clipboard.GetText() is not { } text)
        {
            SetNotice(t => t.GridPasteNoText);
            return;
        }

        if (text.Length > MaxPasteChars)
        {
            var length = text.Length;
            var max = MaxPasteChars;
            SetNotice(t => t.GridPasteTooLarge(length, max));
            return;
        }

        // The paste runs on the UI thread, so the rows are counted before the text is parsed (counting is a plain scan).
        var rowCount = CountRows(text);
        if (rowCount > MaxPasteRows)
        {
            var max = MaxPasteRows;
            SetNotice(t => t.GridPasteTooManyRows(rowCount, max));
            return;
        }

        var result = ClipboardRows.Read(text, _document);
        if (result.Problem != PasteProblem.None)
        {
            var problem = result.Problem;
            var line = result.Line;
            var detail = DisplayText.Safe(result.Detail ?? string.Empty);
            SetNotice(t => line > 0
                ? t.GridPasteRefusedAtLine(line, t.GridPasteProblemText(problem), detail)
                : t.GridPasteRefused(t.GridPasteProblemText(problem), detail));
            return;
        }

        var index = InsertionIndex(ValidSelection());
        var edit = _document.InsertRows(index, result.Rows);

        // The change of the document must not clear the notice that is set right after it.
        _pasting = true;
        try
        {
            _history.Execute(edit);
        }
        finally
        {
            _pasting = false;
        }

        SetNotice(PasteNotice(result));
        RequestSelection(Enumerable.Range(index, result.Rows.Count).ToList());
    }

    // The lines of the text after the first one (the header), split at the line feeds the way the reader splits them; a last line without a
    // line feed counts, the empty rest after the final line feed does not.
    private static int CountRows(string text)
    {
        var span = text.AsSpan();
        var lines = span.Count('\n') + (span.Length > 0 && span[^1] == '\n' ? 0 : 1);
        return Math.Max(0, lines - 1);
    }

    // The columns that the text lacked (they got their default) and the ones that it had too many (they were left out); null when both are empty.
    private static Func<AppTexts, string>? PasteNotice(PasteResult result)
    {
        if (result.MissingColumns.Count == 0 && result.ExtraColumns.Count == 0)
        {
            return null;
        }

        var missing = result.MissingColumns;
        var extra = result.ExtraColumns;
        return t =>
        {
            var parts = new List<string>(2);
            if (missing.Count > 0)
            {
                parts.Add(t.GridPasteMissingColumns(Names(missing, t)));
            }

            if (extra.Count > 0)
            {
                parts.Add(t.GridPasteExtraColumns(Names(extra, t)));
            }

            return string.Join(" ", parts);
        };
    }

    private static string Names(IReadOnlyList<string> names, AppTexts texts) =>
        DisplayText.Safe(string.Join(", ", names), 300, " (" + texts.TextShortened + ")");

    // ----- Messages -----

    private string MessageFor(int column, CellCheck check)
    {
        var field = _document.Columns[column];
        var expected = check.Problem == CellProblem.ColourChannelOutOfRange || _document.IsColourChannelColumn(column)
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
