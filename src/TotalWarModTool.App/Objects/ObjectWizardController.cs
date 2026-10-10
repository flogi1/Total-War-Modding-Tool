using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TotalWarModTool.Logging;
using TotalWarModTool.Objects;
using TotalWarModTool.Operations;

namespace TotalWarModTool.App;

/// <summary>
/// The wizards of the menu Tools (spec 5.8): "New building" and "New trait" load the templates of their wizard through the runner, ask the dialog
/// and write the composed copy through the write path of the copy (<see cref="ObjectApplyController.WriteCopyAsync"/>, one undo step), then open
/// the new object in a new tab. Lives on the UI thread; created by the composition root after the <see cref="ObjectApplyController"/>.
/// The log gets fixed English phrases (and raw table and field names), never the localized texts of the dialogs.
/// </summary>
public sealed class ObjectWizardController : ObservableObject
{
    private const string TableSuffix = "_tables";
    private const string BuildingWizardName = "new building wizard";
    private const string TraitWizardName = "new trait wizard";

    private readonly ObjectsController _objects;
    private readonly ObjectApplyController _apply;
    private readonly ProjectsController _projects;
    private readonly IOperationRunner _runner;
    private readonly IObjectDialogs _dialogs;
    private readonly IAppLog _log;
    private readonly AppTexts _texts;
    private readonly RelayCommand _newBuildingCommand;
    private readonly RelayCommand _newTraitCommand;

    // The names of each wizard the schema does not know, worked out once per stores (a schema change makes new stores).
    private readonly MissingNames _buildingMissing = new();
    private readonly MissingNames _traitMissing = new();

    // One wizard at a time: the dialogs are modal, but the command may be asked again between two runs.
    private bool _running;

    /// <summary>What the templates run delivered: the load order it was read with and the data of the wizard.</summary>
    private sealed record Loaded<T>(LoadOrder Order, T Data);

    /// <summary>Why a wizard cannot start: Reason is the text for the user, Cause the fixed English phrase for the log.</summary>
    private sealed record Refusal(string Reason, string Cause);

    /// <summary>What a wizard needs to start, taken on the UI thread.</summary>
    private sealed record Start<TDefinition>(ObjectStores Stores, Func<LoadOrder> CaptureOrder, ProjectState Project, WizardDefinitions Wizards,
        TDefinition Definition);

    // MissingIn of a definition against the graph of the stores, worked out once per stores; empty without stores.
    private sealed class MissingNames
    {
        private ObjectStores? _stores;
        private IReadOnlyList<string> _missing = [];

        public IReadOnlyList<string> Of(ObjectStores? stores, Func<ReferenceGraph, IReadOnlyList<string>> missingIn)
        {
            if (stores is null)
            {
                return [];
            }

            if (!ReferenceEquals(stores, _stores))
            {
                _missing = missingIn(stores.Graph);
                _stores = stores;
            }

            return _missing;
        }
    }

    public ObjectWizardController(ObjectsController objects, ObjectApplyController apply, ProjectsController projects, IOperationRunner runner,
        IObjectDialogs dialogs, IAppLog log, AppTexts texts)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(texts);
        _objects = objects;
        _apply = apply;
        _projects = projects;
        _runner = runner;
        _dialogs = dialogs;
        _log = log;
        _texts = texts;
        _newBuildingCommand = new RelayCommand(() => RunDetached(NewBuildingAsync, "The new building wizard"), () => NewBuildingDisabledReason is null);
        _newTraitCommand = new RelayCommand(() => RunDetached(NewTraitAsync, "The new trait wizard"), () => NewTraitDisabledReason is null);

        projects.Changed += (_, _) => Refresh();
        projects.OperationStateChanged += (_, _) => Refresh();
        objects.Changed += (_, _) => Refresh();
    }

    public ICommand NewBuildingCommand => _newBuildingCommand;

    public ICommand NewTraitCommand => _newTraitCommand;

    /// <summary>
    /// The first that applies: no usable project (objects.apply.reason.noProject), no game or schema (as ObjectsController), busy, no building wizard
    /// in the profile (objects.wizard.missing), names unknown to the schema (objects.wizard.schemaMismatch with MissingIn). The schema is compared
    /// once the stores exist; this getter does not make them (only a build or the wizard itself does).
    /// </summary>
    public string? NewBuildingDisabledReason => BuildingRefusal()?.Reason;

    /// <summary>Like <see cref="NewBuildingDisabledReason"/>, for the trait wizard of the profile.</summary>
    public string? NewTraitDisabledReason => TraitRefusal()?.Reason;

    /// <summary>The wizard "New building" (the NewBuildingCommand). Does nothing while NewBuildingDisabledReason is set (logged).</summary>
    public async Task NewBuildingAsync()
    {
        if (BuildingRefusal() is { } refusal)
        {
            _log.Info($"The {BuildingWizardName} was refused: {refusal.Cause}.");
            return;
        }

        await RunWizardAsync(NewBuildingCoreAsync);
    }

    /// <summary>The wizard "New trait" (the NewTraitCommand). Does nothing while NewTraitDisabledReason is set (logged).</summary>
    public async Task NewTraitAsync()
    {
        if (TraitRefusal() is { } refusal)
        {
            _log.Info($"The {TraitWizardName} was refused: {refusal.Cause}.");
            return;
        }

        await RunWizardAsync(NewTraitCoreAsync);
    }

    private async Task RunWizardAsync(Func<Task> core)
    {
        _running = true;
        try
        {
            await core();
        }
        finally
        {
            _running = false;
            Refresh();
        }
    }

    private async Task NewBuildingCoreAsync()
    {
        if (Begin(BuildingWizardName, "building", wizards => wizards.Building, _buildingMissing, (d, graph) => d.MissingIn(graph),
            _texts.ObjectsWizardBuildingTitle) is not { } start)
        {
            return;
        }

        var (stores, captureOrder, project, wizards, definition) = start;
        var loaded = await LoadAsync(BuildingWizardName, captureOrder, (order, ct) =>
            BuildingWizard.Catalog(definition, table => stores.Tables.Get(table, order, ct), stores.Loc.Get(order, ct)));
        if (loaded is null)
        {
            return;
        }

        var catalog = loaded.Data;
        var levelTable = ShortName(definition.LevelTable);
        var chainTable = ShortName(definition.ChainTable);
        string? ExistsIn(string key) =>
            catalog.HasLevel(key) ? levelTable : catalog.HasChain(key + definition.ChainKeySuffix) ? chainTable : null;
        var preselected = catalog.PreselectFor(_objects.SelectedObjectTab?.Result?.View);
        var answer = _dialogs.AskNewBuilding(new NewBuildingInput(catalog, preselected, key => ObjectKeyRules.Problem(_texts, key, ExistsIn)));
        if (answer is null)
        {
            _log.Info($"The {BuildingWizardName} was cancelled in its dialog.");
            return;
        }

        // Taken on this thread: the catalog says whether the template has a chain to copy.
        var templateChain = answer.After is null ? catalog.ChainOf(answer.Template) : null;
        var levelId = new ObjectId(definition.LevelTable, [answer.Template]);
        var newId = new ObjectId(definition.LevelTable, [answer.Key]);
        var description = "New building: " + DisplayText.Safe(answer.Key);
        var done = await _apply.WriteCopyAsync(
            (s, order, ct) =>
            {
                // One allocator for both copies: it avoids only the values it handed out itself.
                var integerKeys = _objects.IntegerKeys(s, order, ct);
                ObjectView Build(ObjectId id) => s.Builder.Build(id, order, null, ct);
                var levelCopy = ObjectCopy.Rename(Build(levelId), newId, Build, _objects.CategoryOf, s.Graph, integerKeys);
                var chainCopy = templateChain is null
                    ? null
                    : ObjectCopy.Rename(Build(new ObjectId(definition.ChainTable, [templateChain])), BuildingWizard.ChainIdFor(definition, answer.Key), Build,
                        _objects.CategoryOf, s.Graph, integerKeys);
                return BuildingWizard.Compose(definition, levelCopy, chainCopy, answer.After, wizards.NameSuffix);
            },
            loaded.Order, project, stores, description, DisplayText.Safe(answer.Key), () => true);
        if (done is null)
        {
            return;
        }

        // The new building opens in a new tab, built through the build handler; an undo finds that tab again.
        var shown = new ObjectRef(definition.LevelTable, [answer.Key], null);
        var tab = _objects.OpenObject(shown);
        var mode = answer.After is null ? "a new chain" : "a stage after " + DisplayText.Safe(answer.After.LevelKey);
        await _apply.FinishCopyAsync(done, tab, shown,
            $"New building {DisplayText.Safe(definition.LevelTable)} {DisplayText.Safe(answer.Key)} from {DisplayText.Safe(answer.Template)} as {mode}");
    }

    private async Task NewTraitCoreAsync()
    {
        if (Begin(TraitWizardName, "trait", wizards => wizards.Trait, _traitMissing, (d, graph) => d.MissingIn(graph), _texts.ObjectsWizardTraitTitle)
            is not { } start)
        {
            return;
        }

        var (stores, captureOrder, project, wizards, definition) = start;
        var loaded = await LoadAsync(TraitWizardName, captureOrder, (order, ct) =>
            TraitWizard.Templates(definition, stores.Tables.Get(definition.TraitTable, order, ct), stores.Loc.Get(order, ct)));
        if (loaded is null)
        {
            return;
        }

        var templates = loaded.Data;
        var traitTable = ShortName(definition.TraitTable);
        var known = templates.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        var answer = _dialogs.AskNewTrait(new NewTraitInput(templates, key => ObjectKeyRules.Problem(_texts, key, k => known.Contains(k) ? traitTable : null)));
        if (answer is null)
        {
            _log.Info($"The {TraitWizardName} was cancelled in its dialog.");
            return;
        }

        var traitId = new ObjectId(definition.TraitTable, [answer.Template]);
        var newId = new ObjectId(definition.TraitTable, [answer.Key]);
        var description = "New trait: " + DisplayText.Safe(answer.Key);
        var done = await _apply.WriteCopyAsync(
            (s, order, ct) =>
            {
                ObjectView Build(ObjectId id) => s.Builder.Build(id, order, null, ct);

                // The levels with the trait key as prefix are sub-objects of the copy (rule 3); their texts get the suffix.
                var traitCopy = ObjectCopy.Rename(Build(traitId), newId, Build, _objects.CategoryOf, s.Graph, _objects.IntegerKeys(s, order, ct));
                return TraitWizard.Compose(definition, traitCopy, wizards.NameSuffix);
            },
            loaded.Order, project, stores, description, DisplayText.Safe(answer.Key), () => true);
        if (done is null)
        {
            return;
        }

        // The new trait opens in a new tab, built through the build handler; an undo finds that tab again.
        var shown = new ObjectRef(definition.TraitTable, [answer.Key], null);
        var tab = _objects.OpenObject(shown);
        await _apply.FinishCopyAsync(done, tab, shown,
            $"New trait {DisplayText.Safe(definition.TraitTable)} {DisplayText.Safe(answer.Key)} from {DisplayText.Safe(answer.Template)}");
    }

    // Takes what a wizard needs on the UI thread; logs the cause (one line per cause) and returns null when something is missing. The stores may be
    // new: only now can the schema be compared with the names of the wizard, and a mismatch is shown to the user under the title of the wizard.
    private Start<TDefinition>? Begin<TDefinition>(string wizard, string section, Func<WizardDefinitions, TDefinition?> select, MissingNames cache,
        Func<TDefinition, ReferenceGraph, IReadOnlyList<string>> missingIn, string title)
        where TDefinition : class
    {
        if (_objects.EnsureStores() is not { } stores)
        {
            _log.Info($"The {wizard} was dropped: there are no stores (no game or no schema).");
            return null;
        }

        if (_objects.CaptureOrder() is not { } captureOrder)
        {
            _log.Info($"The {wizard} was dropped: there is no game.");
            return null;
        }

        if (_projects.Active is not { IsUsable: true } project)
        {
            _log.Info($"The {wizard} was dropped: there is no active usable project.");
            return null;
        }

        if (_objects.Wizards is not { } wizards || select(wizards) is not { } definition)
        {
            _log.Info($"The {wizard} was dropped: the game profile has no {section} wizard.");
            return null;
        }

        if (cache.Of(stores, graph => missingIn(definition, graph)) is { Count: > 0 } missing)
        {
            _log.Warn($"The {wizard} was refused: the schema does not know {Names(missing)}.");
            _dialogs.ShowMessage(title, _texts.ObjectsWizardSchemaMismatch(Names(missing)));
            return null;
        }

        return new Start<TDefinition>(stores, captureOrder, project, wizards, definition);
    }

    // The run that reads the templates; null (logged) when it did not complete.
    private async Task<Loaded<T>?> LoadAsync<T>(string wizard, Func<LoadOrder> captureOrder, Func<LoadOrder, CancellationToken, T> read)
    {
        OperationOutcome<Loaded<T>>? loadRun = null;
        try
        {
            loadRun = await _runner.RunAsync(_texts.ObjectsWizardLoading, (progress, ct) =>
            {
                progress.Report(new OperationProgress(0, 1, string.Empty));
                var order = captureOrder();
                return new Loaded<T>(order, read(order, ct));
            });
        }
        catch (Exception ex) when (IsEdgeFailure(ex))
        {
            _log.Error($"Loading the templates of the {wizard} could not be started.", ex);
        }

        if (loadRun is not { Status: OperationStatus.Completed, Value: { } loaded })
        {
            _log.Info($"The {wizard} ended without templates: " + (loadRun?.Status.ToString() ?? "not started") + ".");
            return null;
        }

        return loaded;
    }

    private Refusal? BuildingRefusal() => RefusalFor(_objects.Wizards?.Building, "building", _buildingMissing, (d, graph) => d.MissingIn(graph));

    private Refusal? TraitRefusal() => RefusalFor(_objects.Wizards?.Trait, "trait", _traitMissing, (d, graph) => d.MissingIn(graph));

    private Refusal? RefusalFor<TDefinition>(TDefinition? definition, string section, MissingNames cache,
        Func<TDefinition, ReferenceGraph, IReadOnlyList<string>> missingIn)
        where TDefinition : class
    {
        if (_projects.Active is not { IsUsable: true })
        {
            return new Refusal(_texts.ObjectsApplyNoProject, "there is no active usable project");
        }

        if (_objects.EnvironmentReason() is { } environment)
        {
            return new Refusal(environment, environment == _texts.ProjectBusy ? "another operation runs" : "there is no game or no schema");
        }

        if (_running)
        {
            return new Refusal(_texts.ProjectBusy, "another wizard runs");
        }

        if (definition is null)
        {
            return new Refusal(_texts.ObjectsWizardMissing, $"the game profile has no {section} wizard");
        }

        return cache.Of(_objects.Stores, graph => missingIn(definition, graph)) is { Count: > 0 } missing
            ? new Refusal(_texts.ObjectsWizardSchemaMismatch(Names(missing)), $"the schema does not know {Names(missing)}")
            : null;
    }

    // The raw table and field names, escaped for display and log.
    private static string Names(IReadOnlyList<string> missing) => string.Join(", ", missing.Select(name => DisplayText.Safe(name)));

    // The answers of the disabled reasons may have changed.
    private void Refresh()
    {
        OnPropertyChanged(nameof(NewBuildingDisabledReason));
        OnPropertyChanged(nameof(NewTraitDisabledReason));
        _newBuildingCommand.RaiseCanExecuteChanged();
        _newTraitCommand.RaiseCanExecuteChanged();
    }

    // The name of a table as the key check shows it: without "_tables".
    private static string ShortName(string table) =>
        table.EndsWith(TableSuffix, StringComparison.OrdinalIgnoreCase) ? table[..^TableSuffix.Length] : table;

    private async void RunDetached(Func<Task> work, string what)
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

    private static bool IsEdgeFailure(Exception ex) =>
        ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or Win32Exception;
}
