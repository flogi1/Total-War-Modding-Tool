using System.Windows.Input;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>One open entry. Created by <see cref="TabFactory"/>; <see cref="CloseCommand"/> hands the tab to the owner of the tab list.</summary>
public abstract class TabViewModel : ObservableObject
{
    private bool _isStale;

    protected TabViewModel(PackSource pack, string entryPath, Action<TabViewModel> close)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(entryPath);
        ArgumentNullException.ThrowIfNull(close);
        PackPath = pack.Path;
        EntryPath = entryPath;
        Title = entryPath[(entryPath.LastIndexOf('/') + 1)..];
        ToolTip = $"{pack.Name}: {entryPath}";
        CloseCommand = new RelayCommand(() => close(this));
    }

    /// <summary><see cref="PackSource.Path"/> of the pack the entry lives in.</summary>
    public string PackPath { get; }

    public string EntryPath { get; }

    /// <summary>The last segment of <see cref="EntryPath"/>.</summary>
    public string Title { get; }

    /// <summary>"&lt;pack name&gt;: &lt;entry path&gt;"; both are data, not UI texts.</summary>
    public string ToolTip { get; }

    /// <summary>True when the pack changed on disk after the tab was opened; the tab shows the old content.</summary>
    public bool IsStale
    {
        get => _isStale;
        private set => SetProperty(ref _isStale, value);
    }

    public ICommand CloseCommand { get; }

    internal void MarkStale() => IsStale = true;
}

/// <summary>A decoded DB table. <see cref="Rows"/> hold boxed cell values in the order of <see cref="Columns"/>.</summary>
public sealed class TableTabViewModel : TabViewModel
{
    public TableTabViewModel(PackSource pack, string entryPath, Action<TabViewModel> close, string header, IReadOnlyList<string> columns,
        IReadOnlyList<object?[]> rows)
        : base(pack, entryPath, close)
    {
        Header = header;
        Columns = columns;
        Rows = rows;
    }

    public string Header { get; }

    /// <summary>Field names in schema order.</summary>
    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyList<object?[]> Rows { get; }
}

public sealed class LocTabViewModel : TabViewModel
{
    public LocTabViewModel(PackSource pack, string entryPath, Action<TabViewModel> close, string header, IReadOnlyList<LocRow> rows)
        : base(pack, entryPath, close)
    {
        Header = header;
        Rows = rows;
    }

    public string Header { get; }

    public IReadOnlyList<LocRow> Rows { get; }
}

public sealed class TextTabViewModel : TabViewModel
{
    public TextTabViewModel(PackSource pack, string entryPath, Action<TabViewModel> close, string text, string header, string? notice)
        : base(pack, entryPath, close)
    {
        Text = text;
        Header = header;
        Notice = notice;
    }

    public string Text { get; }

    /// <summary>Encoding and size.</summary>
    public string Header { get; }

    /// <summary>Set only when the text is cut at the size limit.</summary>
    public string? Notice { get; }
}

public sealed record InfoLine(string Label, string Value);

/// <summary>A hint that names the reason why an entry cannot be shown, with what is known about it.</summary>
public sealed class InfoTabViewModel : TabViewModel
{
    public InfoTabViewModel(PackSource pack, string entryPath, Action<TabViewModel> close, string heading, string reason, IReadOnlyList<InfoLine> lines)
        : base(pack, entryPath, close)
    {
        Heading = heading;
        Reason = reason;
        Lines = lines;
    }

    public string Heading { get; }

    /// <summary>Empty when there is nothing more to say than the heading.</summary>
    public string Reason { get; }

    public IReadOnlyList<InfoLine> Lines { get; }
}
