using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TotalWarModTool.GameProfiles;
using TotalWarModTool.Logging;
using TotalWarModTool.Operations;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Session;
using TotalWarModTool.Settings;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

public enum GameStatus { NoProfile, NotFound, Found }

public sealed record RestoredPack(PackSource Pack, PackContent Content);

public sealed record RestoredTab(PackSource Pack, OpenedEntry Entry);

/// <summary>What of a stored session could be brought back. <see cref="ActiveTab"/> indexes <see cref="Tabs"/> (-1 without tabs).</summary>
public sealed record RestoreResult(IReadOnlyList<RestoredPack> Packs, IReadOnlyList<RestoredTab> Tabs, int ActiveTab);

/// <summary>
/// State of one game: where it is, its packs, its schema and the indexes of the packs that are open. Lives on the UI thread;
/// work runs on pool threads. No window, no view: usable in tests with a fake operation runner.
/// The state is only touched on the calling thread, after the awaited work has ended. Events are raised once the state of an
/// operation is applied completely.
/// </summary>
public sealed class WorkspaceController
{
    private const string EntryMissingDetail = "The entry no longer exists in the pack.";

    private readonly GameSettings _gameSettings;
    private readonly GlobalSettings _settings;
    private readonly AppPaths _paths;
    private readonly ISteamLocationSource _steam;
    private readonly IOperationRunner _runner;
    private readonly IAppLog _log;
    private readonly AppTexts _texts;

    // Open packs by their path as the catalog delivers it, compared ignoring case.
    private readonly Dictionary<string, PackContent> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PackIndexResult> _indexProblems = new(StringComparer.OrdinalIgnoreCase);

    // The last state of every pack file this controller has seen, by path: from the catalog, then from every index that was read. A pack
    // whose file differs from it has changed since, whether or not its index is cached. Null: the catalog could not read the file.
    private Dictionary<string, FileFingerprint?> _seen = new(StringComparer.OrdinalIgnoreCase);

    // A settings change while a silent run is busy is remembered and carried out when that run ends.
    private bool _pendingRefresh;
    private bool _pendingSchemaReload;

    // Goes up whenever a located game, catalog or schema is applied. A silent run compares it with the value it started with and
    // discards its result when a newer state was applied meanwhile: the older result must never overwrite the newer one.
    private int _generation;

    public WorkspaceController(GameProfile? profile, GameSettings gameSettings, GlobalSettings settings, AppPaths paths,
        ISteamLocationSource steam, IOperationRunner runner, IAppLog log, AppTexts texts)
    {
        ArgumentNullException.ThrowIfNull(gameSettings);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(steam);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(texts);

        Profile = profile;
        _gameSettings = gameSettings;
        _settings = settings;
        _paths = paths;
        _steam = steam;
        _runner = runner;
        _log = log;
        _texts = texts;

        // The install folder is known before the first window appears; only the scan and the schema are loaded later.
        ApplyPlace(Locate(profile, gameSettings.InstallPath, steam));
        gameSettings.Changed += OnGameSettingsChanged;
        settings.Changed += OnSettingsChanged;
    }

    public GameProfile? Profile { get; }

    public GameStatus Status { get; private set; }

    public GameLocation? Location { get; private set; }

    /// <summary>True when a stored install path is set but has no data folder. There is no fall back to Steam then, so the problem stays visible.</summary>
    public bool ManualPathInvalid { get; private set; }

    /// <summary>Null until the first load ended and whenever the game is not found.</summary>
    public PackCatalogResult? Catalog { get; private set; }

    /// <summary>Null until the first load ended and whenever the game is not found.</summary>
    public ResolvedSchema? Schema { get; private set; }

    /// <summary>
    /// The table versions of the game's vanilla packs, read after the catalog scan. Null until the first load ended and whenever the game is
    /// not found. An incomplete result counts as "not read": use <see cref="TableVersionsMissingReason"/> to find out.
    /// </summary>
    public GameTableVersionsResult? TableVersions { get; private set; }

    /// <summary>
    /// The UI text that says why the game's table versions cannot be used now; null when <see cref="TableVersions"/> is complete.
    /// Everything that writes a table in a version (new tables, build, import, adopt) and the object view wait for it.
    /// </summary>
    public string? TableVersionsMissingReason =>
        Status != GameStatus.Found ? _texts.TableVersionsNoGame
        : TableVersions is null ? _texts.TableVersionsReading
        : !TableVersions.IsComplete ? _texts.TableVersionsIncomplete
        : null;

    /// <summary>True during <see cref="LoadAsync"/> and <see cref="RestoreAsync"/>, the silent runs.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>True while a reload that a settings change asked for is parked because a run was busy; it starts when that run ends and raises <see cref="Changed"/> then.</summary>
    public bool HasPendingReload => _pendingRefresh || _pendingSchemaReload;

    public bool CanStartOperation => !IsBusy && !_runner.IsRunning;

    /// <summary>Status, location, catalog, schema or <see cref="IsBusy"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>A pack was found changed on disk. Its cached index was replaced or dropped before this is raised.</summary>
    public event EventHandler<PackSource>? PackChanged;

    /// <summary>
    /// Silent start: scans the packs of the already located game and resolves the schema, on a pool thread, without the runner.
    /// An unexpected error is logged and leaves the state without a catalog. Must be awaited before <see cref="RestoreAsync"/>.
    /// </summary>
    public async Task LoadAsync()
    {
        // The state is read before Changed is raised: a handler that starts or finishes a reload then counts as "meanwhile".
        var generation = _generation;
        var profile = Profile;
        var place = new Place(Status, Location, ManualPathInvalid);
        var schemaFolder = _settings.SchemaFolder;
        BeginSilentRun();
        try
        {
            var loaded = await Task.Run(() => Compute(profile, place, null, schemaFolder, null, CancellationToken.None));
            if (_generation == generation)
            {
                RememberFingerprints(loaded.Catalog);
                ApplyLoaded(loaded);
            }
            else
            {
                _log.Info("The first load was skipped: the game was loaded again in the meantime, the newer state is kept.");
            }
        }
        catch (Exception ex)
        {
            _log.Error("Loading the game failed.", ex);
            if (_generation == generation)
            {
                Catalog = null;
            }
        }
        finally
        {
            EndSilentRun();
        }

        // The returned task ends only after a reload that was asked for during the load has ended.
        await RunPending();
    }

    /// <summary>User action: locates the game and scans the packs again and reloads the schema, through the runner.</summary>
    public async Task RefreshAsync()
    {
        await RefreshCore();
        await RunPending();
    }

    private async Task RefreshCore()
    {
        if (!CanStartOperation || Profile is not { } profile)
        {
            return;
        }

        _pendingRefresh = false;
        _pendingSchemaReload = false;
        var installPath = _gameSettings.InstallPath;
        var schemaFolder = _settings.SchemaFolder;
        var outcome = await _runner.RunAsync(_texts.OperationRefreshPacks,
            (progress, ct) => Compute(profile, null, installPath, schemaFolder, progress, ct));
        if (outcome.Status != OperationStatus.Completed || outcome.Value is not { } loaded)
        {
            return;
        }

        var changed = DropStaleIndexes(loaded.Catalog);
        foreach (var pack in RememberFingerprints(loaded.Catalog))
        {
            if (!changed.Any(known => string.Equals(known.Path, pack.Path, StringComparison.OrdinalIgnoreCase)))
            {
                changed.Add(pack);
            }
        }

        ApplyLoaded(loaded);
        Changed?.Invoke(this, EventArgs.Empty);
        foreach (var pack in changed)
        {
            RaisePackChanged(pack, "its file differs from the cached index");
        }
    }

    /// <summary>User action: resolves the schema again, through the runner.</summary>
    public async Task ReloadSchemaAsync()
    {
        await ReloadSchemaCore();
        await RunPending();
    }

    private async Task ReloadSchemaCore()
    {
        if (!CanStartOperation || Profile is not { } profile)
        {
            return;
        }

        _pendingSchemaReload = false;
        var schemaFolder = _settings.SchemaFolder;
        var outcome = await _runner.RunAsync(_texts.OperationLoadSchema,
            (_, _) => SchemaSource.Resolve(_paths.SchemasDirectory, schemaFolder, profile.SchemaFile));
        if (outcome.Status != OperationStatus.Completed || outcome.Value is not { } resolved)
        {
            return;
        }

        _generation++;
        Schema = resolved;
        LogSchema(resolved);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The index and tree of a pack. Served from the cache when the pack is open; otherwise read through the runner and cached.
    /// Null when nothing can be started right now, the run was cancelled or failed, or the index has a problem
    /// (see <see cref="LastIndexProblem"/>).
    /// </summary>
    public async Task<PackContent?> OpenPackAsync(PackSource pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var content = await OpenPackCore(pack);
        await RunPending();
        return content;
    }

    private async Task<PackContent?> OpenPackCore(PackSource pack)
    {
        if (_cache.TryGetValue(pack.Path, out var cached))
        {
            return cached;
        }

        if (!CanStartOperation)
        {
            return null;
        }

        var outcome = await _runner.RunAsync(_texts.OperationOpenPack(pack.Name), (_, ct) => ReadContent(pack.Path, ct));
        if (outcome.Status != OperationStatus.Completed || outcome.Value is not { } read)
        {
            return null;
        }

        if (read.Content is { } content)
        {
            _cache[pack.Path] = content;
            _indexProblems.Remove(pack.Path);
            if (NoticeFingerprint(pack.Path, content.Index.Fingerprint))
            {
                RaisePackChanged(pack, "its file differs from the last state seen");
            }

            return content;
        }

        _indexProblems[pack.Path] = read.Result;
        _log.Warn($"Pack \"{pack.Name}\" cannot be opened ({read.Result.Problem}): {read.Result.Detail}");
        return null;
    }

    /// <summary>The cached content of a pack that is open, else null. Never reads a pack and never starts a run.</summary>
    public PackContent? OpenContent(PackSource pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return _cache.GetValueOrDefault(pack.Path);
    }

    /// <summary>Drops the cached index of a pack that was collapsed.</summary>
    public void ClosePack(PackSource pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        _cache.Remove(pack.Path);
    }

    /// <summary>The problem of the last failed <see cref="OpenPackAsync"/> of this pack, else null.</summary>
    public PackIndexResult? LastIndexProblem(PackSource pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return _indexProblems.GetValueOrDefault(pack.Path);
    }

    /// <summary>
    /// Opens one entry through the runner with the current schema. Problems come back inside the <see cref="OpenedEntry"/>.
    /// A pack that changed on disk is read again and the entry is tried a second time; <see cref="PackChanged"/> is raised after the run.
    /// Null when nothing can be started right now or the run was cancelled or failed.
    /// </summary>
    public async Task<OpenedEntry?> OpenEntryAsync(PackSource pack, string entryPath)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(entryPath);
        var opened = await OpenEntryCore(pack, entryPath);
        await RunPending();
        return opened;
    }

    private Task<OpenedEntry?> OpenEntryCore(PackSource pack, string entryPath)
    {
        var schema = Schema?.Schema;
        return RunEntryAsync(pack, entryPath,
            (index, missing, ct) => OpenFrom(index, entryPath, schema, missing, ct),
            opened => opened.ReadProblem,
            read => IndexProblemEntry(entryPath, read),
            opened => LogEntryProblem(pack, opened));
    }

    /// <summary>
    /// Reads the uncompressed bytes of one entry through the runner, with the cached index of the pack when it is open (the index handling is
    /// that of <see cref="OpenEntryAsync"/>: a pack that changed on disk is read again and <see cref="PackChanged"/> is raised after the run).
    /// The size limit is the one of <see cref="PackReader.ReadEntry"/> (<see cref="PackReader.DefaultMaxEntryBytes"/> uncompressed); a larger
    /// entry comes back as <see cref="EntryProblem.TooLarge"/>. A read problem comes back inside the result, with no bytes. Null when nothing
    /// can be started right now or the run was cancelled or failed.
    /// </summary>
    public async Task<EntryContent?> ReadEntryBytesAsync(PackSource pack, string entryPath)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(entryPath);
        var content = await RunEntryAsync(pack, entryPath,
            (index, missing, ct) => index.Find(entryPath) is { } entry
                ? PackReader.ReadEntry(index, entry, ct)
                : ProblemContent(missing, EntryMissingDetail),
            read => read.Problem,
            read => ProblemContent(EntryProblemOf(read), read.Detail),
            read => LogContentProblem(pack, entryPath, read));
        await RunPending();
        return content;
    }

    // One run through the runner that does something with one entry of a pack: the index is the cached one, or read inside the run; an entry
    // that cannot be read because the pack changed gets one second try with a fresh index. What happens to the cache, the fingerprints and
    // the log afterwards is the same for every kind of result.
    private async Task<T?> RunEntryAsync<T>(PackSource pack, string entryPath,
        Func<PackIndex, EntryProblem, CancellationToken, T> read, Func<T, EntryProblem> problemOf,
        Func<PackIndexResult, T> fromIndexProblem, Action<T> logResult)
        where T : class
    {
        if (!CanStartOperation)
        {
            return null;
        }

        var cached = _cache.GetValueOrDefault(pack.Path);
        var entryName = entryPath[(entryPath.LastIndexOfAny(['/', '\\']) + 1)..];
        var outcome = await _runner.RunAsync(_texts.OperationOpenEntry(entryName),
            (_, ct) => RunWithIndex(pack.Path, cached, read, problemOf, fromIndexProblem, ct));
        if (outcome.Status != OperationStatus.Completed || outcome.Value is not { } run)
        {
            return null;
        }

        // The state of the file that the index of this run was read from, when this run read one.
        var noticed = run.Fingerprint is { } fingerprint && NoticeFingerprint(pack.Path, fingerprint);
        if (run.PackChanged)
        {
            // Only an open pack keeps an index; a failed re-read leaves no replacement and drops it.
            if (_cache.ContainsKey(pack.Path))
            {
                if (run.Replacement is { } replacement)
                {
                    _cache[pack.Path] = replacement;
                }
                else
                {
                    _cache.Remove(pack.Path);
                }
            }

            _indexProblems.Remove(pack.Path);
        }

        if (run.IndexProblem is { } indexProblem)
        {
            _log.Warn($"Entry \"{entryPath}\" cannot be opened, the index of pack \"{pack.Name}\" cannot be read ({indexProblem.Problem}): {indexProblem.Detail}");
        }
        else
        {
            logResult(run.Result);
        }

        if (run.PackChanged)
        {
            RaisePackChanged(pack, "an entry could not be read from the cached index");
        }
        else if (noticed)
        {
            RaisePackChanged(pack, "its file differs from the last state seen");
        }

        return run.Result;
    }

    /// <summary>
    /// Silent: brings back the open packs and tabs of a stored session, on a pool thread, without the runner. Packs and entries that no
    /// longer exist or cannot be read are left out and logged. Paths are matched against the catalog ignoring case; the catalog's
    /// spelling counts afterwards. Call after <see cref="LoadAsync"/> ended.
    /// Null means "not restored": the restore failed (logged) or its result was discarded because the pack list or the schema changed
    /// meanwhile. An empty result is a restore of a session that had nothing to bring back.
    /// </summary>
    public async Task<RestoreResult?> RestoreAsync(SessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // The state is read before Changed is raised, as in LoadAsync.
        var generation = _generation;
        var catalog = Catalog;
        var schema = Schema?.Schema;
        BeginSilentRun();
        RestoreResult? result = null;
        try
        {
            var restored = await Task.Run(() => RestoreCore(catalog, schema, state));
            if (_generation != generation)
            {
                // Packs and tabs of an older pack list or schema must not come back.
                _log.Info("Session restore skipped: the pack list or the schema changed in the meantime.");
            }
            else
            {
                result = ApplyRestore(restored);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Restoring the session failed.", ex);
            result = null;
        }
        finally
        {
            EndSilentRun();
        }

        _ = RunPending();
        return result;
    }

    // Applies what a restore read: caches the open packs, logs what was left out and notices changed packs. Returns the result for the caller.
    private RestoreResult ApplyRestore(RestoreRun restored)
    {
        foreach (var restoredPack in restored.Result.Packs)
        {
            _cache[restoredPack.Pack.Path] = restoredPack.Content;
            _indexProblems.Remove(restoredPack.Pack.Path);
        }

        foreach (var (isWarning, text) in restored.Notes)
        {
            if (isWarning)
            {
                _log.Warn(text);
            }
            else
            {
                _log.Info(text);
            }
        }

        foreach (var tab in restored.Result.Tabs)
        {
            LogEntryProblem(tab.Pack, tab.Entry);
        }

        foreach (var (readPack, fingerprint) in restored.Fingerprints)
        {
            if (NoticeFingerprint(readPack.Path, fingerprint))
            {
                RaisePackChanged(readPack, "its file differs from the last state seen");
            }
        }

        return restored.Result;
    }

    // ---- state -----------------------------------------------------------------------------------------------------------------

    private void BeginSilentRun()
    {
        IsBusy = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EndSilentRun()
    {
        IsBusy = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyPlace(Place place)
    {
        Status = place.Status;
        Location = place.Location;
        ManualPathInvalid = place.ManualPathInvalid;
    }

    // Does not raise Changed: the caller does, once, after everything of the operation is applied.
    private void ApplyLoaded(Loaded loaded)
    {
        _generation++;
        ApplyPlace(loaded.Place);
        Catalog = loaded.Catalog;
        Schema = loaded.Schema;
        TableVersions = loaded.TableVersions;
        LogCatalog(loaded.Catalog);
        LogSchema(loaded.Schema);
        LogTableVersions(loaded.TableVersions);
    }

    // Drops the cached index of every pack that is gone or whose file differs from the one the index was read from.
    // Returns the changed packs that still exist, in the spelling of the new catalog.
    private List<PackSource> DropStaleIndexes(PackCatalogResult? catalog)
    {
        var byPath = new Dictionary<string, PackSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in catalog?.Packs ?? [])
        {
            byPath.TryAdd(pack.Path, pack);
        }

        var changed = new List<PackSource>();
        foreach (var (path, content) in _cache.ToList())
        {
            if (!byPath.TryGetValue(path, out var current))
            {
                _cache.Remove(path);
            }
            else if (current.Fingerprint is not { } fingerprint || !SameFile(fingerprint, content.Index.Fingerprint))
            {
                _cache.Remove(path);
                changed.Add(current);
            }
        }

        _indexProblems.Clear();
        return changed;
    }

    // Remembers the fingerprints of a catalog and returns the packs of it whose file differs from the one remembered before.
    // A pack that is gone is forgotten without a report; a pack seen for the first time is only remembered.
    private List<PackSource> RememberFingerprints(PackCatalogResult? catalog)
    {
        var changed = new List<PackSource>();
        var next = new Dictionary<string, FileFingerprint?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in catalog?.Packs ?? [])
        {
            if (next.ContainsKey(pack.Path))
            {
                continue;
            }

            if (_seen.GetValueOrDefault(pack.Path) is { } known && pack.Fingerprint is { } current && !SameFile(known, current))
            {
                changed.Add(pack);
            }

            next[pack.Path] = pack.Fingerprint;
        }

        _seen = next;
        return changed;
    }

    // Remembers the fingerprint of an index that was read. True when it differs from the one remembered before.
    private bool NoticeFingerprint(string path, FileFingerprint fingerprint)
    {
        var known = _seen.GetValueOrDefault(path);
        _seen[path] = fingerprint;
        return known is not null && !SameFile(known, fingerprint);
    }

    private static bool SameFile(FileFingerprint a, FileFingerprint b) => a.Length == b.Length && a.LastWriteTimeUtc == b.LastWriteTimeUtc;

    private void RaisePackChanged(PackSource pack, string reason)
    {
        _log.Info($"Pack \"{pack.Name}\" was changed on disk ({reason}); its cached index was dropped or read again.");
        PackChanged?.Invoke(this, pack);
    }

    // ---- settings --------------------------------------------------------------------------------------------------------------

    private void OnGameSettingsChanged(object? sender, string property)
    {
        if (property == nameof(GameSettings.InstallPath))
        {
            Request(refresh: true);
        }
    }

    private void OnSettingsChanged(object? sender, string property)
    {
        if (property == nameof(GlobalSettings.SchemaFolder))
        {
            Request(refresh: false);
        }
    }

    private void Request(bool refresh)
    {
        if (!CanStartOperation)
        {
            if (refresh)
            {
                _pendingRefresh = true;
            }
            else
            {
                _pendingSchemaReload = true;
            }

            return;
        }

        _ = Observe(refresh ? RefreshAsync() : ReloadSchemaAsync());
    }

    // A refresh includes the schema, so it also settles a remembered schema reload. The wishes are cleared before the run starts, so a
    // wish that cannot be carried out (no profile) is not retried forever. The task ends when the reload has ended.
    private Task RunPending()
    {
        if (!CanStartOperation)
        {
            return Task.CompletedTask;
        }

        if (_pendingRefresh)
        {
            _pendingRefresh = false;
            _pendingSchemaReload = false;
            return Observe(RefreshAsync());
        }

        if (_pendingSchemaReload)
        {
            _pendingSchemaReload = false;
            return Observe(ReloadSchemaAsync());
        }

        return Task.CompletedTask;
    }

    // The operations handle their own failures; this only makes sure a surprise is logged and never lost in an unobserved task.
    private async Task Observe(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            _log.Error("A reload after a settings change failed.", ex);
        }
    }

    // ---- logging ---------------------------------------------------------------------------------------------------------------

    private void LogCatalog(PackCatalogResult? catalog)
    {
        if (catalog is null)
        {
            return;
        }

        foreach (var problem in catalog.FolderProblems)
        {
            _log.Warn($"Pack folder \"{problem.Path}\" could not be listed: {problem.Detail}");
        }

        var withProblem = catalog.Packs.Count(pack => pack.Problem != PackProblem.None);
        var message = $"Pack list: {catalog.Packs.Count} packs, {withProblem} with a problem"
                      + (catalog.UsedManifest ? ", vanilla packs taken from the manifest." : ".");
        if (withProblem > 0)
        {
            _log.Warn(message);
        }
        else
        {
            _log.Info(message);
        }
    }

    // One warning per opened entry that carries a problem: the user sees a hint for it, so it belongs in the log (technical detail included).
    private void LogEntryProblem(PackSource pack, OpenedEntry opened)
    {
        string what;
        string? detail;
        if (opened.ReadProblem != EntryProblem.None)
        {
            (what, detail) = ($"read problem {opened.ReadProblem}", opened.ReadDetail);
        }
        else if (opened.SchemaMissing)
        {
            (what, detail) = ("schema missing", "No schema is loaded.");
        }
        else if (opened.Table is { Status: not TableDecodeStatus.Ok } table)
        {
            (what, detail) = ($"table status {table.Status}", table.Detail);
        }
        else if (opened.Loc is { Status: LocDecodeStatus.Failed } loc)
        {
            (what, detail) = ($"loc status {loc.Status}", loc.Detail);
        }
        else
        {
            return;
        }

        _log.Warn($"Entry \"{opened.EntryPath}\" of pack \"{pack.Name}\" has a problem ({what}): {detail}");
    }

    // A read problem of an entry that was read for its bytes (adopting it into a project): there is no tab with a hint, so the log is the record.
    private void LogContentProblem(PackSource pack, string entryPath, EntryContent content)
    {
        if (content.Problem != EntryProblem.None)
        {
            _log.Warn($"Entry \"{DisplayText.Safe(entryPath)}\" of pack \"{DisplayText.Safe(pack.Name)}\" cannot be read ({content.Problem}): {DisplayText.Safe(content.Detail ?? string.Empty)}");
        }
    }

    private void LogTableVersions(GameTableVersionsResult? result)
    {
        if (result is null)
        {
            return;
        }

        foreach (var problem in result.Problems)
        {
            var where = problem.EntryPath is null ? problem.PackPath : problem.PackPath + " / " + problem.EntryPath;
            _log.Warn($"Table versions of the game: {DisplayText.Safe(where)} could not be read: {DisplayText.Safe(problem.Detail)}");
        }

        foreach (var conflict in result.Conflicts)
        {
            _log.Warn($"Table versions of the game: {DisplayText.Safe(conflict)}");
        }

        if (result.CacheWriteProblem is { } cacheProblem)
        {
            _log.Warn($"The table versions of the game could not be cached: {DisplayText.Safe(cacheProblem)}");
        }

        if (!result.IsComplete)
        {
            _log.Warn(result.Versions is null
                ? "The game has no vanilla pack; its table versions are unknown."
                : $"The table versions of the game were read incompletely ({result.Problems.Count} problems); building and new tables stay disabled.");
            return;
        }

        _log.Info($"Table versions of the game: {result.Versions!.Versions.Count} tables, {(result.FromCache ? "from the cache" : "read from the packs")}.");
    }

    private void LogSchema(ResolvedSchema? schema)
    {
        if (schema is null)
        {
            return;
        }

        if (schema.OverrideAttempt is { Problem: not SchemaProblem.None } overrideAttempt)
        {
            _log.Warn($"The override schema could not be loaded ({overrideAttempt.Problem}): {overrideAttempt.Detail}");
        }

        if (schema.ShippedAttempt is { Problem: not SchemaProblem.None } shippedAttempt)
        {
            _log.Warn($"The shipped schema could not be loaded ({shippedAttempt.Problem}): {shippedAttempt.Detail}");
        }

        if (schema.Origin == SchemaOrigin.None)
        {
            _log.Warn("No schema could be loaded; tables cannot be decoded.");
        }
        else
        {
            _log.Info($"Schema loaded from the {schema.Origin} file {schema.Path}.");
        }
    }

    // ---- work (pool threads: no controller state is touched) ---------------------------------------------------------------------

    private sealed record Place(GameStatus Status, GameLocation? Location, bool ManualPathInvalid);

    private sealed record Loaded(Place Place, PackCatalogResult? Catalog, ResolvedSchema? Schema, GameTableVersionsResult? TableVersions);

    private sealed record IndexRead(PackIndexResult Result, PackContent? Content);

    // Fingerprint: the file state of the index this run read (a fresh one or the re-read one); null when it used the cached index unchanged.
    private sealed record EntryRun<T>(T Result, bool PackChanged, PackContent? Replacement, PackIndexResult? IndexProblem = null,
        FileFingerprint? Fingerprint = null);

    private sealed record RestoreRun(RestoreResult Result, IReadOnlyList<(bool IsWarning, string Text)> Notes,
        IReadOnlyList<(PackSource Pack, FileFingerprint Fingerprint)> Fingerprints);

    private static Place Locate(GameProfile? profile, string? installPath, ISteamLocationSource steam)
    {
        if (profile is null)
        {
            return new Place(GameStatus.NoProfile, null, false);
        }

        if (installPath is not null)
        {
            // A stored path that does not work is not replaced by Steam: the user must see that the path is wrong.
            return GameLocator.FromInstallPath(profile, installPath) is { } manual
                ? new Place(GameStatus.Found, manual, false)
                : new Place(GameStatus.NotFound, null, true);
        }

        return GameLocator.Find(profile, steam) is { } found
            ? new Place(GameStatus.Found, found, false)
            : new Place(GameStatus.NotFound, null, false);
    }

    // The place is known (load at start) or looked up again (refresh).
    private Loaded Compute(GameProfile? profile, Place? knownPlace, string? installPath, string? schemaFolder,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var place = knownPlace ?? Locate(profile, installPath, _steam);
        PackCatalogResult? catalog = null;
        if (profile is not null && place.Location is { } location)
        {
            catalog = PackCatalog.Scan(profile, location.InstallPath, location.WorkshopPath, progress, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        GameTableVersionsResult? versions = null;
        if (profile is not null && catalog is not null)
        {
            versions = GameTableVersionsCache.LoadOrRead(
                Path.Combine(_paths.CacheDirectory, $"table_versions_{profile.Id}.json"), catalog.Packs, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var schema = profile is null ? null : SchemaSource.Resolve(_paths.SchemasDirectory, schemaFolder, profile.SchemaFile);
        return new Loaded(place, catalog, schema, versions);
    }

    private static IndexRead ReadContent(string packPath, CancellationToken cancellationToken)
    {
        var result = PackAccess.ReadIndex(packPath, cancellationToken);
        return result.Index is { } index
            ? new IndexRead(result, new PackContent(index, PackTree.Build(index.Entries, cancellationToken)))
            : new IndexRead(result, null);
    }

    // The work of RunEntryAsync: finds the index (cached, or read here) and hands it to read; read gets the problem to report for an entry that
    // is not in the index (Corrupt for the first try, PackChanged after the index was read again).
    private static EntryRun<T> RunWithIndex<T>(string packPath, PackContent? cached, Func<PackIndex, EntryProblem, CancellationToken, T> read,
        Func<T, EntryProblem> problemOf, Func<PackIndexResult, T> fromIndexProblem, CancellationToken cancellationToken)
    {
        var index = cached?.Index;
        if (index is null)
        {
            var indexRead = PackAccess.ReadIndex(packPath, cancellationToken);
            if (indexRead.Index is null)
            {
                return new EntryRun<T>(fromIndexProblem(indexRead), false, null, indexRead);
            }

            index = indexRead.Index;
        }

        var freshFingerprint = cached is null ? index.Fingerprint : null;
        var first = read(index, EntryProblem.Corrupt, cancellationToken);
        if (problemOf(first) != EntryProblem.PackChanged)
        {
            return new EntryRun<T>(first, false, null, Fingerprint: freshFingerprint);
        }

        // The pack changed under the cached index. Read the index again; if that fails the first result stands.
        var reread = PackAccess.ReadIndex(packPath, cancellationToken);
        if (reread.Index is not { } newIndex)
        {
            return new EntryRun<T>(first, true, null);
        }

        var replacement = cached is null ? null : new PackContent(newIndex, PackTree.Build(newIndex.Entries, cancellationToken));
        return new EntryRun<T>(read(newIndex, EntryProblem.PackChanged, cancellationToken), true, replacement, Fingerprint: newIndex.Fingerprint);
    }

    // An entry that is not in the index is damaged, unless the index was read again because the pack changed: then the pack changed under it.
    private static OpenedEntry OpenFrom(PackIndex index, string entryPath, GameSchema? schema, EntryProblem missing, CancellationToken cancellationToken) =>
        index.Find(entryPath) is { } entry
            ? EntryOpener.Open(index, entry, schema, cancellationToken)
            : ProblemEntry(entryPath, missing, EntryMissingDetail);

    // The index of a pack that is not open could not be read: the entry shows it as the matching entry problem.
    private static OpenedEntry IndexProblemEntry(string entryPath, PackIndexResult read) =>
        ProblemEntry(entryPath, EntryProblemOf(read), read.Detail);

    private static EntryProblem EntryProblemOf(PackIndexResult read) => read.Problem switch
    {
        PackProblem.NotFound => EntryProblem.PackChanged,
        PackProblem.Unreadable => EntryProblem.Unreadable,
        PackProblem.EncryptedIndex => EntryProblem.Encrypted,
        _ => EntryProblem.Corrupt,
    };

    private static EntryContent ProblemContent(EntryProblem problem, string? detail) => new(null, CompressionFormat.None, 0, problem, detail);

    private static OpenedEntry ProblemEntry(string entryPath, EntryProblem problem, string? detail) =>
        new(entryPath, EntryOpener.KindOf(entryPath), 0, 0, CompressionFormat.None, problem, detail,
            SchemaMissing: false, Table: null, Loc: null, Text: null);

    private static RestoreRun RestoreCore(PackCatalogResult? catalog, GameSchema? schema, SessionState state)
    {
        var notes = new List<(bool IsWarning, string Text)>();
        var byPath = new Dictionary<string, PackSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in catalog?.Packs ?? [])
        {
            byPath.TryAdd(pack.Path, pack);
        }

        // Index per pack path; null once a read failed, so a broken pack is read and logged only once.
        var indexes = new Dictionary<string, PackIndex?>(StringComparer.OrdinalIgnoreCase);
        PackIndex? IndexOf(PackSource pack)
        {
            if (indexes.TryGetValue(pack.Path, out var known))
            {
                return known;
            }

            var read = PackAccess.ReadIndex(pack.Path, CancellationToken.None);
            if (read.Index is null)
            {
                notes.Add((true, $"Session: the index of pack \"{pack.Name}\" cannot be read ({read.Problem}): {read.Detail}"));
            }

            indexes[pack.Path] = read.Index;
            return read.Index;
        }

        var packs = new List<RestoredPack>();
        foreach (var path in state.ExpandedPacks)
        {
            if (!byPath.TryGetValue(path, out var pack))
            {
                notes.Add((false, $"Session: pack \"{Path.GetFileName(path)}\" is no longer in the pack list and stays closed."));
            }
            else if (packs.All(restored => restored.Pack != pack) && IndexOf(pack) is { } index)
            {
                packs.Add(new RestoredPack(pack, new PackContent(index, PackTree.Build(index.Entries, CancellationToken.None))));
            }
        }

        var tabs = new List<RestoredTab>();
        var active = -1;
        for (var i = 0; i < state.Tabs.Count; i++)
        {
            var tab = state.Tabs[i];
            var tabName = Path.GetFileName(tab.PackPath) + ": " + tab.EntryPath;
            if (!byPath.TryGetValue(tab.PackPath, out var pack))
            {
                notes.Add((false, $"Session: tab \"{tabName}\" was dropped, the pack is no longer in the pack list."));
            }
            else if (IndexOf(pack) is not { } index)
            {
                notes.Add((false, $"Session: tab \"{tabName}\" was dropped, the pack cannot be read."));
            }
            else if (index.Find(tab.EntryPath) is not { } entry)
            {
                notes.Add((false, $"Session: tab \"{tabName}\" was dropped, the entry no longer exists in the pack."));
            }
            else
            {
                // A read or decode problem does not drop the tab: it shows the problem.
                var opened = EntryOpener.Open(index, entry, schema, CancellationToken.None);
                if (i == state.ActiveTab)
                {
                    active = tabs.Count;
                }

                tabs.Add(new RestoredTab(pack, opened));
            }
        }

        active = tabs.Count == 0 ? -1 : Math.Max(active, 0);
        var fingerprints = indexes.Where(entry => entry.Value is not null && byPath.ContainsKey(entry.Key))
            .Select(entry => (byPath[entry.Key], entry.Value!.Fingerprint)).ToList();
        return new RestoreRun(new RestoreResult(packs, tabs, active), notes, fingerprints);
    }
}
