using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TotalWarModTool.Editing;
using TotalWarModTool.Logging;
using TotalWarModTool.Objects;
using TotalWarModTool.Operations;
using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.App;

/// <summary>
/// Writes the text of an object tab into the active project: parses and diffs through the runner (run A), asks the questions of the plan,
/// writes through the runner (run B), records the undo step and rebuilds the tab. Lives on the UI thread; created by the composition root after
/// the <see cref="ObjectsController"/> and the <see cref="DocumentsController"/>. Writes only through <see cref="ProjectWriter"/> (and the
/// <see cref="FileContentsStep"/> it returns).
/// </summary>
public sealed class ObjectApplyController
{
    // The cap of the error lines in a dialog is CappedLines.MaxLines; a detail of a foreign file is cut after this many characters.
    private const int MaxDetailChars = 300;

    private readonly ObjectsController _objects;
    private readonly ProjectsController _projects;
    private readonly DocumentsController _documents;
    private readonly ActionHistory _history;
    private readonly IOperationRunner _runner;
    private readonly IObjectDialogs _dialogs;
    private readonly string _trashRoot;
    private readonly IAppLog _log;
    private readonly AppTexts _texts;
    private readonly TimeProvider _time;

    // Where each recorded step came from, so that an undo can find the tab again. Weak: a step that leaves the history is not kept alive here.
    private readonly ConditionalWeakTable<FileContentsStep, StepOrigin> _origins = new();

    // One apply at a time: the questions are modal, but the command may be asked again between two runs.
    private bool _applying;

    private sealed record StepOrigin(ObjectTabViewModel Tab, ObjectRef Object);

    private abstract record CheckOutcome;

    private sealed record CheckErrors(IReadOnlyList<BlockDiagnostic> Diagnostics) : CheckOutcome;

    private sealed record CheckNothing : CheckOutcome;

    private sealed record CheckPlanned(ObjectChanges Changes, ProjectSnapshot Snapshot, WritePlan Plan) : CheckOutcome;

    private sealed record CheckInput(ObjectStores Stores, ObjectBuildResult Result, string Text, string ProjectFolder, string ProjectName, string Item);

    public ObjectApplyController(ObjectsController objects, ProjectsController projects, DocumentsController documents, ActionHistory history,
        IOperationRunner runner, IObjectDialogs dialogs, string trashRoot, IAppLog log, AppTexts texts, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentException.ThrowIfNullOrWhiteSpace(trashRoot);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(time);
        _objects = objects;
        _projects = projects;
        _documents = documents;
        _history = history;
        _runner = runner;
        _dialogs = dialogs;
        _trashRoot = trashRoot;
        _log = log;
        _texts = texts;
        _time = time;
    }

    /// <summary>Raised after an apply changed the files of the project (the explorer lists them again).</summary>
    public event EventHandler<Guid>? FilesChanged;

    /// <summary>The apply of the tab (the ApplyCommand). Does nothing when the tab's ApplyDisabledReason is not null (logged).</summary>
    public async Task ApplyAsync(ObjectTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (_applying)
        {
            return;
        }

        _applying = true;
        try
        {
            await ApplyCoreAsync(tab);
        }
        finally
        {
            _applying = false;
            tab.RefreshApplyState();
        }
    }

    /// <summary>Asked before a tab with unapplied text moves on: Apply (applies; true when it worked), Discard (DiscardText, true), Cancel (false).</summary>
    public async Task<bool> LeaveAsync(ObjectTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!tab.HasUnappliedText)
        {
            return true;
        }

        switch (_dialogs.AskUnapplied(Shown(tab.Current.DisplayKey)))
        {
            case SaveChoice.Save:
                // A tab that cannot apply says why; leaving without the text being applied or discarded would lose it.
                if (tab.ApplyDisabledReason is { } reason)
                {
                    _dialogs.ShowMessage(_texts.ObjectsApplyErrorsTitle, reason);
                    return false;
                }

                await ApplyAsync(tab);
                return !tab.HasUnappliedText;
            case SaveChoice.Discard:
                tab.DiscardText();
                return true;
            default:
                return false;
        }
    }

    /// <summary>The tab a <see cref="FileContentsStep"/> came from, if it is still open and shows the same object; null otherwise.</summary>
    internal ObjectTabViewModel? TabOf(FileContentsStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return _origins.TryGetValue(step, out var origin) && _objects.ObjectTabs.Contains(origin.Tab) && origin.Tab.Current.Equals(origin.Object)
            ? origin.Tab
            : null;
    }

    /// <summary>The text of a failed write for the dialog: the kind and the file; for a failed rollback every file with its state.</summary>
    internal string FailureMessage(WriteFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var file = Shown(failure.File ?? string.Empty);
        var detail = Shown(failure.Detail);
        switch (failure.Kind)
        {
            case WriteFailureKind.FileChanged:
                return _texts.ObjectsApplyFileChanged(file);
            case WriteFailureKind.FileUnknown:
                return _texts.ObjectsApplyFileUnknown(file, detail);
            case WriteFailureKind.RollbackFailed:
                var states = failure.Files.Select(entry => _texts.ObjectsApplyFileState(Shown(entry.RelativeFile), _texts.ObjectsFileStateText(entry.State))).ToList();
                return _texts.ObjectsApplyRollbackFailed(detail) + "\n" + string.Join("\n", CappedLines.Of(states, _texts.ReportMoreLines));
            default:
                return _texts.ObjectsApplyWriteFailed(detail);
        }
    }

    private async Task ApplyCoreAsync(ObjectTabViewModel tab)
    {
        if (tab.ApplyDisabledReason is { } reason)
        {
            _log.Info("An apply of an object was refused: " + DisplayText.Safe(reason));
            return;
        }

        if (_objects.EnsureStores() is not { } stores || _projects.Active is not { IsUsable: true } project)
        {
            return;
        }

        // The load order is the one of the build: the snapshot and the diff belong to the baseline the text was edited from.
        var started = tab.Current;
        var result = tab.Result!;
        var input = new CheckInput(stores, result, tab.Text, project.Folder, project.DisplayName, Shown(started.DisplayKey));

        // Run A: parse, diff, snapshot, plan without answers.
        OperationOutcome<CheckOutcome>? checkRun = null;
        try
        {
            checkRun = await _runner.RunAsync(_texts.ObjectsApplyChecking, (progress, ct) => Check(input, progress, ct));
        }
        catch (Exception ex) when (IsEdgeFailure(ex))
        {
            _log.Error("Checking the text of an object could not be started.", ex);
        }
        finally
        {
            tab.RefreshApplyState();
        }

        if (!StillShows(tab, started, result))
        {
            _log.Info("An apply of an object was dropped: the tab moved on to another object while the text was checked.");
            return;
        }

        if (checkRun is not { Status: OperationStatus.Completed, Value: { } outcome })
        {
            _log.Info("An apply of an object ended without a result: " + (checkRun?.Status.ToString() ?? "not started") + ".");
            return;
        }

        switch (outcome)
        {
            case CheckErrors errors:
                var shownErrors = errors.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                _log.Warn($"The text of an object was refused: {shownErrors.Count} errors.");
                _dialogs.ShowMessage(_texts.ObjectsApplyErrorsTitle, ErrorsMessage(shownErrors));
                return;
            case CheckNothing:
                _log.Info("The text of an object has no change to write.");
                _dialogs.ShowMessage(_texts.ObjectsApply, _texts.ObjectsApplyResultNothing);
                return;
            case CheckPlanned planned:
                await WriteAsync(tab, started, result, project, stores, planned);
                return;
        }
    }

    private async Task WriteAsync(ObjectTabViewModel tab, ObjectRef started, ObjectBuildResult result, ProjectState project, ObjectStores stores, CheckPlanned planned)
    {
        var (changes, snapshot, plan) = (planned.Changes, planned.Snapshot, planned.Plan);
        var schema = stores.Schema.Schema!;
        if (plan.HasErrors)
        {
            ShowPlanErrors(plan);
            return;
        }

        // The questions: which file, and whether to copy a file of the game. Answered with a pure second plan on this thread.
        if (plan.HasQuestions)
        {
            var targetFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var question in plan.TargetQuestions)
            {
                var shown = question.Candidates.Select(candidate => MessageText.EscapeControlCharacters(candidate)).ToList();
                var chosen = _dialogs.AskTargetFile(Shown(question.Table), question.IsLoc, shown);
                var index = chosen is null ? -1 : shown.IndexOf(chosen);
                if (index < 0)
                {
                    _log.Info("An apply of an object was cancelled at the question for the target file.");
                    return;
                }

                targetFiles[question.IsLoc ? WriteAnswers.LocKey : question.Table] = question.Candidates[index];
            }

            var copiesConfirmed = false;
            if (plan.CopyQuestions.Count > 0)
            {
                var lines = plan.CopyQuestions
                    .Select(question => _texts.ObjectsCopyLine(Shown(question.RelativeFile), question.RowCount, string.Join(", ", question.DeletedKeys.Select(key => Shown(key)))))
                    .ToList();
                if (!_dialogs.AskCopies(lines))
                {
                    _log.Info("An apply of an object was cancelled at the question for the copy of a file.");
                    return;
                }

                copiesConfirmed = true;
            }

            plan = ProjectWritePlan.Compute(changes, snapshot, schema, stores.Graph, project.DisplayName, new WriteAnswers(targetFiles, copiesConfirmed));
            if (plan.HasErrors)
            {
                ShowPlanErrors(plan);
                return;
            }

            if (plan.HasQuestions)
            {
                _log.Error("The plan of an apply still has questions after they were answered.");
                _dialogs.ShowMessage(_texts.ObjectsApplyErrorsTitle, _texts.ObjectsApplyPlanErrorsMessage(1));
                return;
            }
        }

        // A dialog or a hook may have changed the world while it was open.
        if (!StillShows(tab, started, result) || _projects.Active?.Id != project.Id)
        {
            _log.Info("An apply of an object was dropped: the tab or the project changed while the questions were open.");
            return;
        }

        // A grid tab of a target file with unsaved work would lose it to the write.
        foreach (var file in plan.Files)
        {
            if (_documents.FirstTabWithUnsavedWork(_documents.TabsOfFile(project.Id, file.RelativeFile)) is { } unsaved)
            {
                _documents.Tabs.SelectedTab = unsaved;
                _log.Info("An apply of an object was refused: a grid tab of a target file has unsaved changes.");
                _dialogs.ShowMessage(_texts.ObjectsApplyErrorsTitle, _texts.ObjectsApplyGridUnsaved(Shown(unsaved.EntryPath)));
                return;
            }
        }

        // Run B: the write. Cancelling works only before the first file; a write that ran to its end counts even when the run reports a cancel.
        var description = "Apply: " + DisplayText.Safe(started.Table) + " " + DisplayText.Safe(started.DisplayKey);
        var finalPlan = plan;
        OperationOutcome<WriteOutcome>? writeRun = null;
        try
        {
            writeRun = await _runner.RunAsync(_texts.ObjectsApplyWriting, (progress, ct) =>
            {
                progress.Report(new OperationProgress(0, finalPlan.Files.Count, Shown(finalPlan.Files[0].RelativeFile)));
                return ProjectWriter.Apply(finalPlan, project.Id, project.Folder, description, _trashRoot, _time, ct);
            });
        }
        catch (Exception ex) when (IsEdgeFailure(ex))
        {
            _log.Error("Writing the text of an object could not be started.", ex);
        }
        finally
        {
            tab.RefreshApplyState();
        }

        if (writeRun?.Value is not { } outcome)
        {
            _log.Info("An apply of an object wrote nothing: " + (writeRun?.Status.ToString() ?? "not started") + ".");
            return;
        }

        if (outcome.Failure is { } failure)
        {
            _log.Error($"Applying an object failed: {failure.Kind}.");
            _dialogs.ShowMessage(_texts.ObjectsApplyFailedTitle, FailureMessage(failure));
            if (failure.Kind == WriteFailureKind.RollbackFailed)
            {
                // The disk is no longer the baseline the text was edited from.
                tab.MarkStale();
            }
            else if (failure.Kind is WriteFailureKind.FileChanged or WriteFailureKind.FileUnknown)
            {
                await _objects.CheckStaleAsync();
            }

            return;
        }

        var step = outcome.Step!;
        _history.Record(step);
        _origins.AddOrUpdate(step, new StepOrigin(tab, started));
        var summary = plan.Summary;
        _log.Info($"Applied {DisplayText.Safe(started.Table)} {DisplayText.Safe(started.DisplayKey)}: {summary.RowsSet + summary.RowsInserted + summary.RowsDeleted} rows, "
            + $"{summary.TextsSet + summary.TextsInserted} texts, {summary.FilesChanged} files changed, {summary.FilesCreated} files created, {summary.Copies} copies.");
        var created = plan.Files.Where(file => file.Kind != PlannedFileKind.Changed).Select(file => file.RelativeFile).ToList();
        tab.SetApplyResult(summary, created, changes.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning));
        FilesChanged?.Invoke(this, project.Id);
        await _documents.CheckExternalChangesAsync();

        // The disk changed, so the object is built again; its result replaces the text. A build that did not run (another operation) leaves the old
        // result, which no longer matches the disk.
        await _objects.BuildAsync(tab);
        if (ReferenceEquals(tab.Result, result))
        {
            tab.MarkStale();
        }
    }

    // Runs inside run A on a pool thread: nothing here touches the UI.
    private static CheckOutcome Check(CheckInput input, IProgress<OperationProgress> progress, CancellationToken ct)
    {
        var stores = input.Stores;
        var result = input.Result;
        var order = result.Order;
        var schema = stores.Schema.Schema!;
        progress.Report(new OperationProgress(0, 4, input.Item));
        var parsed = ObjectTextParser.Parse(input.Text, result.View, result.Category, stores.Graph);
        if (parsed.HasErrors)
        {
            return new CheckErrors(parsed.Diagnostics);
        }

        ct.ThrowIfCancellationRequested();
        progress.Report(new OperationProgress(1, 4, input.Item));
        var changes = ObjectDiff.Compute(result.View, parsed, result.Category, stores.Graph,
            (table, column, value) => stores.Tables.Get(table, order, ct).FindByField(column, value).Count > 0);
        if (changes.HasErrors)
        {
            return new CheckErrors(changes.Diagnostics);
        }

        if (changes.IsEmpty)
        {
            return new CheckNothing();
        }

        ct.ThrowIfCancellationRequested();
        progress.Report(new OperationProgress(2, 4, input.Item));
        var snapshot = ProjectSnapshotReader.Read(input.ProjectFolder, schema, SnapshotRequest.For(changes), path => stores.Tables.HasPackEntry(order, path, ct), ct);
        progress.Report(new OperationProgress(3, 4, input.Item));
        var plan = ProjectWritePlan.Compute(changes, snapshot, schema, stores.Graph, input.ProjectName, WriteAnswers.None);
        return new CheckPlanned(changes, snapshot, plan);
    }

    private string ErrorsMessage(IReadOnlyList<BlockDiagnostic> errors)
    {
        var lines = errors.Select(error => _texts.ObjectsApplyErrorLine(error.Line, _texts.DiagnosticText(error.Kind))).ToList();
        return _texts.ObjectsApplyErrorsMessage(errors.Count) + "\n" + string.Join("\n", CappedLines.Of(lines, _texts.ReportMoreLines));
    }

    private void ShowPlanErrors(WritePlan plan)
    {
        _log.Warn($"The plan of an apply was refused: {plan.Errors.Count} problems.");
        var lines = plan.Errors
            .Select(error => _texts.ObjectsApplyPlanErrorLine(Shown(error.RelativeFile ?? "-"), _texts.WriteErrorText(error.Kind) + " " + Shown(error.Detail)))
            .ToList();
        _dialogs.ShowMessage(_texts.ObjectsApplyErrorsTitle,
            _texts.ObjectsApplyPlanErrorsMessage(plan.Errors.Count) + "\n" + string.Join("\n", CappedLines.Of(lines, _texts.ReportMoreLines)));
    }

    // The tab still shows the object and the result the apply started from.
    private static bool StillShows(ObjectTabViewModel tab, ObjectRef started, ObjectBuildResult result) =>
        tab.Current.Equals(started) && ReferenceEquals(tab.Result, result);

    private string Shown(string text) => DisplayText.Safe(text, MaxDetailChars, " (" + _texts.TextShortened + ")");

    private static bool IsEdgeFailure(Exception ex) =>
        ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or Win32Exception;
}
