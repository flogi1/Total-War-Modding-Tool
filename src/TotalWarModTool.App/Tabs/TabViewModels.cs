using System.ComponentModel;
using System.Windows.Input;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// One open entry. Created by <see cref="TabFactory"/>; <see cref="CloseCommand"/> hands the tab to the owner of the tab list.
/// Texts that come from <see cref="AppTexts"/> are computed again when the language changes (<see cref="RefreshTexts"/>); data stays as it is.
/// A tab listens to the texts until it is disposed, which the owner of the tab list does when it closes the tab.
/// </summary>
public abstract class TabViewModel : ObservableObject, IDisposable
{
    private readonly AppTexts _texts;
    private StaleKind _stale;
    private string _staleText = string.Empty;
    private bool _disposed;

    private enum StaleKind { None, Schema, Pack }

    protected TabViewModel(PackSource pack, string entryPath, AppTexts texts, Action<TabViewModel> close)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(entryPath);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(close);
        _texts = texts;
        PackPath = pack.Path;
        EntryPath = entryPath;
        Title = entryPath[(entryPath.LastIndexOf('/') + 1)..];
        ToolTip = $"{pack.Name}: {entryPath}";
        CloseCommand = new RelayCommand(() => close(this));
        _texts.PropertyChanged += OnTextsChanged;
    }

    protected AppTexts Texts => _texts;

    /// <summary><see cref="PackSource.Path"/> of the pack the entry lives in.</summary>
    public string PackPath { get; }

    public string EntryPath { get; }

    /// <summary>The last segment of <see cref="EntryPath"/>.</summary>
    public string Title { get; }

    /// <summary>"&lt;pack name&gt;: &lt;entry path&gt;"; both are data, not UI texts.</summary>
    public string ToolTip { get; }

    /// <summary>True when the pack changed on disk or the schema changed after the tab was opened; the tab shows the old content.</summary>
    public bool IsStale => _stale != StaleKind.None;

    /// <summary>The tooltip of the stale mark in the current language: what changed. Empty while the tab is not stale.</summary>
    public string StaleText => _staleText;

    public ICommand CloseCommand { get; }

    /// <summary>Stops listening to the texts. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _texts.PropertyChanged -= OnTextsChanged;
        }
    }

    /// <summary>True when the entry is a table (a table tab or the hint tab of a table): a change of the schema affects what it shows.</summary>
    internal virtual bool IsTableEntry => false;

    /// <summary>The pack changed. This wins over a change of the schema.</summary>
    internal void MarkStale() => SetStale(StaleKind.Pack);

    /// <summary>The schema changed. Does not replace a mark that is already there.</summary>
    internal void MarkSchemaStale()
    {
        if (_stale == StaleKind.None)
        {
            SetStale(StaleKind.Schema);
        }
    }

    private void SetStale(StaleKind kind)
    {
        if (_stale == kind)
        {
            return;
        }

        var wasStale = IsStale;
        _stale = kind;
        SetProperty(ref _staleText, StaleTextFor(kind), nameof(StaleText));
        if (!wasStale)
        {
            OnPropertyChanged(nameof(IsStale));
        }
    }

    private string StaleTextFor(StaleKind kind) => kind switch
    {
        StaleKind.Pack => _texts.TabStale,
        StaleKind.Schema => _texts.TabStaleSchema,
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

/// <summary>A decoded DB table. <see cref="Rows"/> hold boxed cell values in the order of <see cref="Columns"/>.</summary>
public sealed class TableTabViewModel : TabViewModel
{
    private readonly DecodedTable _table;
    private string _header;

    public TableTabViewModel(PackSource pack, string entryPath, AppTexts texts, Action<TabViewModel> close, DecodedTable table)
        : base(pack, entryPath, texts, close)
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

    protected override void RefreshTexts() => SetProperty(ref _header, HeaderText(), nameof(Header));

    private string HeaderText() => Texts.TabTableHeader(_table.TableName, _table.Version, _table.Rows.Count);
}

public sealed class LocTabViewModel : TabViewModel
{
    private string _header;

    public LocTabViewModel(PackSource pack, string entryPath, AppTexts texts, Action<TabViewModel> close, IReadOnlyList<LocRow> rows)
        : base(pack, entryPath, texts, close)
    {
        Rows = rows;
        _header = HeaderText();
    }

    public string Header => _header;

    public IReadOnlyList<LocRow> Rows { get; }

    protected override void RefreshTexts() => SetProperty(ref _header, HeaderText(), nameof(Header));

    private string HeaderText() => Texts.TabLocHeader(Rows.Count);
}

public sealed class TextTabViewModel : TabViewModel
{
    private readonly TextContent _content;
    private string _header;
    private string? _notice;

    public TextTabViewModel(PackSource pack, string entryPath, AppTexts texts, Action<TabViewModel> close, TextContent content)
        : base(pack, entryPath, texts, close)
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
    public InfoTabViewModel(PackSource pack, OpenedEntry entry, ResolvedSchema? schema, AppTexts texts, Action<TabViewModel> close,
        Func<AppTexts, string> heading, Func<AppTexts, string> reason)
        : base(pack, entry.EntryPath, texts, close)
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
