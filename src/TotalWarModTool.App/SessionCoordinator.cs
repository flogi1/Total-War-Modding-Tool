using TotalWarModTool.Logging;
using TotalWarModTool.Session;
using TotalWarModTool.Storage;

namespace TotalWarModTool.App;

/// <summary>
/// Connects the tabs and the explorer with session.json: brings the stored session back after the start and writes the current one.
/// Lives on the UI thread. It never writes before the restore ended, never without a pack catalog (a session without a found game would
/// erase the stored tabs) and never over a file of a newer version or one that could not be read.
/// </summary>
public sealed class SessionCoordinator
{
    private readonly SessionStore _store;
    private readonly SessionLoadResult _loaded;
    private readonly WorkspaceController _workspace;
    private readonly ExplorerViewModel _explorer;
    private readonly TabsViewModel _tabs;
    private readonly bool _writable;
    private bool _restored;

    public SessionCoordinator(SessionStore store, SessionLoadResult loaded, WorkspaceController workspace, ExplorerViewModel explorer,
        TabsViewModel tabs, IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(explorer);
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(log);

        _store = store;
        _loaded = loaded;
        _workspace = workspace;
        _explorer = explorer;
        _tabs = tabs;

        _writable = loaded.Status is not (LoadStatus.NewerVersion or LoadStatus.Unreadable);
        if (!_writable)
        {
            var reason = loaded.Status == LoadStatus.NewerVersion
                ? "was written by a newer version of the app and would lose what this version does not know"
                : "could not be read and would be overwritten with an empty session";
            log.Warn($"The session will not be saved in this session: {store.FilePath} {reason}.");
        }

        tabs.Changed += (_, _) => RaiseDirty();
        explorer.ExpandedPacksChanged += (_, _) => RaiseDirty();
    }

    /// <summary>Tabs, selection or expanded packs changed after the restore ended.</summary>
    public event EventHandler? Dirty;

    /// <summary>The session as it is now: tabs in order, the index of the selected tab (-1 without tabs) and the expanded packs.</summary>
    public SessionState Capture()
    {
        var tabs = _tabs.Tabs.Select(tab => new SessionTab(tab.PackPath, tab.EntryPath)).ToList();
        var selected = _tabs.SelectedTab is { } tab ? _tabs.Tabs.IndexOf(tab) : -1;
        return new SessionState(tabs, tabs.Count == 0 ? -1 : Math.Max(selected, 0), _explorer.ExpandedPackPaths);
    }

    /// <summary>
    /// Brings back the packs and tabs of the stored session, silently. Call once, after <see cref="WorkspaceController.LoadAsync"/>.
    /// What no longer exists is left out (the controller logs each such case); <see cref="Dirty"/> then comes once at the end.
    /// </summary>
    public async Task RestoreAsync()
    {
        var dropped = false;
        try
        {
            var catalog = _workspace.Catalog;
            if (catalog is null)
            {
                return;
            }

            var schema = _workspace.Schema;
            var result = await _workspace.RestoreAsync(_loaded.State);

            // A restore of an older pack list or schema comes back empty. That is no reason to write: the stored session stays as it is.
            if (!ReferenceEquals(catalog, _workspace.Catalog) || !ReferenceEquals(schema, _workspace.Schema))
            {
                return;
            }

            _explorer.ApplyRestored(result.Packs);
            foreach (var restored in result.Tabs)
            {
                if (!_tabs.Tabs.Any(tab => IsTabOf(tab, restored)))
                {
                    _tabs.Add(restored.Pack, restored.Entry, schema, select: false);
                }
            }

            // Set explicitly: Add selects a tab on its own when nothing is selected, and that must not be the active one by accident.
            if (result.ActiveTab >= 0 && result.ActiveTab < result.Tabs.Count)
            {
                _tabs.SelectedTab = _tabs.Tabs.FirstOrDefault(tab => IsTabOf(tab, result.Tabs[result.ActiveTab])) ?? _tabs.SelectedTab;
            }

            dropped = result.Tabs.Count < _loaded.State.Tabs.Count || result.Packs.Count < _loaded.State.ExpandedPacks.Count;
        }
        finally
        {
            _restored = true;
        }

        if (dropped)
        {
            Dirty?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Writes <see cref="Capture"/>, unless that is not allowed (see the class description). A failure to write is thrown.</summary>
    public void Save()
    {
        if (!_writable || !_restored || _workspace.Catalog is null)
        {
            return;
        }

        _store.Save(Capture());
    }

    private void RaiseDirty()
    {
        if (_restored)
        {
            Dirty?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsTabOf(TabViewModel tab, RestoredTab restored) =>
        string.Equals(tab.PackPath, restored.Pack.Path, StringComparison.OrdinalIgnoreCase)
        && string.Equals(tab.EntryPath, restored.Entry.EntryPath, StringComparison.OrdinalIgnoreCase);
}
