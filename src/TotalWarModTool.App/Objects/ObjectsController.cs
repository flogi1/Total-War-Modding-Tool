using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TotalWarModTool.GameProfiles;
using TotalWarModTool.Logging;
using TotalWarModTool.Objects;
using TotalWarModTool.Operations;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// The object view of the app: keeps the stores of the current schema, works out the load order (vanilla packs, active mods, the active
/// project), builds the objects of the object tabs through the runner, opens and navigates object tabs, marks the ones whose sources changed
/// and jumps from an object to its row. Lives on the UI thread. One per game profile; created by the composition root after the main view
/// model exists. Writes nothing.
/// </summary>
public sealed partial class ObjectsController : IRowObjectOpener
{
    private const string CategoryFolderName = "objects";
    private const string CategoryFileExtension = ".json";

    private readonly WorkspaceController _workspace;
    private readonly ProjectsController? _projects;
    private readonly TabsViewModel _tabs;
    private readonly IOperationRunner _runner;
    private readonly IAppLog _log;
    private readonly AppTexts _texts;
    private readonly Func<string, string> _translate;
    private readonly Func<PackSource, string, Task> _openEntry;
    private readonly Func<Guid, string, Task> _openProjectFile;
    private readonly IReadOnlyDictionary<string, CategoryDefinition> _categories;

    // The root tables whose category file could not be read: their tabs say that the default layout is used.
    private readonly HashSet<string> _unreadableCategories = new(StringComparer.OrdinalIgnoreCase);

    // Made by the first build, dropped when the schema or the game folder changes. Null in between.
    private ObjectStores? _stores;

    // The catalog seen at the last change of the workspace: a new one may hold changed packs.
    private PackCatalogResult? _seenCatalog;

    /// <param name="openEntry">MainViewModel.OpenEntryAsync: opens or activates the tab of a pack entry.</param>
    /// <param name="openProjectFile">MainViewModel.OpenProjectFileAsync: opens or activates the tab of a project file.</param>
    /// <param name="translate">UiText.Get with the key as fallback (section titles of the category files).</param>
    public ObjectsController(WorkspaceController workspace, ProjectsController? projects, DocumentsController? documents, TabsViewModel tabs,
        IOperationRunner runner, AppPaths paths, IAppLog log, AppTexts texts, Func<string, string> translate,
        Func<PackSource, string, Task> openEntry, Func<Guid, string, Task> openProjectFile)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(translate);
        ArgumentNullException.ThrowIfNull(openEntry);
        ArgumentNullException.ThrowIfNull(openProjectFile);
        _workspace = workspace;
        _projects = projects;
        _tabs = tabs;
        _runner = runner;
        _log = log;
        _texts = texts;
        _translate = translate;
        _openEntry = openEntry;
        _openProjectFile = openProjectFile;
        _categories = LoadCategories(paths, workspace.Profile);
        Wizards = LoadWizards(paths, workspace.Profile);
        _seenCatalog = workspace.Catalog;

        workspace.Changed += (_, _) => OnWorkspaceChanged();
        workspace.PackChanged += (_, _) => StartStaleCheck();
        if (projects is not null)
        {
            projects.Changed += (_, _) =>
            {
                RefreshApplyStates();
                StartStaleCheck();
            };
            projects.OperationStateChanged += (_, _) => RefreshApplyStates();
        }

        // A save changes the set of modified tabs, and a saved project table may be a source of an object.
        if (documents is not null)
        {
            documents.ModifiedChanged += (_, _) => StartStaleCheck();
        }

        _seenSelectedTab = tabs.SelectedTab;
        tabs.Changed += (_, _) => OnTabsChanged();
    }

    /// <summary>Set by the composition root: the apply of a tab (ObjectApplyController.ApplyAsync). Copied to every tab made after it was set.</summary>
    internal Func<ObjectTabViewModel, Task>? ApplyHandler { get; set; }

    /// <summary>Set by the composition root: the question before a tab with unapplied text moves on or closes (ObjectApplyController.LeaveAsync). Copied to every tab made after it was set.</summary>
    internal Func<ObjectTabViewModel, Task<bool>>? LeaveHandler { get; set; }

    /// <summary>Set by the composition root like ApplyHandler (ObjectApplyController.CopyAsync), copied to every new tab through RunGuardedAsync.</summary>
    internal Func<ObjectTabViewModel, Task>? CopyHandler { get; set; }

    /// <summary>The wizards of the game profile (wizards.json beside the category files); null when the file is missing or cannot be used (logged).</summary>
    internal WizardDefinitions? Wizards { get; }

    /// <summary>The selected tab when it is an object tab; null otherwise.</summary>
    internal ObjectTabViewModel? SelectedObjectTab => _tabs.SelectedTab as ObjectTabViewModel;

    /// <summary>The random source of new integer keys; Random.Shared unless a test sets a seeded one.</summary>
    internal Random KeyRandom { get; set; } = Random.Shared;

    /// <summary>The answer of <see cref="DisabledReason"/> may have changed: the workspace changed, or an own operation ended.</summary>
    public event EventHandler? Changed;

    /// <summary>Why no object can be shown now: no game, no schema, another operation runs; null when one can.</summary>
    public string? DisabledReason(RowsTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return MissingReason() ?? (IsBusy ? _texts.ProjectBusy : null);
    }

    /// <summary>
    /// Builds the object of the tab through the runner. Does nothing while the runner is busy (the tab stays as it is) or while the tab builds;
    /// without a game or a schema the tab fails with the reason and the runner is not started.
    /// </summary>
    public async Task BuildAsync(ObjectTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.State == ObjectTabState.Building)
        {
            return;
        }

        if (MissingReason() is { } reason)
        {
            tab.ApplyFailed(reason);
            return;
        }

        if (IsBusy || CurrentContext() is not { } context)
        {
            return;
        }

        var stores = _stores ??= ObjectStores.Create(_workspace.Schema!, CurrentVersions(), context.InstallPath);
        var started = tab.Current;
        var category = _categories.GetValueOrDefault(started.Table);
        tab.SetCategoryProblem(_unreadableCategories.Contains(started.Table));
        tab.BeginBuild();
        OperationOutcome<ObjectBuildResult>? outcome = null;
        try
        {
            outcome = await _runner.RunAsync(_texts.ObjectsBuilding(MessageText.EscapeControlCharacters(started.DisplayKey)),
                (progress, ct) => Build(stores, context, started, category, progress, ct));
        }
        catch (Exception ex) when (IsEdgeFailure(ex))
        {
            // The runner could not run the build at all; the tab must not stay in "building".
            _log.Error("An object build could not be started.", ex);
        }
        finally
        {
            Changed?.Invoke(this, EventArgs.Empty);
            RefreshApplyStates();
        }

        // The tab moved on to another object meanwhile: this outcome belongs to none of its entries any more.
        if (!tab.Current.Equals(started))
        {
            return;
        }

        switch (outcome)
        {
            case { Status: OperationStatus.Completed, Value: { } result }:
                tab.ApplyResult(result);

                // The stores were dropped while the build ran: the result shows what the old schema made of the data, keep the mark.
                if (!ReferenceEquals(stores, _stores))
                {
                    tab.MarkSchemaStale();
                }

                break;
            case { Status: OperationStatus.Cancelled }:
                tab.ApplyCancelled();
                break;
            default:
                // A failed outcome was shown and logged by the runner; a RunAsync that threw was logged above.
                tab.ApplyFailed();
                break;
        }
    }

    // Runs inside the operation: reads used_mods.txt and language.txt, the packs and the project files.
    private static ObjectBuildResult Build(ObjectStores stores, BuildContext context, ObjectRef current, CategoryDefinition? category,
        IProgress<OperationProgress> progress, CancellationToken ct)
    {
        var order = Resolve(context, current);
        var view = stores.Builder.Build(current.ToObjectId(), order, progress, ct);
        var signatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in view.Tables)
        {
            signatures[table] = stores.Tables.SignatureOf(table, order, ct);
        }

        return new ObjectBuildResult(view, order, category, signatures, stores.Loc.SignatureOf(order, ct), stores.Loc.Get(order, ct));
    }

    private static LoadOrder Resolve(BuildContext context, ObjectRef current) =>
        LoadOrderResolver.Resolve(new LoadOrderRequest(context.Profile, context.InstallPath, context.Catalog, context.ProjectFolder, context.ProjectName,
            current.RootPackPath));

    private string? MissingReason()
    {
        if (_workspace.Status != GameStatus.Found || _workspace.Location is null || _workspace.Profile is null)
        {
            return _texts.ObjectsNoGame;
        }

        // Before the catalog: until the first load ended, the answer is "still being read", not "no game".
        if (_workspace.TableVersionsMissingReason is { } versionsReason)
        {
            return versionsReason;
        }

        if (_workspace.Catalog is null)
        {
            return _texts.ObjectsNoGame;
        }

        return _workspace.Schema?.Schema is null ? _texts.ObjectsNoSchema : null;
    }

    // The versions the stores are made for; only called after MissingReason() was null, which means they are complete.
    private GameTableVersions CurrentVersions() => _workspace.TableVersions!.Versions!;

    private bool IsBusy => _runner.IsRunning || !_workspace.CanStartOperation;

    /// <summary>The stores as they are now, without making any; null when the build has not made them yet or a schema or game folder change dropped them.</summary>
    internal ObjectStores? Stores => _stores;

    /// <summary>The stores of the current schema and game folder, made when missing; null without a game or a schema.</summary>
    internal ObjectStores? EnsureStores() =>
        MissingReason() is null && CurrentContext() is { } context ? _stores ??= ObjectStores.Create(_workspace.Schema!, CurrentVersions(), context.InstallPath) : null;

    /// <summary>
    /// The check while typing: parses the text of the tab against its result and diffs it, without reference checks (the apply checks those).
    /// Pure and quick enough for the UI thread; null without stores or a result. It never makes stores: only a build does, so a check after a
    /// schema change cannot rebuild the reference graph inside the typing timer.
    /// </summary>
    private ObjectChanges? Check(ObjectTabViewModel tab)
    {
        if (tab.Result is not { } result || _stores is not { } stores)
        {
            return null;
        }

        try
        {
            var parsed = ObjectTextParser.Parse(tab.Text, result.View, result.Category, stores.Graph);
            return parsed.HasErrors
                ? new ObjectChanges([], [], parsed.Diagnostics)
                : ObjectDiff.Compute(result.View, parsed, result.Category, stores.Graph, null);
        }
        catch (Exception ex)
        {
            // The text of the user is not logged.
            _log.Error("The check of an object text failed.", ex);
            return null;
        }
    }

    /// <summary>The category file of a root table; null when there is none.</summary>
    internal CategoryDefinition? CategoryOf(string table) => _categories.GetValueOrDefault(table);

    /// <summary>The load order without a root pack, as a closure that reads the files when it runs (inside the runner); null without a game.</summary>
    internal Func<LoadOrder>? CaptureOrder() =>
        CurrentContext() is { } context
            ? () => LoadOrderResolver.Resolve(new LoadOrderRequest(context.Profile, context.InstallPath, context.Catalog, context.ProjectFolder,
                context.ProjectName, null))
            : null;

    /// <summary>A new allocator for one copy: random integer keys that no row of the table in the load order has in the field.</summary>
    internal NewIntegerKey IntegerKeys(ObjectStores stores, LoadOrder order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stores);
        ArgumentNullException.ThrowIfNull(order);
        return RandomIntegerKeys.Create(KeyRandom, (table, field, value) => stores.Tables.Get(table, order, ct).FindByField(field, value).Count > 0);
    }

    /// <summary>Why nothing can be built or copied now, whatever the project: no game, no schema, another operation runs; null when none of these.</summary>
    internal string? EnvironmentReason() => MissingReason() ?? (IsBusy ? _texts.ProjectBusy : null);

    /// <summary>Why no tab can apply or copy now, whatever its own state: no active usable project, or another operation runs. Null when none of the two.</summary>
    internal string? ApplyEnvironmentReason() =>
        _projects?.Active is not { IsUsable: true } ? _texts.ObjectsApplyNoProject : IsBusy ? _texts.ProjectBusy : null;

    // The answers of ApplyEnvironmentReason may have changed for every open object tab.
    private void RefreshApplyStates()
    {
        foreach (var tab in ObjectTabs)
        {
            tab.RefreshApplyState();
        }
    }

    // What a load order is made of, taken on the UI thread; null without a game.
    private BuildContext? CurrentContext()
    {
        if (_workspace.Profile is not { } profile || _workspace.Location?.InstallPath is not { } installPath || _workspace.Catalog is not { } catalog)
        {
            return null;
        }

        var project = _projects?.Active is { IsUsable: true } active ? active : null;
        return new BuildContext(profile, installPath, catalog, project?.Folder, project?.DisplayName);
    }

    // The stores were made for a schema and a game folder; another one makes what they hold worthless, and every object tab shows what the
    // old ones made of the data.
    private void OnWorkspaceChanged()
    {
        if (_stores is { } stores
            && (SchemaDiffers(stores.Schema, _workspace.Schema)
                || !string.Equals(stores.InstallPath, _workspace.Location?.InstallPath, StringComparison.OrdinalIgnoreCase)
                || VersionsDiffer(stores.GameVersions, _workspace.TableVersions)))
        {
            stores.Clear();
            _stores = null;
            _log.Info("The stores of the object view were discarded: the schema, the game folder or the game's table versions changed.");
            foreach (var tab in ObjectTabs)
            {
                tab.MarkSchemaStale();
            }
        }

        var catalogChanged = !ReferenceEquals(_seenCatalog, _workspace.Catalog);
        _seenCatalog = _workspace.Catalog;
        Changed?.Invoke(this, EventArgs.Empty);
        RefreshApplyStates();
        if (catalogChanged)
        {
            StartStaleCheck();
        }
    }

    // Equal content read again (a refresh) is no change. A missing or incomplete result drops the stores: the object view is disabled then anyway.
    private static bool VersionsDiffer(GameTableVersions before, GameTableVersionsResult? after) =>
        after is not { IsComplete: true, Versions: { } versions } || !before.SameAs(versions);

    // As MainViewModel decides it: a reload that resolves the same file with the same date is no change.
    private static bool SchemaDiffers(ResolvedSchema before, ResolvedSchema? after) =>
        after is null
        || before.Origin != after.Origin
        || !string.Equals(before.Path, after.Path, StringComparison.OrdinalIgnoreCase)
        || before.FileDateUtc != after.FileDateUtc
        || (before.Schema is null) != (after.Schema is null);

    private IReadOnlyDictionary<string, CategoryDefinition> LoadCategories(AppPaths paths, GameProfile? profile)
    {
        if (profile is null)
        {
            return new Dictionary<string, CategoryDefinition>();
        }

        var loaded = CategoryDefinitions.LoadAll(Path.Combine(paths.ProfilesDirectory, profile.Id, CategoryFolderName));
        foreach (var problem in loaded.Problems)
        {
            _log.Warn($"Category file {DisplayText.Safe(problem.FileName)} was skipped: {DisplayText.Safe(problem.Detail)}");
            if (problem.FileName.EndsWith(CategoryFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                _unreadableCategories.Add(Path.GetFileNameWithoutExtension(problem.FileName));
            }
        }

        return loaded.ByRootTable;
    }

    private WizardDefinitions? LoadWizards(AppPaths paths, GameProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        var loaded = WizardDefinitionLoader.Load(Path.Combine(paths.ProfilesDirectory, profile.Id, WizardDefinitionLoader.FileName));
        if (loaded.Problem is { } problem)
        {
            _log.Warn($"The wizard file {WizardDefinitionLoader.FileName} was skipped: {DisplayText.Safe(problem)}");
        }

        return loaded.Definitions;
    }

    private static bool IsEdgeFailure(Exception ex) =>
        ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or Win32Exception;

    private sealed record BuildContext(GameProfile Profile, string InstallPath, PackCatalogResult Catalog, string? ProjectFolder, string? ProjectName);
}
