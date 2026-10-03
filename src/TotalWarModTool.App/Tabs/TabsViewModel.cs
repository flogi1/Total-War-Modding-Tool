using System.Collections.ObjectModel;
using System.Windows.Input;
using TotalWarModTool.Schema;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>The open tabs and the selected one. Pack and entry paths are compared ignoring case.</summary>
public sealed class TabsViewModel : ObservableObject
{
    private readonly RelayCommand _closeSelectedCommand;
    private TabViewModel? _selectedTab;

    public TabsViewModel(AppTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        Texts = texts;
        _closeSelectedCommand = new RelayCommand(() =>
        {
            if (SelectedTab is { } tab)
            {
                Close(tab);
            }
        }, () => SelectedTab is not null);
        Tabs.CollectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public AppTexts Texts { get; }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public TabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                _closeSelectedCommand.RaiseCanExecuteChanged();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public ICommand CloseSelectedCommand => _closeSelectedCommand;

    /// <summary>Raised when tabs were added or removed and when the selection changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Selects the tab of this entry. False when no such tab is open.</summary>
    public bool TryActivate(string packPath, string entryPath)
    {
        var tab = Tabs.FirstOrDefault(t => Matches(t, packPath, entryPath));
        if (tab is null)
        {
            return false;
        }

        SelectedTab = tab;
        return true;
    }

    /// <summary>Appends the tab for an opened entry. It is selected when <paramref name="select"/> is set or nothing is selected yet.</summary>
    public void Add(PackSource pack, OpenedEntry entry, ResolvedSchema? schema, bool select = true)
    {
        var tab = TabFactory.Create(pack, entry, schema, Texts, Close);
        Tabs.Add(tab);
        if (select || SelectedTab is null)
        {
            SelectedTab = tab;
        }
    }

    /// <summary>Closes the tab. When it is the selected one, its right neighbour is selected, else its left neighbour, else nothing.</summary>
    public void Close(TabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        if (ReferenceEquals(SelectedTab, tab))
        {
            // Selected before the removal, so the tab control never has to pick a tab on its own.
            SelectedTab = index + 1 < Tabs.Count ? Tabs[index + 1] : index > 0 ? Tabs[index - 1] : null;
        }

        Tabs.RemoveAt(index);
    }

    /// <summary>Marks every tab of the pack: the pack changed on disk, the tabs show old content.</summary>
    public void MarkStale(string packPath)
    {
        foreach (var tab in Tabs.Where(t => string.Equals(t.PackPath, packPath, StringComparison.OrdinalIgnoreCase)))
        {
            tab.MarkStale();
        }
    }

    private static bool Matches(TabViewModel tab, string packPath, string entryPath) =>
        string.Equals(tab.PackPath, packPath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(tab.EntryPath, entryPath, StringComparison.OrdinalIgnoreCase);
}
