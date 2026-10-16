using System.Threading;
using System.Threading.Tasks;
using TotalWarModTool.Editing;
using TotalWarModTool.Objects;
using TotalWarModTool.Operations;

namespace TotalWarModTool.App;

// "Copy as" (spec 5.8): the object of a tab written under a new key into the active project, through the write path of the apply.
public sealed partial class ObjectApplyController
{
    // The subject of the log lines of a copy.
    private const string CopySubject = "A copy of an object";

    /// <summary>What a written copy delivered: the recorded step and its plan, the notes of the copy.</summary>
    internal sealed record CopyDone(FileContentsStep Step, WritePlan Plan, IReadOnlyList<CopyNote> Notes);

    private abstract record CopyOutcome;

    private sealed record CopyErrors(IReadOnlyList<BlockDiagnostic> Diagnostics) : CopyOutcome;

    private sealed record CopyTaken(IReadOnlyList<TakenKey> Keys) : CopyOutcome;

    private sealed record CopyNothing : CopyOutcome;

    private sealed record CopyPlanned(IReadOnlyList<CopyNote> Notes, ObjectChanges Changes, ProjectSnapshot Snapshot, WritePlan Plan) : CopyOutcome;

    /// <summary>"Copy as" of the tab (the CopyCommand). Does nothing while another write runs or when the tab's CopyDisabledReason is not null (logged).</summary>
    public async Task CopyAsync(ObjectTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (CopyRefusal(tab) is { } refusal)
        {
            _log.Info("A copy of an object was refused: " + refusal);
            return;
        }

        // Unapplied text is asked about first. An apply from that question is a write of its own, so the guard is taken only after it.
        if (!await LeaveAsync(tab))
        {
            _log.Info("A copy of an object was cancelled at the question about unapplied text.");
            return;
        }

        if (CopyRefusal(tab) is { } after)
        {
            _log.Info("A copy of an object was refused after the question about unapplied text: " + after);
            return;
        }

        _applying = true;
        try
        {
            await CopyCoreAsync(tab);
        }
        finally
        {
            _applying = false;
            tab.RefreshApplyState();
        }
    }

    /// <summary>
    /// Run A ("objects.copyAs.checking"): copy(stores, order, ct), ObjectCopy.Changes (no reference checks), errors, TakenKeys, snapshot, plan; then the
    /// shared write path (questions, second plan, grid lock, run B, history Record) and FilesChanged. Shows every refusal itself; null when nothing was
    /// written. stillValid is asked after the questions, together with "the active project is still the one passed".
    /// </summary>
    internal async Task<CopyDone?> WriteCopyAsync(Func<ObjectStores, LoadOrder, CancellationToken, CopyResult> copy, LoadOrder order, ProjectState project,
        ObjectStores stores, string description, string item, Func<bool> stillValid)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(stores);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(stillValid);

        OperationOutcome<CopyOutcome>? checkRun = null;
        try
        {
            checkRun = await _runner.RunAsync(_texts.ObjectsCopyAsChecking,
                (progress, ct) => CheckCopy(copy, order, stores, project.Folder, project.DisplayName, item, progress, ct));
        }
        catch (Exception ex) when (IsEdgeFailure(ex))
        {
            _log.Error("Preparing a copy of an object could not be started.", ex);
        }

        if (checkRun is not { Status: OperationStatus.Completed, Value: { } outcome })
        {
            _log.Info("A copy of an object ended without a result: " + (checkRun?.Status.ToString() ?? "not started") + ".");
            return null;
        }

        switch (outcome)
        {
            case CopyErrors errors:
                var shownErrors = errors.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                _log.Warn($"A copy of an object was refused: {shownErrors.Count} errors.");
                _dialogs.ShowMessage(_texts.ObjectsApplyErrorsTitle, ErrorsMessage(shownErrors));
                return null;
            case CopyNothing:
                _log.Info("A copy of an object was dropped: the copy has no change to write.");
                _dialogs.ShowMessage(_texts.ObjectsCopyAs, _texts.ObjectsApplyResultNothing);
                return null;
            case CopyTaken taken:
                _log.Warn($"A copy of an object was refused: {taken.Keys.Count} of its keys exist already.");
                var lines = taken.Keys.Select(key => Shown(key.Table) + ": " + Shown(string.Join(" | ", key.KeyValues))).ToList();
                _dialogs.ShowMessage(_texts.ObjectsCopyAsTakenTitle,
                    _texts.ObjectsCopyAsTakenMessage(taken.Keys.Count) + "\n" + string.Join("\n", CappedLines.Of(lines, _texts.ReportMoreLines)));
                return null;
            case CopyPlanned planned:
                if (await WritePlannedAsync(planned.Changes, planned.Snapshot, planned.Plan, project, stores, description, stillValid, null, CopySubject)
                    is not { } written)
                {
                    return null;
                }

                FilesChanged?.Invoke(this, project.Id);
                return new CopyDone(written.Step, written.Plan, planned.Notes);
            default:
                return null;
        }
    }

    // Why no copy can start now: another write runs, or the tab says why not. Null when it can.
    private string? CopyRefusal(ObjectTabViewModel tab) =>
        _applying ? "another write runs." : tab.CopyDisabledReason is { } reason ? DisplayText.Safe(reason) : null;

    private async Task CopyCoreAsync(ObjectTabViewModel tab)
    {
        if (_objects.EnsureStores() is not { } stores)
        {
            _log.Info("A copy of an object was dropped: there are no stores (no game or no schema).");
            return;
        }

        if (_projects.Active is not { IsUsable: true } project)
        {
            _log.Info("A copy of an object was dropped: there is no active usable project.");
            return;
        }

        // Taken after the question about unapplied text: an apply from it built the tab again. The load order is the one of the build.
        var started = tab.Current;
        var result = tab.Result!;
        var oldKey = started.KeyValues[0];

        // The table of the root was read by the build and is in the cache; the dialog asks it on every keystroke.
        var merged = stores.Tables.Get(started.Table, result.Order, CancellationToken.None);
        string? ExistsIn(string key) => merged.FindByKey([key]) is { Count: > 0 } rows ? rows[0].Provenance.Source.Name : null;
        var newKey = _dialogs.AskNewKey(Shown(started.DisplayKey), oldKey + "_copy", key => ObjectKeyRules.Problem(_texts, key, ExistsIn));
        if (newKey is null)
        {
            _log.Info("A copy of an object was cancelled at the question for the new key.");
            return;
        }

        var newId = new ObjectId(started.Table, [newKey]);
        var description = "Copy: " + DisplayText.Safe(started.Table) + " " + DisplayText.Safe(oldKey) + " as " + DisplayText.Safe(newKey);
        var done = await WriteCopyAsync(
            (s, order, ct) => ObjectCopy.Rename(result.View, newId, id => s.Builder.Build(id, order, null, ct), _objects.CategoryOf, s.Graph,
                _objects.IntegerKeys(s, order, ct)),
            result.Order, project, stores, description, Shown(started.DisplayKey), () => StillShows(tab, started, result));
        if (done is null)
        {
            return;
        }

        // The copy opens in the same tab (the history grows) and is built through the build handler. The origin is the entry after the navigation,
        // with its root pack, so that an undo finds the tab again.
        tab.NavigateTo(newId, newTab: false);
        await FinishCopyAsync(done, tab, tab.Current,
            $"Copied {DisplayText.Safe(started.Table)} {DisplayText.Safe(oldKey)} as {DisplayText.Safe(newKey)}");
    }

    /// <summary>
    /// The tail of every written copy ("Copy as" and the wizards), on the UI thread once the tab shows the new object: remembers the origin of the
    /// step (shown is the entry of the tab that an undo finds again), logs "what" with the counts of the plan, puts the result line and the notes
    /// on the tab and checks the project after the write.
    /// </summary>
    internal async Task FinishCopyAsync(CopyDone done, ObjectTabViewModel tab, ObjectRef shown, string what)
    {
        ArgumentNullException.ThrowIfNull(done);
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(what);
        RememberOrigin(done.Step, tab, shown);
        var summary = done.Plan.Summary;
        _log.Info($"{what}: {summary.RowsInserted} rows, {summary.TextsInserted} texts, {summary.FilesChanged} files changed, "
            + $"{summary.FilesCreated} files created, {done.Notes.Count} notes.");
        tab.SetApplyResult(summary, CreatedFiles(done.Plan), 0, done.Notes);
        await CheckAfterWriteAsync();
    }

    // Runs inside run A of a copy on a pool thread: nothing here touches the UI (the categories of the objects controller are read-only).
    private CopyOutcome CheckCopy(Func<ObjectStores, LoadOrder, CancellationToken, CopyResult> copy, LoadOrder order, ObjectStores stores, string projectFolder,
        string projectName, string item, IProgress<OperationProgress> progress, CancellationToken ct)
    {
        var schema = stores.Schema.Schema!;
        progress.Report(new OperationProgress(0, 4, item));
        var copied = copy(stores, order, ct);
        ct.ThrowIfCancellationRequested();
        progress.Report(new OperationProgress(1, 4, item));

        // No reference checks: the targets inside the copy do not exist yet.
        var changes = ObjectCopy.Changes(copied, order, _objects.CategoryOf, stores.Graph, null);
        if (changes.HasErrors)
        {
            return new CopyErrors(changes.Diagnostics);
        }

        if (changes.IsEmpty)
        {
            return new CopyNothing();
        }

        var taken = ObjectCopy.TakenKeys(changes, stores.Graph, (table, keyValues) => stores.Tables.Get(table, order, ct).FindByKey(keyValues).Count > 0);
        if (taken.Count > 0)
        {
            return new CopyTaken(taken);
        }

        ct.ThrowIfCancellationRequested();
        progress.Report(new OperationProgress(2, 4, item));
        var snapshot = ProjectSnapshotReader.Read(projectFolder, schema, SnapshotRequest.For(changes), path => stores.Tables.HasPackEntry(order, path, ct), ct);
        progress.Report(new OperationProgress(3, 4, item));
        var plan = ProjectWritePlan.Compute(changes, snapshot, schema, stores.Graph, projectName, WriteAnswers.None);
        return new CopyPlanned(copied.Notes, changes, snapshot, plan);
    }
}
