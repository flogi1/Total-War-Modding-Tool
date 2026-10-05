using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using TotalWarModTool.Editing;
using TotalWarModTool.Operations;
using TotalWarModTool.Projects;
using TotalWarModTool.Tables;

namespace TotalWarModTool.App;

// Saving the modified tabs and the guards for unsaved changes: closing a tab, closing the app, removing a project, building.
public sealed partial class DocumentsController
{
    // A project name in a message shows at most this many characters, as elsewhere.
    private const int MaxNameChars = 100;

    // The tabs that are being closed right now (the question is open or their save is running): a second request for one of them is ignored.
    private readonly HashSet<EditableRowsTabViewModel> _closing = [];
    private bool _closingApp;
    private bool _appCloseConfirmed;

    /// <summary>One file to write: the document and the folder are read on the UI thread, so the work on the pool thread touches no tab.</summary>
    private sealed record SaveItem(EditableRowsTabViewModel Tab, RowsDocument Document, string? Folder, string RelativeFile, string Name);

    private enum SaveFailure { None, ProjectGone, Refused, Write }

    private sealed record SaveResult(SaveItem Item, SaveFailure Failure, FileWriteResult? Write, string? Detail);

    /// <summary>Saves the selected tab. Disabled unless it is an editable tab with changes, and while an operation runs.</summary>
    public ICommand SaveCommand => _saveCommand;

    /// <summary>Saves every modified tab in one run. Disabled without a modified tab and while an operation runs.</summary>
    public ICommand SaveAllCommand => _saveAllCommand;

    // ---- commands --------------------------------------------------------------------------------------------------------------

    // The test of "may an operation start": the same for the commands, the menu tool tip and the direct calls.
    private bool CanStartSave() => !_runner.IsRunning && _projects.CanStartOperation;

    private bool CanSaveSelected() => CanStartSave() && _tabs.SelectedTab is EditableRowsTabViewModel { IsModified: true };

    private bool CanSaveAll() => CanStartSave() && HasModifiedTabs;

    private void SaveSelected()
    {
        // Also guarded here: a direct call must not start a run while another one is active.
        if (CanStartSave() && _tabs.SelectedTab is EditableRowsTabViewModel { IsModified: true } tab)
        {
            RunDetached(() => SaveAsync([tab]), "Saving a tab");
        }
    }

    private void SaveAllModified()
    {
        if (CanStartSave() && _tracked.Where(tab => tab.IsModified).ToList() is { Count: > 0 } modified)
        {
            RunDetached(() => SaveAsync(modified), "Saving all tabs");
        }
    }

    private void RaiseSaveCommandsChanged()
    {
        _saveCommand.RaiseCanExecuteChanged();
        _saveAllCommand.RaiseCanExecuteChanged();
    }

    // Starts work whose result nobody awaits (a command, a close request). async void on purpose: the exceptions that such work can meet at
    // the edge of the app (a dialog, the runner, the window) are logged here and shown to the user, so that a button never silently does
    // nothing. Anything else is a surprise and reaches the global handler.
    private async void RunDetached(Func<Task> work, string what)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (IsEdgeFailure(ex))
        {
            _log.Error($"{what} failed unexpectedly.", ex);
            TellFailure(ex);
        }
    }

    private static bool IsEdgeFailure(Exception ex) =>
        ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or Win32Exception;

    // A message that cannot be shown either (no window) is only logged: there is nobody left to tell.
    private void TellFailure(Exception failure)
    {
        try
        {
            _dialogs.ShowMessage(_texts.ActionFailedTitle, _texts.ActionFailedMessage(Shown(failure.Message, MaxDetailChars)));
        }
        catch (Exception ex) when (IsEdgeFailure(ex))
        {
            _log.Error("The message about the failure could not be shown.", ex);
        }
    }

    // ---- saving ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes the modified tabs among <paramref name="tabs"/> through the runner (one run, "file n / total", cancellable between two files) and
    /// marks every written one as saved. A tab that was not modified counts as saved and is not written. True when every tab was saved.
    /// Problems and a cancel are shown in one message; the tabs that were not written stay modified. Nothing runs while another operation does.
    /// </summary>
    public async Task<bool> SaveAsync(IReadOnlyList<EditableRowsTabViewModel> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        var modified = tabs.Where(tab => tab.IsModified).Distinct().ToList();
        if (modified.Count == 0)
        {
            return true;
        }

        // The runner throws when a run is active, so this is decided before it is called.
        if (!CanStartSave())
        {
            _dialogs.ShowMessage(_texts.SaveFailedTitle, _texts.ProjectBusy);
            return false;
        }

        var withProject = ListsSeveralProjects(modified);
        var items = modified.Select(tab => NewItem(tab, withProject)).ToList();
        var outcome = await _runner.RunAsync(_texts.OperationSaveFiles, (progress, cancellation) => WriteFiles(items, progress, cancellation));

        // A failed run without a value was reported by the runner already. A run that is reported as failed but has its value (its window
        // could not be shown) is used like a cancelled one: what was written, was written.
        if (outcome.Value is not { } results)
        {
            RaiseSaveCommandsChanged();
            if (outcome.Status == OperationStatus.Cancelled)
            {
                _dialogs.ShowMessage(_texts.SaveCancelledTitle, _texts.SaveCancelledMessage(0, items.Count));
            }

            return false;
        }

        var problems = new List<string>();
        var saved = 0;
        foreach (var result in results)
        {
            if (result.Failure == SaveFailure.None)
            {
                // The window was locked during the run, so the document is the one that was written; the check is for the case that it is not.
                result.Item.Document.MarkSaved();
                if (ReferenceEquals(result.Item.Tab.Document, result.Item.Document))
                {
                    result.Item.Tab.SavedFingerprint = result.Write!.Fingerprint;
                }

                saved++;
            }
            else
            {
                problems.Add(_texts.SaveProblemLine(result.Item.Name, ProblemText(result)));
            }
        }

        var incomplete = results.Count < items.Count;
        RaiseSaveCommandsChanged();
        if (problems.Count > 0)
        {
            var lines = CappedLines.Of(problems, _texts.ReportMoreLines);
            var message = _texts.SaveFailedSummary(problems.Count) + "\n\n" + string.Join("\n", lines);
            if (incomplete)
            {
                message += "\n\n" + _texts.SaveCancelledMessage(saved, items.Count);
            }

            _dialogs.ShowMessage(_texts.SaveFailedTitle, message);
        }
        else if (incomplete)
        {
            _dialogs.ShowMessage(_texts.SaveCancelledTitle, _texts.SaveCancelledMessage(saved, items.Count));
        }

        return problems.Count == 0 && !incomplete;
    }

    private SaveItem NewItem(EditableRowsTabViewModel tab, bool withProject)
    {
        var folder = _projects.Projects.FirstOrDefault(state => state.Id == tab.ProjectId)?.Folder;
        return new SaveItem(tab, tab.Document, folder, tab.RelativeFile, DisplayName(tab, withProject));
    }

    // Runs on a pool thread and touches only what the items hold. The progress of file n is reported before the token is looked at, so that
    // a cancel at that point leaves file n unwritten; no exception is thrown for it, because the result of the files already written must
    // not be lost.
    private static List<SaveResult> WriteFiles(IReadOnlyList<SaveItem> items, IProgress<OperationProgress> progress, CancellationToken cancellation)
    {
        var results = new List<SaveResult>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            progress.Report(new OperationProgress(i + 1, items.Count, items[i].Name));
            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            results.Add(WriteOne(items[i]));
        }

        return results;
    }

    private static SaveResult WriteOne(SaveItem item)
    {
        if (item.Folder is null)
        {
            return new SaveResult(item, SaveFailure.ProjectGone, null, null);
        }

        TsvWriteResult text;
        try
        {
            text = item.Document.ToText();
        }
        catch (ArgumentException ex)
        {
            // A pack path the dialect cannot carry: the same case as a refusal.
            return new SaveResult(item, SaveFailure.Refused, null, ex.Message.ReplaceLineEndings(" "));
        }

        if (text.Text is null)
        {
            return new SaveResult(item, SaveFailure.Refused, null, text.Detail);
        }

        var write = ProjectFileWriter.WriteText(item.Folder, item.RelativeFile, text.Text);
        return new SaveResult(item, write.Problem == FileWriteProblem.None ? SaveFailure.None : SaveFailure.Write, write, null);
    }

    // One text per problem, with the technical detail of the library behind it (escaped and cut, it is data).
    private string ProblemText(SaveResult result)
    {
        string text;
        string? detail = null;
        switch (result.Failure)
        {
            case SaveFailure.ProjectGone:
                text = _texts.SaveProblemProjectGone;
                break;
            case SaveFailure.Refused:
                text = _texts.SaveProblemRefused;
                detail = result.Detail;
                break;
            default:
                text = _texts.SaveProblemText(result.Write!.Problem);
                detail = result.Write.Detail;
                break;
        }

        return string.IsNullOrEmpty(detail) ? text : _texts.SaveProblemWithDetail(text, Shown(detail, MaxDetailChars));
    }

    // ---- the guards ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Asks what to do with the unsaved changes among <paramref name="tabs"/>; no modified tab: true without asking. Save: saves them, true when
    /// all were saved. Discard: true, nothing is written. Cancel: false.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync(IReadOnlyList<EditableRowsTabViewModel> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        var modified = tabs.Where(tab => tab.IsModified).Distinct().ToList();
        if (modified.Count == 0)
        {
            return true;
        }

        switch (_dialogs.AskUnsaved(DisplayNames(modified)))
        {
            case SaveChoice.Save:
                return await SaveAsync(modified);
            case SaveChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    // The hook of the tab list: an unmodified tab (or any other kind) closes at once; a modified one is never closed by this call. The question
    // and the save are asynchronous, so they start here and the tab is closed when they agree.
    private bool CanCloseTab(TabViewModel tab)
    {
        if (tab is not EditableRowsTabViewModel { IsModified: true } editable)
        {
            return true;
        }

        if (_closing.Add(editable))
        {
            RunDetached(() => ConfirmAndCloseAsync(editable), "Closing a tab");
        }

        return false;
    }

    private async Task ConfirmAndCloseAsync(EditableRowsTabViewModel tab)
    {
        try
        {
            if (await ConfirmCloseAsync([tab]))
            {
                _tabs.CloseWithoutAsking(tab);
            }
        }
        finally
        {
            _closing.Remove(tab);
        }
    }

    /// <summary>
    /// The decision of the main window's <c>Closing</c> event. True: the window may close now (no modified tab, or the confirmation already
    /// succeeded; that is consumed once). False: the close is refused, and either an operation runs (nothing else happens), a question is
    /// open already, or the question is started now for all modified tabs together and <paramref name="close"/> is called when it ended with
    /// "go on". <paramref name="close"/> must not close the window before the event has returned (the window defers it).
    /// </summary>
    public bool RequestAppClose(Action close)
    {
        ArgumentNullException.ThrowIfNull(close);
        if (_runner.IsRunning)
        {
            return false;
        }

        if (_appCloseConfirmed)
        {
            _appCloseConfirmed = false;
            return true;
        }

        if (!HasModifiedTabs)
        {
            return true;
        }

        if (!_closingApp)
        {
            _closingApp = true;
            RunDetached(() => ConfirmAppCloseAsync(close), "Closing the app");
        }

        return false;
    }

    private async Task ConfirmAppCloseAsync(Action close)
    {
        try
        {
            if (!await ConfirmCloseAsync(EditableTabs))
            {
                return;
            }
        }
        finally
        {
            _closingApp = false;
        }

        // Set before the window is closed again: that Closing event must not ask a second time.
        _appCloseConfirmed = true;
        close();
    }

    /// <summary>The reason why a project cannot leave the list right now: it has tabs with unsaved changes. Null when it can.</summary>
    public string? UnsavedReason(Guid projectId) =>
        _tracked.Any(tab => tab.ProjectId == projectId && tab.IsModified) ? _texts.ProjectRemoveUnsaved : null;

    /// <summary>
    /// Asked before a build of the project: "save all and build" for its modified tabs (none: true without asking). True when the build may
    /// go on, which is when the tabs were saved. No (or a save that did not work) is false: the build must not start.
    /// </summary>
    public async Task<bool> ConfirmSaveBeforeBuildAsync(Guid projectId)
    {
        var modified = _tracked.Where(tab => tab.ProjectId == projectId && tab.IsModified).ToList();
        if (modified.Count == 0)
        {
            return true;
        }

        return _dialogs.AskSaveAllBeforeBuild(DisplayNames(modified)) && await SaveAsync(modified);
    }

    // ---- names in questions and messages ---------------------------------------------------------------------------------------

    private List<string> DisplayNames(IReadOnlyList<EditableRowsTabViewModel> tabs)
    {
        var withProject = ListsSeveralProjects(tabs);
        return tabs.Select(tab => DisplayName(tab, withProject)).ToList();
    }

    private static bool ListsSeveralProjects(IEnumerable<EditableRowsTabViewModel> tabs) => tabs.Select(tab => tab.ProjectId).Distinct().Skip(1).Any();

    // The path of the file as the user knows it from the tab, escaped; with the name of the project in front when files of several projects are listed.
    private string DisplayName(EditableRowsTabViewModel tab, bool withProject)
    {
        var file = Shown(tab.EntryPath);
        if (withProject && _projects.Projects.FirstOrDefault(state => state.Id == tab.ProjectId) is { } project)
        {
            return Shown(project.DisplayName, MaxNameChars) + ": " + file;
        }

        return file;
    }

    // Foreign text (a path, a technical detail) as the UI shows it: escaped and cut with the marker of the current language.
    private string Shown(string text, int maxChars = 300) => DisplayText.Safe(text, maxChars, " (" + _texts.TextShortened + ")");
}
