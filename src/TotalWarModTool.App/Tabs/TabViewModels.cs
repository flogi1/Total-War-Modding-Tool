using System.ComponentModel;
using System.Windows.Input;
using TotalWarModTool.Editing;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>What happened to the file of a project tab outside the tool, after the tab read it.</summary>
internal enum OutsideChange
{
    None,

    /// <summary>Another program wrote the file.</summary>
    Changed,

    /// <summary>The file is gone.</summary>
    Deleted,
}

/// <summary>
/// One open entry. Created by <see cref="TabFactory"/>; <see cref="CloseCommand"/> hands the tab to the owner of the tab list.
/// Texts that come from <see cref="AppTexts"/> are computed again when the language changes (<see cref="RefreshTexts"/>); data stays as it is.
/// A tab listens to the texts until it is disposed, which the owner of the tab list does when it closes the tab.
/// </summary>
public abstract class TabViewModel : ObservableObject, IDisposable
{
    private readonly AppTexts _texts;

    // The marks that are set. What is shown is the strongest of them (_stale); clearing the outside marks brings back a schema mark under them.
    private bool _schemaStale;
    private bool _packStale;
    private OutsideChange _outside;
    private StaleKind _stale;
    private string _staleText = string.Empty;
    private bool _disposed;

    // In the order of strength: a later one wins over every earlier one.
    private enum StaleKind { None, Schema, Pack, ChangedOutside, DeletedOutside }

    protected TabViewModel(TabSource source, string entryPath, AppTexts texts, Action<TabViewModel> close)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entryPath);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(close);
        _texts = texts;
        PackPath = source.Key;
        EntryPath = entryPath;
        Title = entryPath[(entryPath.LastIndexOf('/') + 1)..];
        ToolTip = $"{source.Name}: {entryPath}";
        CloseCommand = new RelayCommand(() => close(this));
        _texts.PropertyChanged += OnTextsChanged;
    }

    /// <summary>The texts of the app; the views of a tab read the texts of their menus from here, so they follow a change of the language.</summary>
    public AppTexts Texts => _texts;

    /// <summary><see cref="TabSource.Key"/> of the source the entry lives in: the path of a pack, or the key of a project.</summary>
    public string PackPath { get; }

    public string EntryPath { get; }

    /// <summary>The last segment of <see cref="EntryPath"/>.</summary>
    public string Title { get; }

    /// <summary>"&lt;source name&gt;: &lt;entry path&gt;"; both are data, not UI texts.</summary>
    public string ToolTip { get; }

    /// <summary>
    /// True when the pack changed on disk, the schema changed or the file of a project tab was changed or deleted outside after the tab
    /// was opened; the tab shows the old content.
    /// </summary>
    public bool IsStale => _stale != StaleKind.None;

    /// <summary>True when the file of a project tab was changed or deleted outside (not the pack or the schema); see <see cref="SetOutsideChange"/>.</summary>
    public bool IsStaleOutside => _outside != OutsideChange.None;

    /// <summary>The tooltip of the stale mark in the current language: what changed. Empty while the tab is not stale.</summary>
    public string StaleText => _staleText;

    public ICommand CloseCommand { get; }

    /// <summary>True when the tab has changes that are not saved. Only an editable tab can have any; the header shows a mark.</summary>
    public virtual bool IsModified => false;

    /// <summary>Stops listening to the texts, then calls <see cref="OnDispose"/>. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _texts.PropertyChanged -= OnTextsChanged;
            OnDispose();
        }
    }

    /// <summary>Called once, by the first <see cref="Dispose"/>: a tab that listens to something else stops listening here.</summary>
    protected virtual void OnDispose()
    {
    }

    /// <summary>True when the entry is a table (a table tab or the hint tab of a table): a change of the schema affects what it shows.</summary>
    internal virtual bool IsTableEntry => false;

    /// <summary>The pack changed. This wins over a change of the schema.</summary>
    internal void MarkStale()
    {
        _packStale = true;
        UpdateStale();
    }

    /// <summary>The schema changed. A mark of another kind that is already there stays what is shown, and the schema mark stays under it.</summary>
    internal void MarkSchemaStale()
    {
        _schemaStale = true;
        UpdateStale();
    }

    /// <summary>
    /// Sets what happened to the file of a project tab outside: it was changed, it was deleted, or nothing (<see cref="OutsideChange.None"/>
    /// clears both, which a reload or a successful save does). The new value replaces the old one; deleted wins over changed, both win over
    /// the schema mark, and clearing them leaves the schema mark.
    /// </summary>
    internal void SetOutsideChange(OutsideChange change)
    {
        _outside = change;
        UpdateStale();
    }

    /// <summary>Called after what <see cref="IsStale"/> and <see cref="IsStaleOutside"/> report has changed.</summary>
    protected virtual void OnStaleChanged()
    {
    }

    private void UpdateStale()
    {
        var kind = _outside switch
        {
            OutsideChange.Deleted => StaleKind.DeletedOutside,
            OutsideChange.Changed => StaleKind.ChangedOutside,
            _ => _packStale ? StaleKind.Pack : _schemaStale ? StaleKind.Schema : StaleKind.None,
        };
        if (kind == _stale)
        {
            return;
        }

        var wasStale = IsStale;
        _stale = kind;
        SetProperty(ref _staleText, StaleTextFor(kind), nameof(StaleText));
        if (wasStale != IsStale)
        {
            OnPropertyChanged(nameof(IsStale));
        }

        OnPropertyChanged(nameof(IsStaleOutside));
        OnStaleChanged();
    }

    private string StaleTextFor(StaleKind kind) => kind switch
    {
        StaleKind.Pack => _texts.TabStale,
        StaleKind.Schema => _texts.TabStaleSchema,
        StaleKind.ChangedOutside => _texts.TabStaleChanged,
        StaleKind.DeletedOutside => _texts.TabStaleDeleted,
        _ => string.Empty,
    };

    /// <summary>The title, also what screen readers and UI automation call the tab.</summary>
    public override string ToString() => Title;

    /// <summary>Computes every text property again in the language that is active now and raises <c>PropertyChanged</c> for those that changed.</summary>
    protected abstract void RefreshTexts();

    private void OnTextsChanged(object? sender, PropertyChangedEventArgs e)
    {
        SetProperty(ref _staleText, StaleTextFor(_stale), nameof(StaleText));
        RefreshTexts();
    }
}

/// <summary>
/// A tab with rows that can be shown as a grid or, read-only, as the TSV text of the table (see <see cref="TableTsv"/>). The text is built
/// on the first switch to the text view and kept (until <see cref="InvalidateText"/>); it is cut at a line end when it is longer than
/// <see cref="MaxTextChars"/>.
/// </summary>
public abstract class RowsTabViewModel : TabViewModel
{
    /// <summary>The longest text the text view shows, in characters.</summary>
    public const int MaxTextChars = 5_000_000;

    /// <summary>The most rows that one copy puts on the clipboard.</summary>
    public const int DefaultMaxCopyRows = 100_000;

    private readonly IClipboard? _clipboard;
    private readonly RelayCommand _copyRowsCommand;
    private bool _showText;
    private bool _textBuilt;
    private string? _text;
    private string? _textNotice;
    private Func<AppTexts, string>? _notice;
    private IReadOnlyList<int> _selectedRows = [];
    private Func<AppTexts, string>? _gridNoticeText;
    private string? _gridNotice;

    /// <param name="clipboard">Where copy puts the rows; null disables copying (and pasting, where a tab has it).</param>
    protected RowsTabViewModel(TabSource source, string entryPath, AppTexts texts, Action<TabViewModel> close, IClipboard? clipboard = null)
        : base(source, entryPath, texts, close)
    {
        _clipboard = clipboard;
        _copyRowsCommand = new RelayCommand(CopyRows, CanCopyRows);
    }

    /// <summary>The limit of the text; settable so that tests need no five million characters.</summary>
    internal int MaxChars { get; set; } = MaxTextChars;

    /// <summary>The limit of rows for one copy; settable so that tests need no hundred thousand rows.</summary>
    internal int MaxCopyRows { get; set; } = DefaultMaxCopyRows;

    /// <summary>
    /// The rows the user selected, as indexes into the rows of the tab (not into the sorted view); set by the view. Indexes outside the
    /// rows are ignored by the commands.
    /// </summary>
    public IReadOnlyList<int> SelectedRows
    {
        get => _selectedRows;
        set
        {
            _selectedRows = value ?? [];
            OnPropertyChanged();
            OnSelectionChanged();
        }
    }

    /// <summary>Copies the selected rows to the clipboard (a header line with the column names, then one line per row). Disabled without a selection or a clipboard.</summary>
    public ICommand CopyRowsCommand => _copyRowsCommand;

    /// <summary>
    /// What the last copy or paste has to say (a refusal, the columns that a paste filled or dropped); null when there is nothing. The
    /// view shows it in a line under the header. Computed again when the language changes.
    /// </summary>
    public string? Notice => _gridNotice;

    /// <summary>False shows the grid, true the text. Switching to the text for the first time builds it.</summary>
    public bool ShowText
    {
        get => _showText;
        set
        {
            if (value && !_showText && !CanShowText())
            {
                // Refused: the switch that has been clicked goes back.
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowGrid));
                return;
            }

            if (!SetProperty(ref _showText, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowGrid));
            if (value && !_textBuilt)
            {
                BuildText();
            }
        }
    }

    /// <summary>Asked before the switch to the text view; false refuses it. An editable tab commits the cell that is being edited first.</summary>
    protected virtual bool CanShowText() => true;

    public bool ShowGrid => !_showText;

    /// <summary>The TSV form of the table; null before the first switch to the text view and when the table cannot be written as text.</summary>
    public string? Text => _text;

    /// <summary>Why there is no text, or that the text is cut. Null when there is nothing to say.</summary>
    public string? TextNotice => _textNotice;

    /// <summary>
    /// The TSV form of the content; <c>Text</c> is null when the content cannot be written without loss. Null when there is no definition
    /// to write the content with. May throw <see cref="ArgumentException"/>.
    /// </summary>
    protected abstract TsvWriteResult? WriteText();

    /// <summary>Computes the header again in the language that is active now.</summary>
    protected abstract void RefreshHeader();

    protected sealed override void RefreshTexts()
    {
        RefreshHeader();
        if (_notice is not null)
        {
            SetProperty(ref _textNotice, _notice(Texts), nameof(TextNotice));
        }

        if (_gridNoticeText is not null)
        {
            SetProperty(ref _gridNotice, _gridNoticeText(Texts), nameof(Notice));
        }

        RefreshMoreTexts();
    }

    /// <summary>Called after the texts of the base were computed again; a tab with texts of its own computes them here.</summary>
    protected virtual void RefreshMoreTexts()
    {
    }

    /// <summary>The number of rows of the tab; indexes from 0 to this count minus one are rows.</summary>
    protected abstract int RowCount { get; }

    /// <summary>What copy writes: the columns and the cells of a row by its index. Null when the tab has no definition to write the cells with.</summary>
    protected abstract CopySource? GetCopySource();

    /// <summary>The columns and the rows of a copy. <paramref name="RowAt"/> gives the cell array of a row by its index.</summary>
    protected sealed record CopySource(IReadOnlyList<FieldDefinition> Columns, Func<int, object?[]> RowAt);

    /// <summary>The clipboard of the tab; null when it has none.</summary>
    protected IClipboard? Clipboard => _clipboard;

    /// <summary>Sets <see cref="Notice"/> from a text of the app; the text is computed again when the language changes. Null clears it.</summary>
    protected void SetNotice(Func<AppTexts, string>? notice)
    {
        _gridNoticeText = notice;
        SetProperty(ref _gridNotice, notice?.Invoke(Texts), nameof(Notice));
    }

    /// <summary>Called after <see cref="SelectedRows"/> was set; commands that depend on the selection ask again here.</summary>
    protected virtual void OnSelectionChanged() => _copyRowsCommand.RaiseCanExecuteChanged();

    /// <summary>True when a selected index is a row.</summary>
    protected bool HasSelection() => _selectedRows.Any(IsRowIndex);

    protected bool IsRowIndex(int index) => index >= 0 && index < RowCount;

    /// <summary>The selected rows that exist, ascending and without repeats. The selection is set by the view and can be stale after a change.</summary>
    protected List<int> ValidSelection() => _selectedRows.Where(IsRowIndex).Distinct().Order().ToList();

    private bool CanCopyRows() => _clipboard is not null && HasSelection();

    private void CopyRows()
    {
        if (_clipboard is null || GetCopySource() is not { } source)
        {
            return;
        }

        var selected = ValidSelection();
        if (selected.Count == 0)
        {
            return;
        }

        if (selected.Count > MaxCopyRows)
        {
            var count = selected.Count;
            var max = MaxCopyRows;
            SetNotice(t => t.GridCopyTooManyRows(count, max));
            return;
        }

        TsvWriteResult result;
        try
        {
            result = ClipboardRows.Write(source.Columns, selected.Select(source.RowAt));
        }
        catch (ArgumentException ex)
        {
            // A cell of the wrong type or a row of another length (a table that could not be edited): the same case as a refusal.
            result = new TsvWriteResult(null, MessageWithoutParameter(ex));
        }

        if (result.Text is null)
        {
            var detail = DisplayText.Safe(result.Detail ?? string.Empty);
            SetNotice(t => t.GridCopyRefused(detail));
        }
        else if (!_clipboard.SetText(result.Text))
        {
            SetNotice(t => t.GridCopyClipboardFailed);
        }
        else
        {
            SetNotice(null);
        }
    }

    /// <summary>
    /// The content changed: the text built so far is out of date and is dropped. It is built again on the next switch to the text view.
    /// When the text view is showing, the tab goes back to the grid, where the change is visible; the text is never built here, so
    /// this is cheap and cannot throw (it runs inside the change events of the document).
    /// </summary>
    protected void InvalidateText()
    {
        _textBuilt = false;
        var hadText = _text is not null || _textNotice is not null;
        _text = null;
        _notice = null;
        _textNotice = null;
        ShowText = false;
        if (hadText)
        {
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(TextNotice));
        }
    }

    private void BuildText()
    {
        _textBuilt = true;
        TsvWriteResult? result;
        try
        {
            result = WriteText();
        }
        catch (ArgumentException ex)
        {
            // A table name or a path the dialect cannot carry (a tab, a line break, ';'): the same case as a refusal.
            result = new TsvWriteResult(null, MessageWithoutParameter(ex));
        }

        if (result is null)
        {
            _notice = t => t.TabTextViewUnavailable(t.TabTextViewNoDefinition);
        }
        else if (result.Text is null)
        {
            var detail = result.Detail ?? string.Empty;
            _notice = t => t.TabTextViewUnavailable(detail);
        }
        else
        {
            _text = CutAtLineEnd(result.Text, MaxChars, out var cut);
            if (cut)
            {
                _notice = t => t.TabTextViewTruncated;
            }
        }

        _textNotice = _notice?.Invoke(Texts);
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(TextNotice));
    }

    // .NET appends " (Parameter 'name')" in the language of the system. The suffix is found by the parameter name, not by its words:
    // the message is cut at the last " (" when what follows ends in 'name'). Without that pattern the whole message is kept.
    private static string MessageWithoutParameter(ArgumentException ex)
    {
        var message = ex.Message;
        if (ex.ParamName is { } name && message.EndsWith($"'{name}')", StringComparison.Ordinal))
        {
            var start = message.LastIndexOf(" (", StringComparison.Ordinal);
            if (start > 0)
            {
                message = message[..start];
            }
        }

        return message.ReplaceLineEndings(" ");
    }

    // Keeps whole lines: the text ends at the last line end that fits into the limit.
    private static string CutAtLineEnd(string text, int limit, out bool cut)
    {
        cut = text.Length > Math.Max(limit, 0);
        if (!cut)
        {
            return text;
        }

        var end = limit <= 0 ? -1 : text.LastIndexOf('\n', limit - 1);
        return end < 0 ? string.Empty : text[..(end + 1)];
    }
}

/// <summary>A decoded DB table. <see cref="Rows"/> hold boxed cell values in the order of <see cref="Columns"/>.</summary>
public sealed class TableTabViewModel : RowsTabViewModel
{
    private readonly DecodedTable _table;
    private string _header;

    public TableTabViewModel(TabSource source, string entryPath, AppTexts texts, Action<TabViewModel> close, DecodedTable table,
        IClipboard? clipboard = null)
        : base(source, entryPath, texts, close, clipboard)
    {
        _table = table;
        _header = HeaderText();
        Columns = table.Definition?.Fields.Select(f => f.Name).ToList() ?? [];
    }

    public string Header => _header;

    /// <summary>Field names in schema order.</summary>
    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyList<object?[]> Rows => _table.Rows;

    internal override bool IsTableEntry => true;

    protected override void RefreshHeader() => SetProperty(ref _header, HeaderText(), nameof(Header));

    protected override int RowCount => _table.Rows.Count;

    protected override CopySource? GetCopySource() => _table.Definition is { } definition
        ? new CopySource(definition.Fields, index => _table.Rows[index])
        : null;

    // The meta line of the TSV names the entry the table was read from.
    protected override TsvWriteResult? WriteText() => _table.Definition is { } definition
        ? TableTsv.Write(_table.TableName, _table.Version, EntryPath, definition, _table.Rows)
        : null;

    private string HeaderText() => Texts.TabTableHeader(_table.TableName, _table.Version, _table.Rows.Count);
}

public sealed class LocTabViewModel : RowsTabViewModel
{
    private string _header;

    public LocTabViewModel(TabSource source, string entryPath, AppTexts texts, Action<TabViewModel> close, IReadOnlyList<LocRow> rows,
        IClipboard? clipboard = null)
        : base(source, entryPath, texts, close, clipboard)
    {
        Rows = rows;
        _header = HeaderText();
    }

    public string Header => _header;

    public IReadOnlyList<LocRow> Rows { get; }

    protected override void RefreshHeader() => SetProperty(ref _header, HeaderText(), nameof(Header));

    protected override int RowCount => Rows.Count;

    // The three cells of a loc row, in the columns of a loc document (key, text, tooltip).
    protected override CopySource? GetCopySource() => new(RowsDocument.LocColumns, index =>
    {
        var row = Rows[index];
        return [row.Key, row.Text, row.Tooltip];
    });

    protected override TsvWriteResult? WriteText() => TableTsv.WriteLoc(EntryPath, Rows);

    private string HeaderText() => Texts.TabLocHeader(Rows.Count);
}

public sealed class TextTabViewModel : TabViewModel
{
    private readonly TextContent _content;
    private string _header;
    private string? _notice;

    public TextTabViewModel(TabSource source, string entryPath, AppTexts texts, Action<TabViewModel> close, TextContent content)
        : base(source, entryPath, texts, close)
    {
        _content = content;
        _header = HeaderText();
        _notice = NoticeText();
    }

    public string Text => _content.Text;

    /// <summary>Encoding and size.</summary>
    public string Header => _header;

    /// <summary>Set only when the text is cut at the size limit.</summary>
    public string? Notice => _notice;

    protected override void RefreshTexts()
    {
        SetProperty(ref _header, HeaderText(), nameof(Header));
        SetProperty(ref _notice, NoticeText(), nameof(Notice));
    }

    private string HeaderText() => Texts.TabTextHeader(_content.EncodingName, TabFactory.FormatSize(_content.TotalBytes));

    private string? NoticeText() => _content.Truncated
        ? Texts.TabTextTruncated(TabFactory.FormatSize(EntryText.MaxBytes), TabFactory.FormatSize(_content.TotalBytes))
        : null;
}

public sealed record InfoLine(string Label, string Value);

/// <summary>A hint that names the reason why an entry cannot be shown, with what is known about it.</summary>
public sealed class InfoTabViewModel : TabViewModel
{
    private readonly OpenedEntry _entry;
    private readonly ResolvedSchema? _schema;
    private readonly Func<AppTexts, string> _heading;
    private readonly Func<AppTexts, string> _reason;
    private string _headingText;
    private string _reasonText;
    private IReadOnlyList<InfoLine> _lines;

    /// <param name="heading">The heading in the language of the given texts.</param>
    /// <param name="reason">The reason in the language of the given texts; empty when there is nothing more to say than the heading.</param>
    public InfoTabViewModel(TabSource source, OpenedEntry entry, ResolvedSchema? schema, AppTexts texts, Action<TabViewModel> close,
        Func<AppTexts, string> heading, Func<AppTexts, string> reason)
        : base(source, entry.EntryPath, texts, close)
    {
        _entry = entry;
        _schema = schema;
        _heading = heading;
        _reason = reason;
        _headingText = heading(texts);
        _reasonText = reason(texts);
        _lines = TabFactory.InfoLines(entry, schema, texts);
    }

    public string Heading => _headingText;

    /// <summary>Empty when there is nothing more to say than the heading.</summary>
    public string Reason => _reasonText;

    public IReadOnlyList<InfoLine> Lines => _lines;

    internal override bool IsTableEntry => _entry.Kind == EntryKind.Table;

    protected override void RefreshTexts()
    {
        SetProperty(ref _headingText, _heading(Texts), nameof(Heading));
        SetProperty(ref _reasonText, _reason(Texts), nameof(Reason));
        var lines = TabFactory.InfoLines(_entry, _schema, Texts);
        if (!lines.SequenceEqual(_lines))
        {
            _lines = lines;
            OnPropertyChanged(nameof(Lines));
        }
    }
}
