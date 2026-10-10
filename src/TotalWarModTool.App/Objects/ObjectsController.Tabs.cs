using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TotalWarModTool.Objects;
using TotalWarModTool.Projects;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

// Opening, navigation, the check for changed sources and the jump to the row.
public sealed partial class ObjectsController
{
    private const string BuildWhat = "Building an object";

    // The check that runs, and whether another one was asked for while it ran.
    private Task? _staleCheck;
    private bool _staleCheckAgain;

    // The number of open scopes of SuppressLazyBuilds.
    private int _suppressLazyBuilds;

    // The selected tab at the last change of the tab list: only a change of the selection builds lazily.
    private TabViewModel? _seenSelectedTab;

    /// <summary>The object tabs of the tab list, in tab order.</summary>
    public IReadOnlyList<ObjectTabViewModel> ObjectTabs => _tabs.Tabs.OfType<ObjectTabViewModel>().ToList();

    /// <summary>
    /// Shows the row as an object: selects the object tab that shows it already, else opens a new tab and builds it. The pack of a row from a
    /// pack that is not vanilla goes into the load order of the object (it may not be an active mod).
    /// </summary>
    public void Show(RowsTabViewModel tab, RowKey key)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(key);
        var target = new ObjectRef(key.Table, [.. key.KeyValues], RootPackOf(tab));
        if (ObjectTabs.FirstOrDefault(open => open.Current.Equals(target)) is { } existing)
        {
            _tabs.SelectedTab = existing;
            return;
        }

        var created = NewTab([target], 0);
        created.SetUnsavedSource(tab is EditableRowsTabViewModel editable && (editable.IsModified || editable.HasPendingEdit));
        AddAndBuild(created);
    }

    /// <summary>An Idle tab for a stored session (not built; built when it is activated). The caller adds it to the tab list.</summary>
    public ObjectTabViewModel CreateTab(IReadOnlyList<ObjectRef> history, int position) => NewTab(history, position);

    /// <summary>
    /// Silent: compares the sources of every Ready tab with the disk and marks the stale ones. One check at a time; a request while one runs
    /// is run once more afterwards, and the task of the running check (which ends after that) is returned.
    /// </summary>
    public Task CheckStaleAsync()
    {
        if (_staleCheck is { IsCompleted: false } running)
        {
            _staleCheckAgain = true;
            return running;
        }

        _staleCheck = RunStaleChecks();
        return _staleCheck;
    }

    /// <summary>The check started last (it may have ended); null before the first. Lets tests await a check that a change event started.</summary>
    internal Task? StaleCheck => _staleCheck;

    /// <summary>Opens the table the root row of the tab came from and selects the row there; the tab says when that was not possible.</summary>
    internal async Task JumpAsync(ObjectTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.Result?.View.Root is not { } root)
        {
            return;
        }

        var keyValues = tab.Current.KeyValues;
        var source = root.Provenance.Source;
        var entry = root.Provenance.EntryPath;
        string tabKey;
        string tabEntry;
        if (source.Kind == SourceKind.Project)
        {
            if (_projects?.Active is not { IsUsable: true } project || !SamePath(project.Folder, source.Path))
            {
                tab.SetJumpNotice(JumpOutcome.SourceGone);
                return;
            }

            var relative = BelowPackFolder(entry);
            await _openProjectFile(project.Id, relative);
            (tabKey, tabEntry) = (TabSource.KeyOf(project.Id), relative.Replace('\\', '/'));
        }
        else
        {
            if (_workspace.Catalog?.Packs.FirstOrDefault(pack => SamePath(pack.Path, source.Path)) is not { } pack)
            {
                tab.SetJumpNotice(JumpOutcome.SourceGone);
                return;
            }

            await _openEntry(pack, entry);
            (tabKey, tabEntry) = (pack.Path, entry);
        }

        // The open may have failed or been refused (the runner was busy, the tab list kept the old tab); then the selected tab is another one,
        // the open said why, and nothing is claimed about the row.
        if (_tabs.SelectedTab is not RowsTabViewModel rows
            || !SamePath(rows.PackPath, tabKey)
            || !string.Equals(rows.EntryPath, tabEntry, StringComparison.OrdinalIgnoreCase))
        {
            tab.SetJumpNotice(JumpOutcome.None);
            return;
        }

        tab.SetJumpNotice(rows.SelectRowByKey(keyValues) ? JumpOutcome.None : JumpOutcome.RowNotFound);
    }

    private ObjectTabViewModel NewTab(IReadOnlyList<ObjectRef> history, int position)
    {
        var tab = new ObjectTabViewModel(_texts, _tabs.Close, history, position, _translate)
        {
            BuildHandler = tab => RunDetached(() => BuildAsync(tab), BuildWhat),
            OpenHandler = (_, target, _) => AddAndBuild(NewTab([target], 0)),
            JumpHandler = tab => RunDetached(() => JumpAsync(tab), "Jumping to the row of an object"),
            ApplyEnvironmentReason = ApplyEnvironmentReason,
            CheckHandler = Check,
        };

        // The composition root sets the two handlers right after it made the apply controller, before any tab is restored.
        if (ApplyHandler is { } apply)
        {
            tab.ApplyHandler = target => RunGuardedAsync(() => apply(target), "Applying an object");
        }

        if (LeaveHandler is { } leave)
        {
            tab.LeaveHandler = target => LeaveGuardedAsync(leave, target);
        }

        return tab;
    }

    // A question that fails unexpectedly at the edge of the app counts as "stay": the tab keeps its text.
    private async Task<bool> LeaveGuardedAsync(Func<ObjectTabViewModel, Task<bool>> leave, ObjectTabViewModel tab)
    {
        try
        {
            return await leave(tab);
        }
        catch (Exception ex) when (IsEdgeFailure(ex))
        {
            _log.Error("The question about unapplied object text failed unexpectedly.", ex);
            return false;
        }
    }

    private void AddAndBuild(ObjectTabViewModel tab)
    {
        _tabs.AddTab(tab);

        // Selecting the tab may have started its build already (see OnTabsChanged); only a tab that is still idle is built here.
        if (tab.State == ObjectTabState.Idle)
        {
            RunDetached(() => BuildAsync(tab), BuildWhat);
        }
    }

    // A tab that was not built yet (restored, or the runner was busy) is built when it is shown, unless a restore scope is open. Only a change
    // of the selection counts: a tab added to the list raises a change while the old tab is still selected, and that one must not be built then.
    private void OnTabsChanged()
    {
        var selected = _tabs.SelectedTab;
        if (ReferenceEquals(selected, _seenSelectedTab))
        {
            return;
        }

        _seenSelectedTab = selected;
        if (_suppressLazyBuilds == 0)
        {
            BuildSelectedIfIdle();
        }
    }

    private void BuildSelectedIfIdle()
    {
        if (_tabs.SelectedTab is ObjectTabViewModel { State: ObjectTabState.Idle } tab)
        {
            RunDetached(() => BuildAsync(tab), BuildWhat);
        }
    }

    /// <summary>
    /// Opens a scope in which selecting an Idle object tab starts no build (a restore adds and selects several tabs and only the one that stays
    /// selected should be built). When the last open scope is disposed, the selected tab is built if it is Idle and the runner is free.
    /// </summary>
    public IDisposable SuppressLazyBuilds()
    {
        _suppressLazyBuilds++;
        return new LazyBuildScope(this);
    }

    private sealed class LazyBuildScope(ObjectsController owner) : IDisposable
    {
        private ObjectsController? _owner = owner;

        public void Dispose()
        {
            if (_owner is not { } controller)
            {
                return;
            }

            _owner = null;
            if (--controller._suppressLazyBuilds == 0)
            {
                controller.BuildSelectedIfIdle();
            }
        }
    }

    // The pack of a row that comes from a pack of the catalog that is not vanilla; null for vanilla packs, project files and packs the catalog
    // no longer lists (a pack that is gone cannot feed the load order).
    private string? RootPackOf(RowsTabViewModel tab)
    {
        if (TabSource.TryGetProjectId(tab.PackPath, out _))
        {
            return null;
        }

        var pack = _workspace.Catalog?.Packs.FirstOrDefault(known => SamePath(known.Path, tab.PackPath));
        return pack is { Origin: not PackOrigin.Vanilla } ? tab.PackPath : null;
    }

    private void StartStaleCheck() => RunDetached(CheckStaleAsync, "Checking the object tabs for changed sources");

    private async Task RunStaleChecks()
    {
        do
        {
            _staleCheckAgain = false;
            await CheckStaleOnce();
        }
        while (_staleCheckAgain);
    }

    // The pairs are taken on the UI thread, the load orders and signatures are computed on a pool thread, the marks are set on the UI thread.
    private async Task CheckStaleOnce()
    {
        if (_stores is not { } stores || CurrentContext() is not { } context)
        {
            // Without stores the schema mark is there already; without a game nothing can be compared.
            return;
        }

        var candidates = ObjectTabs
            .Where(tab => tab.State == ObjectTabState.Ready && tab.Result is not null)
            .Select(tab => new StaleCandidate(tab, tab.Result!, tab.Current))
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        List<StaleCandidate> changed;
        try
        {
            changed = await Task.Run(() => candidates.Where(candidate => SourcesChanged(stores, context, candidate)).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _log.Error("Checking the object tabs for changed sources failed.", ex);
            return;
        }

        // Stores that were dropped meanwhile marked their tabs already; a tab with a new result shows the new state.
        if (!ReferenceEquals(_stores, stores))
        {
            return;
        }

        foreach (var candidate in changed.Where(candidate => ReferenceEquals(candidate.Tab.Result, candidate.Result)))
        {
            candidate.Tab.MarkStale();
        }
    }

    private static bool SourcesChanged(ObjectStores stores, BuildContext context, StaleCandidate candidate)
    {
        var order = Resolve(context, candidate.Current);
        var before = candidate.Result.Order.Sources;
        if (before.Count != order.Sources.Count
            || before.Zip(order.Sources).Any(pair => pair.First.Kind != pair.Second.Kind || !SamePath(pair.First.Path, pair.Second.Path)))
        {
            return true;
        }

        foreach (var (table, signature) in candidate.Result.TableSignatures)
        {
            if (!string.Equals(stores.Tables.SignatureOf(table, order, CancellationToken.None), signature, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return !string.Equals(stores.Loc.SignatureOf(order, CancellationToken.None), candidate.Result.LocSignature, StringComparison.Ordinal);
    }

    // "pack\db\x_tables\a.tsv" (as the store reads it) to "db\x_tables\a.tsv" (below the pack folder, as the project tabs open it).
    private static string BelowPackFolder(string entry)
    {
        var prefix = ProjectFile.PackFolder;
        return entry.Length > prefix.Length
            && entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && entry[prefix.Length] is '\\' or '/'
            ? entry[(prefix.Length + 1)..]
            : entry;
    }

    private static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Starts work whose result nobody awaits (a handler of a tab, a change event). async void on purpose: the exceptions that such work can meet at
    // the edge of the app are logged here; anything else is a surprise and reaches the global handler.
    private async void RunDetached(Func<Task> work, string what) => await RunGuardedAsync(work, what);

    private async Task RunGuardedAsync(Func<Task> work, string what)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (IsEdgeFailure(ex))
        {
            _log.Error($"{what} failed unexpectedly.", ex);
        }
    }

    private sealed record StaleCandidate(ObjectTabViewModel Tab, ObjectBuildResult Result, ObjectRef Current);
}
