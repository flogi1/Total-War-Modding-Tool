using TotalWarModTool.Logging;
using TotalWarModTool.Session;
using TotalWarModTool.Storage;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// Connects the tabs and the explorer with session.json: brings the stored session back after the start and writes the current one.
/// Lives on the UI thread. It never writes before a restore succeeded (a restore without catalog, a failed one and a discarded one have not
/// succeeded; it is tried again with the next catalog), never without a pack catalog (a session without a found game would erase the stored tabs) and never over a file of a newer
/// version or one that could not be read.
/// </summary>
public sealed class SessionCoordinator
{
    private readonly SessionStore _store;
    private readonly SessionLoadResult _loaded;
    private readonly WorkspaceController _workspace;
    private readonly ExplorerViewModel _explorer;
    private readonly TabsViewModel _tabs;
    private readonly IAppLog _log;
    private readonly ProjectsController? _projects;
    private readonly bool _writable;
    private bool _restored;

    // At most one restore runs at a time; a call while one runs joins it.
    private bool _attempting;
    private Task _current = Task.CompletedTask;

    // The catalog the last attempt started with (null: there was none). A new attempt needs a catalog that is not this one.
    private PackCatalogResult? _lastCatalog;

    /// <param name="projects">The mod projects; null: entries of projects in the stored session are not brought back and fall out of the next save.</param>
    public SessionCoordinator(SessionStore store, SessionLoadResult loaded, WorkspaceController workspace, ExplorerViewModel explorer,
        TabsViewModel tabs, IAppLog log, ProjectsController? projects = null)
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
        _log = log;
        _projects = projects;

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
        workspace.Changed += (_, _) => RetryRestore();
    }

    /// <summary>Tabs, selection or expanded packs changed after the restore ended.</summary>
    public event EventHandler? Dirty;

    /// <summary>
    /// The session as it is now: tabs in order (a project file is a tab with the key <c>project:&lt;guid&gt;</c> of its project), the index of
    /// the selected tab (-1 without tabs), the expanded packs and then the expanded projects as <c>project:&lt;guid&gt;</c>.
    /// </summary>
    public SessionState Capture()
    {
        var tabs = _tabs.Tabs.Select(tab => new SessionTab(tab.PackPath, tab.EntryPath)).ToList();
        var selected = _tabs.SelectedTab is { } tab ? _tabs.Tabs.IndexOf(tab) : -1;
        var expanded = _explorer.ExpandedPackPaths.Concat(_explorer.ExpandedProjectIds.Select(TabSource.KeyOf)).ToList();
        return new SessionState(tabs, tabs.Count == 0 ? -1 : Math.Max(selected, 0), expanded);
    }

    /// <summary>
    /// Brings back the packs and tabs of the stored session, silently. Call once, after <see cref="WorkspaceController.LoadAsync"/>.
    /// When that is not possible (no catalog because the game was not found, or the controller reports "not restored"), the session is
    /// not touched and nothing is saved; the restore is tried again when a new catalog arrives. A call while a restore runs joins it, and
    /// after a successful restore it does nothing. While a reload that a settings change asked for is parked, it does nothing either: the
    /// change that follows that reload restores against the new catalog. What no longer exists is left out (the controller logs each such case);
    /// <see cref="Dirty"/> then comes once at the end.
    /// </summary>
    public Task RestoreAsync()
    {
        if (_restored || _attempting)
        {
            return _current;
        }

        if (_workspace.HasPendingReload)
        {
            return Task.CompletedTask;
        }

        _current = AttemptAsync();
        return _current;
    }

    private async Task AttemptAsync()
    {
        _attempting = true;
        try
        {
            var catalog = _workspace.Catalog;
            _lastCatalog = catalog;
            if (catalog is null)
            {
                return;
            }

            var schema = _workspace.Schema;
            var stored = _loaded.State;
            var (packState, projectTabs, projectIds) = Split(stored);

            // The project part starts first and runs beside the pack part (both are silent and on pool threads). It is always awaited, so that
            // a failure of it never goes unobserved.
            var projectRestore = _projects is null || (projectTabs.Count == 0 && projectIds.Count == 0)
                ? Task.FromResult(new ProjectRestoreResult([], []))
                : _projects.RestoreAsync(projectIds, projectTabs);
            var result = await _workspace.RestoreAsync(packState);
            var projectResult = await projectRestore;
            if (result is null)
            {
                return;
            }

            _explorer.ApplyRestored(result.Packs);
            _explorer.ApplyRestoredProjects(projectResult.Expanded);

            // The tabs come back in the stored order; what has no counterpart was dropped. A project that left the list meanwhile has no tabs.
            var candidates = result.Tabs.Select(tab => new RestoredEntry(tab.Pack, tab.Entry))
                .Concat(projectResult.Tabs.Where(tab => _projects!.Projects.Any(state => state.Id == tab.ProjectId))
                    .Select(tab => new RestoredEntry(TabSource.ForProject(tab.ProjectId, tab.Name), tab.Entry)))
                .ToList();
            var ordered = new List<RestoredEntry>();
            RestoredEntry? active = null;
            for (var i = 0; i < stored.Tabs.Count; i++)
            {
                var saved = stored.Tabs[i];
                var match = candidates.FirstOrDefault(candidate => IsTabOf(candidate, saved));
                if (match is null)
                {
                    continue;
                }

                if (!ordered.Exists(entry => ReferenceEquals(entry, match)))
                {
                    ordered.Add(match);
                }

                if (i == stored.ActiveTab)
                {
                    active = match;
                }
            }

            foreach (var restored in ordered)
            {
                if (!_tabs.Tabs.Any(tab => IsTabOf(tab, restored)))
                {
                    _tabs.Add(restored.Source, restored.Entry, schema, select: false);
                }
            }

            // Set explicitly: Add selects a tab on its own when nothing is selected, and that must not be the active one by accident.
            // The stored active tab is the one to show; when it was dropped, the first tab is.
            if (ordered.Count > 0)
            {
                var shown = active ?? ordered[0];
                _tabs.SelectedTab = _tabs.Tabs.FirstOrDefault(tab => IsTabOf(tab, shown)) ?? _tabs.SelectedTab;
            }

            _restored = true;

            // Once at the end: something was left out, or the user already had tabs or packs or projects open that the stored session does not know.
            var restoredOpen = result.Packs.Count + projectResult.Expanded.Count;
            var leftOut = ordered.Count < stored.Tabs.Count || restoredOpen < stored.ExpandedPacks.Count;
            var alreadyOpen = _tabs.Tabs.Count != ordered.Count || _explorer.ExpandedPackPaths.Count + _explorer.ExpandedProjectIds.Count != restoredOpen;
            if (leftOut || alreadyOpen)
            {
                Dirty?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _attempting = false;
        }
    }

    // A game that is found later (the install folder was chosen in the settings) delivers a catalog after the start: restore then.
    private async void RetryRestore()
    {
        var workspace = _workspace;
        if (_restored || _attempting || workspace.IsBusy || workspace.Catalog is not { } catalog || ReferenceEquals(catalog, _lastCatalog))
        {
            return;
        }

        try
        {
            await RestoreAsync();
        }
        catch (Exception ex)
        {
            _log.Error("Restoring the session after the game was found failed.", ex);
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

    // Divides the stored session: the pack part goes to the workspace as a session of its own, the project part to the projects. Entries of
    // projects are never given to the workspace (it would report each as a pack that is gone); without a projects controller they fall out.
    private static (SessionState Packs, List<(Guid ProjectId, string RelativeFile)> ProjectTabs, List<Guid> ProjectIds) Split(SessionState stored)
    {
        var packTabs = new List<SessionTab>();
        var projectTabs = new List<(Guid ProjectId, string RelativeFile)>();
        SessionTab? activeTab = stored.ActiveTab >= 0 && stored.ActiveTab < stored.Tabs.Count ? stored.Tabs[stored.ActiveTab] : null;
        foreach (var tab in stored.Tabs)
        {
            // A null tab is passed on: the workspace is the one that fails on it, and logs that.
            if (tab is not null && TabSource.TryGetProjectId(tab.PackPath, out var id))
            {
                projectTabs.Add((id, tab.EntryPath));
            }
            else
            {
                packTabs.Add(tab!);
            }
        }

        var packPaths = new List<string>();
        var projectIds = new List<Guid>();
        foreach (var path in stored.ExpandedPacks)
        {
            if (TabSource.TryGetProjectId(path, out var id))
            {
                projectIds.Add(id);
            }
            else
            {
                packPaths.Add(path);
            }
        }

        var packActive = activeTab is null ? -1 : packTabs.FindIndex(tab => ReferenceEquals(tab, activeTab));
        return (new SessionState(packTabs, packActive, packPaths), projectTabs, projectIds);
    }

    private static bool IsTabOf(TabViewModel tab, RestoredEntry restored) =>
        string.Equals(tab.PackPath, restored.Source.Key, StringComparison.OrdinalIgnoreCase)
        && string.Equals(tab.EntryPath, restored.Entry.EntryPath, StringComparison.OrdinalIgnoreCase);

    private static bool IsTabOf(RestoredEntry restored, SessionTab saved) =>
        string.Equals(saved.PackPath, restored.Source.Key, StringComparison.OrdinalIgnoreCase)
        && string.Equals(saved.EntryPath, restored.Entry.EntryPath, StringComparison.OrdinalIgnoreCase);

    // A tab that came back, from a pack or from a project; Source.Key is the path of the pack or the key of the project.
    private sealed record RestoredEntry(TabSource Source, OpenedEntry Entry);
}
