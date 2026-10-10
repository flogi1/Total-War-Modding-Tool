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
/// The wizards of the menu Tools (spec 5.8): "New building" loads the catalog of the building wizard through the runner, asks the dialog and
/// writes the composed copy through the write path of the copy (<see cref="ObjectApplyController.WriteCopyAsync"/>, one undo step), then opens
/// the new building in a new tab. Lives on the UI thread; created by the composition root after the <see cref="ObjectApplyController"/>.
/// </summary>
public sealed class ObjectWizardController : ObservableObject
{
    private const string TableSuffix = "_tables";

    private readonly ObjectsController _objects;
    private readonly ObjectApplyController _apply;
    private readonly ProjectsController _projects;
    private readonly IOperationRunner _runner;
    private readonly IObjectDialogs _dialogs;
    private readonly IAppLog _log;
    private readonly AppTexts _texts;
    private readonly RelayCommand _newBuildingCommand;

    // The names of the building wizard the schema does not know, worked out once per stores (a schema change makes new stores).
    private ObjectStores? _checkedStores;
    private IReadOnlyList<string> _missing = [];

    // One wizard at a time: the dialogs are modal, but the command may be asked again between two runs.
    private bool _running;

    private sealed record Loaded(LoadOrder Order, BuildingCatalog Catalog);

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

        projects.Changed += (_, _) => Refresh();
        projects.OperationStateChanged += (_, _) => Refresh();
        objects.Changed += (_, _) => Refresh();
    }

    public ICommand NewBuildingCommand => _newBuildingCommand;

    /// <summary>
    /// The first that applies: no usable project (objects.apply.reason.noProject), no game or schema (as ObjectsController), busy, no building wizard
    /// in the profile (objects.wizard.missing), names unknown to the schema (objects.wizard.schemaMismatch with MissingIn). The schema is compared
    /// once the stores exist; this getter does not make them (only a build or the wizard itself does).
    /// </summary>
    public string? NewBuildingDisabledReason
    {
        get
        {
            if (_projects.Active is not { IsUsable: true })
            {
                return _texts.ObjectsApplyNoProject;
            }

            if (_objects.EnvironmentReason() is { } environment)
            {
                return environment;
            }

            if (_running)
            {
                return _texts.ProjectBusy;
            }

            if (_objects.Wizards?.Building is not { } definition)
            {
                return _texts.ObjectsWizardMissing;
            }

            return MissingNames(definition, _objects.Stores) is { Count: > 0 } missing
                ? _texts.ObjectsWizardSchemaMismatch(string.Join(", ", missing.Select(name => DisplayText.Safe(name))))
                : null;
        }
    }

    /// <summary>The wizard "New building" (the NewBuildingCommand). Does nothing while NewBuildingDisabledReason is set (logged).</summary>
    public async Task NewBuildingAsync()
    {
        if (NewBuildingDisabledReason is { } reason)
        {
            _log.Info("The new building wizard was refused: " + reason);
            return;
        }

        _running = true;
        try
        {
            await NewBuildingCoreAsync();
        }
        finally
        {
            _running = false;
            Refresh();
        }
    }

    private async Task NewBuildingCoreAsync()
    {
        if (_objects.EnsureStores() is not { } stores || _objects.CaptureOrder() is not { } captureOrder
            || _projects.Active is not { IsUsable: true } project || _objects.Wizards is not { Building: { } definition } wizards)
        {
            _log.Info("The new building wizard was dropped: no stores, no game, no usable project or no building wizard.");
            return;
        }

        // The stores may be new: only now can the schema be compared with the names of the wizard.
        if (MissingNames(definition, stores) is { Count: > 0 } missing)
        {
            var mismatch = _texts.ObjectsWizardSchemaMismatch(string.Join(", ", missing.Select(name => DisplayText.Safe(name))));
            _log.Warn("The new building wizard was refused: " + mismatch);
            _dialogs.ShowMessage(_texts.ObjectsWizardBuildingTitle, mismatch);
            return;
        }

        OperationOutcome<Loaded>? loadRun = null;
        try
        {
            loadRun = await _runner.RunAsync(_texts.ObjectsWizardLoading, (progress, ct) =>
            {
                progress.Report(new OperationProgress(0, 1, string.Empty));
                var order = captureOrder();
                var catalog = BuildingWizard.Catalog(definition, table => stores.Tables.Get(table, order, ct), stores.Loc.Get(order, ct));
                return new Loaded(order, catalog);
            });
        }
        catch (Exception ex) when (IsEdgeFailure(ex))
        {
            _log.Error("Loading the templates of the new building wizard could not be started.", ex);
        }

        if (loadRun is not { Status: OperationStatus.Completed, Value: { } loaded })
        {
            _log.Info("The new building wizard ended without templates: " + (loadRun?.Status.ToString() ?? "not started") + ".");
            return;
        }

        var catalog = loaded.Catalog;
        var levelTable = ShortName(definition.LevelTable);
        var chainTable = ShortName(definition.ChainTable);
        string? ExistsIn(string key) =>
            catalog.HasLevel(key) ? levelTable : catalog.HasChain(key + definition.ChainKeySuffix) ? chainTable : null;
        var preselected = catalog.PreselectFor(_objects.SelectedObjectTab?.Result?.View);
        var answer = _dialogs.AskNewBuilding(new NewBuildingInput(catalog, preselected, key => ObjectKeyRules.Problem(_texts, key, ExistsIn)));
        if (answer is null)
        {
            _log.Info("The new building wizard was cancelled in its dialog.");
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
        _apply.RememberOrigin(done.Step, tab, shown);
        var summary = done.Plan.Summary;
        var mode = answer.After is null ? "a new chain" : "a stage after " + DisplayText.Safe(answer.After.LevelKey);
        _log.Info($"New building {DisplayText.Safe(definition.LevelTable)} {DisplayText.Safe(answer.Key)} from {DisplayText.Safe(answer.Template)} as {mode}: "
            + $"{summary.RowsInserted} rows, {summary.TextsInserted} texts, {summary.FilesChanged} files changed, {summary.FilesCreated} files created, "
            + $"{done.Notes.Count} notes.");
        tab.SetApplyResult(summary, ObjectApplyController.CreatedFiles(done.Plan), 0, done.Notes);
        await _apply.CheckAfterWriteAsync();
    }

    // MissingIn of the definition against the graph of the stores, worked out once per stores; empty without stores.
    private IReadOnlyList<string> MissingNames(BuildingWizardDefinition definition, ObjectStores? stores)
    {
        if (stores is null)
        {
            return [];
        }

        if (!ReferenceEquals(stores, _checkedStores))
        {
            _missing = definition.MissingIn(stores.Graph);
            _checkedStores = stores;
        }

        return _missing;
    }

    // The answers of the disabled reason may have changed.
    private void Refresh()
    {
        OnPropertyChanged(nameof(NewBuildingDisabledReason));
        _newBuildingCommand.RaiseCanExecuteChanged();
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
