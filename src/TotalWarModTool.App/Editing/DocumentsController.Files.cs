using TotalWarModTool.Projects;

namespace TotalWarModTool.App;

// The tabs of project files that the file operations of a project (delete, rename and the undo and redo of their steps) take away.
public sealed partial class DocumentsController
{
    // The message of a failed file step lists at most this many trash paths.
    private const int MaxTrashLines = 10;

    /// <summary>
    /// Raised after the undo or redo of a <see cref="FileStep"/> ran, with the id of its project: the files of the project changed. Not
    /// raised when the step failed.
    /// </summary>
    public event EventHandler<Guid>? FileStepApplied;

    /// <summary>
    /// Raised after the undo or redo of a <see cref="FileStep"/> closed the tab of a file that the step renamed: the file should be opened
    /// again under the name it has now (<c>RelativeFile</c>, with '\').
    /// </summary>
    public event EventHandler<(Guid ProjectId, string RelativeFile)>? FileReopenRequested;

    /// <summary>What the undo or redo of a <see cref="FileStep"/> does to the open tabs: the tabs to close and the file to open instead.</summary>
    private sealed record FileStepPlan(FileStep Step, IReadOnlyList<TabViewModel> Tabs, string? Reopen);

    /// <summary>
    /// The open tabs of any kind that show the file of a project (an editable table, a read-only table, a hint). <paramref name="relativeFile"/>
    /// is compared ignoring case and with '\' and '/' taken as the same separator, as the entry path of a tab uses '/'.
    /// </summary>
    public IReadOnlyList<TabViewModel> TabsOfFile(Guid projectId, string relativeFile)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);
        return _tabs.Tabs.Where(tab => TabSource.TryGetProjectId(tab.PackPath, out var id) && id == projectId && SameFile(tab.EntryPath, relativeFile)).ToList();
    }

    /// <summary>
    /// The first tab among <paramref name="tabs"/> that holds work a file operation would lose: an editable tab that differs from its last
    /// save, or one with a cell that is being edited. Null when there is none. Nothing is committed or asked.
    /// </summary>
    public TabViewModel? FirstTabWithUnsavedWork(IEnumerable<TabViewModel> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        return tabs.FirstOrDefault(tab => tab is EditableRowsTabViewModel editable && HasSomethingToSave(editable));
    }

    /// <summary>Closes the tabs without asking: the file they show is gone or has another name. The history forgets the steps of their documents.</summary>
    public void CloseTabsOfFile(IEnumerable<TabViewModel> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        foreach (var tab in tabs.ToList())
        {
            _tabs.CloseWithoutAsking(tab);
        }
    }

    private static bool SameFile(string a, string b) =>
        string.Equals(a.Replace('/', '\\'), b.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);

    private static bool SameSpelling(string a, string b) =>
        string.Equals(a.Replace('/', '\\'), b.Replace('/', '\\'), StringComparison.Ordinal);

    // The tabs of the files that the step removes in this direction (the files it has afterwards are not among them), and the file that it puts
    // in their place. Null when the step cannot run because a tab holds unsaved work: the message is shown and the tab selected.
    private FileStepPlan? PlanFileStep(FileStep step, bool undo)
    {
        var before = undo ? step.FilesAfterDo : step.FilesAfterUndo;
        var after = undo ? step.FilesAfterUndo : step.FilesAfterDo;
        // The lists are compared exactly (a rename of the case only counts as a rename: the tab shows the old spelling); the tabs are found
        // ignoring case.
        var removed = before.Where(file => !after.Any(kept => SameSpelling(kept, file)));
        var added = after.FirstOrDefault(file => !before.Any(had => SameSpelling(had, file)));
        var tabs = removed.SelectMany(file => TabsOfFile(step.ProjectId, file)).Distinct().ToList();

        if (FirstTabWithUnsavedWork(tabs) is { } unsaved)
        {
            _tabs.SelectedTab = unsaved;
            _dialogs.ShowMessage(undo ? _texts.EditUndoBlockedTitle : _texts.EditRedoBlockedTitle, _texts.FileTabUnsaved(Shown(unsaved.EntryPath)));
            return null;
        }

        return new FileStepPlan(step, tabs, tabs.Count > 0 ? added : null);
    }

    // The file a step was working on when it could not run: the file that should have been there before the run (the step of a delete that
    // is undone has none in the project: it brings its file back). Shown to the user, so it is the one name that says what went wrong.
    private static string? WorkingFileOf(FileStep step, bool undo)
    {
        var before = undo ? step.FilesAfterDo : step.FilesAfterUndo;
        var other = undo ? step.FilesAfterUndo : step.FilesAfterDo;
        return before.Concat(other).FirstOrDefault();
    }

    // A file step was dropped because it could not run: the disk is not what the steps of that file describe any more (another program wrote
    // the file, the trash was emptied). The other file steps of the same project that deal with the same file, or with a name it had before
    // or after (a rename chain), stay on the stacks otherwise; one of them could rename or trash a file of another program. They are removed
    // too, and the files that they hold in the trash are logged, as nothing brings them back. Returns the removed steps.
    private List<FileStep> DropRelatedFileSteps(FileStep dropped)
    {
        var files = dropped.FilesAfterDo.Concat(dropped.FilesAfterUndo).ToList();
        var removedSteps = new List<FileStep>();
        while (true)
        {
            // The match runs once per step; the files it learns count from the next pass on, so a chain is followed pass by pass.
            var round = new List<FileStep>();
            _history.RemoveWhere(step =>
            {
                if (step is not FileStep related || related.ProjectId != dropped.ProjectId
                    || !related.FilesAfterDo.Concat(related.FilesAfterUndo).Any(file => files.Any(known => SameFile(known, file))))
                {
                    return false;
                }

                round.Add(related);
                return true;
            });

            if (round.Count == 0)
            {
                return removedSteps;
            }

            removedSteps.AddRange(round);
            foreach (var step in round)
            {
                files.AddRange(step.FilesAfterDo);
                files.AddRange(step.FilesAfterUndo);
                var where = step.InTrash is { } trashed ? $" Its file stays in the trash: {DisplayText.Safe(trashed.TrashFile)}" : string.Empty;
                _log.Warn($"The step \"{DisplayText.Safe(step.Description)}\" was removed from the history, because a step of the same file could not run.{where}");
            }
        }
    }

    // The places of the files that removed steps leave in the trash, for the message of a failed step: a heading and one path per line, never
    // cut (the user needs the whole path to fetch the file). At most MaxTrashLines paths; the rest is counted, and all of them are in the log.
    private string TrashLines(IReadOnlyList<string> inTrash)
    {
        if (inTrash.Count == 0)
        {
            return string.Empty;
        }

        var lines = inTrash.Take(MaxTrashLines).Select(MessageText.EscapeControlCharacters).ToList();
        if (inTrash.Count > MaxTrashLines)
        {
            lines.Add(_texts.FileStepMoreTrashFiles(inTrash.Count - MaxTrashLines));
        }

        return "\n\n" + _texts.FileStepTrashFiles + "\n" + string.Join("\n", lines);
    }

    // After the step ran: the tabs of the files it took away are closed (a file that still exists keeps its tabs), the project is announced,
    // and a file that was open under another name is asked for again. Undo and redo only wait for the runner, while opening a file also waits
    // for a silent run of the workspace: when no operation can start now, the request is raised all the same (a receiver that can open it
    // later may), and the log names the file, so that a tab that went is never lost without a word.
    private void ApplyFileStep(FileStepPlan plan)
    {
        CloseTabsOfFile(plan.Tabs);
        FileStepApplied?.Invoke(this, plan.Step.ProjectId);
        if (plan.Reopen is { } file)
        {
            if (!_projects.CanStartOperation)
            {
                _log.Warn($"The tab of a renamed file was closed and cannot be opened again right now, because another operation runs. Open \"{DisplayText.Safe(file)}\" from the explorer.");
            }

            FileReopenRequested?.Invoke(this, (plan.Step.ProjectId, file));
        }
    }
}
