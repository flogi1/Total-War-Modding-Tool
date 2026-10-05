using TotalWarModTool.Projects;

namespace TotalWarModTool.App;

// The tabs of project files that the file operations of a project (delete, rename and the undo and redo of their steps) take away.
public sealed partial class DocumentsController
{
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

    // After the step ran: the tabs of the files it took away are closed (a file that still exists keeps its tabs), the project is announced,
    // and a file that was open under another name is asked for again.
    private void ApplyFileStep(FileStepPlan plan)
    {
        CloseTabsOfFile(plan.Tabs);
        FileStepApplied?.Invoke(this, plan.Step.ProjectId);
        if (plan.Reopen is { } file)
        {
            FileReopenRequested?.Invoke(this, (plan.Step.ProjectId, file));
        }
    }
}
