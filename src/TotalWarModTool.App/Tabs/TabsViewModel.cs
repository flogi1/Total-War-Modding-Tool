using System.Collections.ObjectModel;
using System.Windows.Input;
using TotalWarModTool.Schema;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>The open tabs and the selected one. Source keys and entry paths are compared ignoring case.</summary>
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

    /// <summary>
    /// Asked first by <see cref="Add"/>: returns the tab for an entry that gets a special tab (an editable project file), or null for the
    /// default. It gets the same close callback the factory would get.
    /// </summary>
    public Func<TabSource, OpenedEntry, Action<TabViewModel>, TabViewModel?>? SpecialTab { get; set; }

    /// <summary>
    /// The clipboard that the tabs copy to and paste from; set by the composition root. The tabs made after it is set get it (the
    /// controller of the editable tabs reads it when it makes one). Without it the copy and paste commands of a tab are disabled.
    /// </summary>
    public IClipboard? Clipboard { get; set; }

    /// <summary>Asked before a tab is closed by its command, by Ctrl+W or by the middle button; false keeps it open.</summary>
    public Func<TabViewModel, bool>? CanClose { get; set; }

    /// <summary>Raised when tabs were added or removed and when the selection changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Selects the tab of this entry. <paramref name="packPath"/> is a <see cref="TabSource.Key"/>. False when no such tab is open.</summary>
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
    public void Add(TabSource source, OpenedEntry entry, ResolvedSchema? schema, bool select = true)
    {
        var tab = SpecialTab?.Invoke(source, entry, Close) ?? TabFactory.Create(source, entry, schema, Texts, Close, Clipboard);
        Tabs.Add(tab);
        if (select || SelectedTab is null)
        {
            SelectedTab = tab;
        }
    }

    /// <summary>
    /// Replaces an open tab by the tab that <see cref="Add"/> would build for the entry, at the same place. The new tab is selected when the
    /// old one was; the old one is disposed without asking. Used when a reload turns a tab into a different kind (an editable table that can
    /// no longer be read becomes a hint). False when the tab is not open.
    /// </summary>
    public bool Replace(TabViewModel old, TabSource source, OpenedEntry entry, ResolvedSchema? schema)
    {
        ArgumentNullException.ThrowIfNull(old);
        var index = Tabs.IndexOf(old);
        if (index < 0)
        {
            return false;
        }

        var tab = SpecialTab?.Invoke(source, entry, Close) ?? TabFactory.Create(source, entry, schema, Texts, Close, Clipboard);
        var wasSelected = ReferenceEquals(SelectedTab, old);

        // The new tab is in the list before the old one leaves, and it is selected before that, so the tab control never has to pick one.
        Tabs.Insert(index, tab);
        if (wasSelected)
        {
            SelectedTab = tab;
        }

        Tabs.RemoveAt(index + 1);
        old.Dispose();
        return true;
    }

    /// <summary>
    /// Closes the tab after <see cref="CanClose"/> agreed (the variant the close commands use). A tab that is not open is ignored without asking.
    /// </summary>
    public void Close(TabViewModel tab)
    {
        if (!Tabs.Contains(tab) || CanClose?.Invoke(tab) == false)
        {
            return;
        }

        CloseWithoutAsking(tab);
    }

    /// <summary>
    /// Closes the tab without asking: the app decided it (the project left the list, a reload replaced the tab). When it is the selected
    /// one, its right neighbour is selected, else its left neighbour, else nothing.
    /// </summary>
    public void CloseWithoutAsking(TabViewModel tab)
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
        tab.Dispose();
    }

    /// <summary>Marks every tab of the pack: the pack changed on disk, the tabs show old content. <paramref name="packPath"/> is a <see cref="TabSource.Key"/>.</summary>
    public void MarkStale(string packPath)
    {
        foreach (var tab in Tabs.Where(t => string.Equals(t.PackPath, packPath, StringComparison.OrdinalIgnoreCase)))
        {
            tab.MarkStale();
        }
    }

    /// <summary>
    /// Marks every table tab and every hint tab of a table that is not stale yet: the schema changed, the tabs show what the old schema
    /// made of the entry. They are not decoded again by themselves.
    /// </summary>
    public void MarkSchemaStale()
    {
        foreach (var tab in Tabs.Where(t => t.IsTableEntry))
        {
            tab.MarkSchemaStale();
        }
    }

    private static bool Matches(TabViewModel tab, string packPath, string entryPath) =>
        string.Equals(tab.PackPath, packPath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(tab.EntryPath, entryPath, StringComparison.OrdinalIgnoreCase);
}
