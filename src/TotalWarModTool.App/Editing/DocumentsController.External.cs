using System.IO;
using TotalWarModTool.Editing;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

// Changes that another program makes to the files of open project tabs: the fingerprint of every project file tab, the silent check when the
// window is activated, the stale mark, the reload, and the state that saving compares against (see DocumentsController.Saving.cs).
public sealed partial class DocumentsController
{
    // What a tab knows about its file: the state the tab was read in (at open or reload) or written in (at save). Null means that nothing
    // trustworthy is known: the file did not exist at open, it changed between the read and the fingerprint, or it could not be asked.
    // A tab that is not in here is not followed (its project left the list, or it is no project tab).
    private readonly Dictionary<TabViewModel, FileFingerprint?> _known = new(ReferenceEqualityComparer.Instance);

    // What CreateEditableTab found out about an entry that is being opened; taken by Track when its tab appears in the list, which is right
    // after (one call of TabsViewModel.Add or Replace), and cleared by every SyncTabs.
    private readonly Dictionary<string, Opening> _pending = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<TabViewModel> _reloading = new(ReferenceEqualityComparer.Instance);
    private bool _checking;

    /// <summary>The fingerprint to remember for a file and the mark it starts with.</summary>
    private sealed record Opening(FileFingerprint? Known, OutsideChange Mark);

    /// <summary>The file of one tab, read on the UI thread so that the work on the pool thread touches no tab.</summary>
    private sealed record CheckItem(TabViewModel Tab, string Folder, string RelativeFile, FileFingerprint Known);

    private sealed record CheckResult(CheckItem Item, FileProbe Probe);

    /// <summary>The fingerprint a tab holds for its file (see the comment of the store); null when nothing is known or the tab is not followed. For tests.</summary>
    internal FileFingerprint? KnownFingerprint(TabViewModel tab) => _known.TryGetValue(tab, out var fingerprint) ? fingerprint : null;

    // ---- the fingerprint of a tab ----------------------------------------------------------------------------------------------------

    private string? FolderOf(Guid projectId) => _projects.Projects.FirstOrDefault(state => state.Id == projectId)?.Folder;

    private static string PendingKey(string sourceKey, string entryPath) => sourceKey + "\n" + entryPath;

    private static string RelativeFileOf(TabViewModel tab) => tab.EntryPath.Replace('/', '\\');

    // Present (with fingerprint), missing, or unknown (access error, a link on the path, an unsafe path): never throws for the file system.
    // Safe on any thread.
    private static FileProbe TryProbe(string folder, string relativeFile)
    {
        try
        {
            return ProjectFileWriter.Probe(folder, relativeFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new FileProbe(FileProbeState.Unknown, null, "the file cannot be asked: " + ex.Message);
        }
    }

    // The fingerprint of the file; null when it is missing or cannot be told (nothing trustworthy is known then).
    private static FileFingerprint? TryFingerprint(string folder, string relativeFile) => TryProbe(folder, relativeFile).Fingerprint;

    // What to remember for an entry that was just read, and the mark it starts with. Null when the project is not in the list.
    private Opening? DescribeOpening(Guid projectId, OpenedEntry entry)
    {
        if (FolderOf(projectId) is not { } folder)
        {
            return null;
        }

        var probe = TryProbe(folder, entry.EntryPath.Replace('/', '\\'));
        var fingerprint = probe.Fingerprint;
        if (entry.ReadProblem != EntryProblem.None)
        {
            return new Opening(fingerprint, OutsideChange.None);
        }

        // The file is gone although it was just read (the open and this check are not atomic). A file that cannot be asked is not "gone".
        if (probe.State == FileProbeState.Missing)
        {
            return new Opening(null, OutsideChange.Deleted);
        }

        if (fingerprint is null)
        {
            return new Opening(null, OutsideChange.None);
        }

        // A file that was read has the size of what was read; another size means that it changed between the read and the fingerprint, so the
        // tab does not show what is on disk. Nothing is remembered then: a save asks first, and the check has nothing to compare with.
        var wasRead = entry.Table is not null || entry.Loc is not null || entry.Text is not null;
        return wasRead && fingerprint.Length != entry.UncompressedSize
            ? new Opening(null, OutsideChange.Changed)
            : new Opening(fingerprint, OutsideChange.None);
    }

    private void NoteOpening(Guid projectId, TabSource source, OpenedEntry entry)
    {
        if (DescribeOpening(projectId, entry) is { } opening)
        {
            _pending[PendingKey(source.Key, entry.EntryPath)] = opening;
        }
    }

    private static bool IsProjectTab(TabViewModel tab) => TabSource.TryGetProjectId(tab.PackPath, out _);

    // Follows the project tabs of the list: tabs that left are forgotten, tabs that came get their fingerprint (the one noted when they were
    // opened, or the current one for a tab that was put into the list without being opened here).
    private void SyncFingerprints()
    {
        var projectTabs = _tabs.Tabs.Where(IsProjectTab).ToList();
        foreach (var gone in _known.Keys.Where(tab => !projectTabs.Contains(tab)).ToList())
        {
            _known.Remove(gone);
        }

        foreach (var added in projectTabs.Where(tab => !_known.ContainsKey(tab)))
        {
            Track(added);
        }

        _pending.Clear();
    }

    private void Track(TabViewModel tab)
    {
        TabSource.TryGetProjectId(tab.PackPath, out var projectId);
        if (FolderOf(projectId) is not { } folder)
        {
            return;
        }

        if (_pending.TryGetValue(PendingKey(tab.PackPath, tab.EntryPath), out var opening))
        {
            _known[tab] = opening.Known;
            if (opening.Mark != OutsideChange.None)
            {
                tab.SetOutsideChange(opening.Mark);
            }

            return;
        }

        _known[tab] = TryFingerprint(folder, RelativeFileOf(tab));
    }

    // ---- the silent check ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Compares the fingerprint of every open project file tab (editable or not) with the one taken when it was opened or saved. Silent, on a
    /// pool thread; marks changed and missing files as stale. Called when the window is activated and after F5. Does nothing while an
    /// operation runs or another check is running. Never throws: a file that cannot be asked is "cannot tell" and marks nothing.
    /// </summary>
    public async Task CheckExternalChangesAsync()
    {
        if (_checking || _runner.IsRunning)
        {
            return;
        }

        _checking = true;
        try
        {
            var items = new List<CheckItem>();
            foreach (var tab in _tabs.Tabs)
            {
                // Without a known fingerprint there is nothing to compare with (the tab is marked already, or it is not followed).
                if (TabSource.TryGetProjectId(tab.PackPath, out var projectId) && _known.TryGetValue(tab, out var known) && known is not null
                    && FolderOf(projectId) is { } folder)
                {
                    items.Add(new CheckItem(tab, folder, RelativeFileOf(tab), known));
                }
            }

            if (items.Count == 0)
            {
                return;
            }

            var results = await Task.Run(() => items.Select(ProbeItem).ToList());
            foreach (var result in results)
            {
                Apply(result);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // Not worth a message: the next activation looks again.
            _log.Warn($"The check for outside changes failed: {DisplayText.Safe(ex.Message)}");
        }
        finally
        {
            _checking = false;
        }
    }

    // Runs on a pool thread and touches only what the item holds.
    private static CheckResult ProbeItem(CheckItem item) => new(item, TryProbe(item.Folder, item.RelativeFile));

    // A tab that left the list or got another fingerprint meanwhile (saved, reloaded) is skipped: the probe is about a file state it no longer has.
    // A file that cannot be told (access error, a link on the path) marks nothing: "cannot tell" is not "deleted".
    private void Apply(CheckResult result)
    {
        var tab = result.Item.Tab;
        if (result.Probe.State == FileProbeState.Unknown || !_tabs.Tabs.Contains(tab) || !_known.TryGetValue(tab, out var known)
            || !Equals(known, result.Item.Known))
        {
            return;
        }

        if (result.Probe.State == FileProbeState.Missing)
        {
            tab.SetOutsideChange(OutsideChange.Deleted);
        }
        else if (!Equals(result.Probe.Fingerprint, known))
        {
            tab.SetOutsideChange(OutsideChange.Changed);
        }
    }

    // ---- reload ----------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the file again and replaces the content of the tab; the steps of the old document leave the history. An editable result keeps the
    /// tab (<see cref="EditableRowsTabViewModel.ReplaceDocument"/>); anything else (the file is gone, or no longer a table) replaces the tab at
    /// the same place by the tab that <see cref="TabsViewModel"/> builds for the result. The window is locked while the file is read.
    /// Only an editable tab that is stale for an outside reason and has no changes of its own is reloaded (the condition of the reload
    /// button), and it is looked at again after the file was read: a tab that was modified meanwhile keeps its content. Nothing happens
    /// otherwise, for a tab that is not open or not of a project, and while another reload of it is running. A tab with changes is reloaded
    /// only through the question that comes with saving it. A value that is typed into a cell and not committed is such a change: it is
    /// committed first, and then the tab has changes of its own, so it is not reloaded; a value that cannot be committed stops the reload
    /// with the message of the cell.
    /// </summary>
    public Task ReloadAsync(TabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab is not EditableRowsTabViewModel { CanReload: true } editable)
        {
            return Task.CompletedTask;
        }

        // The typed value is never dropped silently: it becomes a change of the document (the reload condition is asked again), or it keeps
        // the user in the cell.
        if (!CommitPendingEdits([editable]) || !editable.CanReload)
        {
            return Task.CompletedTask;
        }

        return ReloadCoreAsync(tab, discardChanges: false);
    }

    // The reload itself. With discardChanges the tab is replaced although it has changes: the user chose "Reload" in the question of a save.
    private async Task ReloadCoreAsync(TabViewModel tab, bool discardChanges)
    {
        if (!_tabs.Tabs.Contains(tab) || !TabSource.TryGetProjectId(tab.PackPath, out var projectId) || !_reloading.Add(tab))
        {
            return;
        }

        try
        {
            if (_projects.Projects.FirstOrDefault(state => state.Id == projectId) is not { } project)
            {
                return;
            }

            if (!CanStartSave())
            {
                _dialogs.ShowMessage(_texts.ReloadFailedTitle, _texts.ProjectBusy);
                return;
            }

            // Null: the run did not finish or the project is gone; the runner told the user about a failure already.
            if (await _projects.OpenFileAsync(projectId, RelativeFileOf(tab)) is not { } opened || !_tabs.Tabs.Contains(tab))
            {
                return;
            }

            // Changed while the file was read (a timer, the keyboard): the user's changes win, nothing is replaced. That includes a value that
            // was typed into a cell meanwhile and is not committed yet.
            if (!discardChanges && (tab.IsModified || tab is EditableRowsTabViewModel { HasPendingEdit: true }))
            {
                return;
            }

            // The document is replaced on purpose now (the file was read, or the user chose "Reload" over their changes): an editor that is
            // still open is dropped first, so that it cannot write its text into the new document, and the tab that replaces this one
            // does not commit it into the old one.
            (tab as EditableRowsTabViewModel)?.DiscardPendingEdit();

            if (tab is EditableRowsTabViewModel editable && TryCreateDocument(opened) is { } document)
            {
                var old = editable.Document;
                editable.ReplaceDocument(document);
                _history.RemoveWhere(step => step is DocumentEdit edit && ReferenceEquals(edit.Document, old));

                var opening = DescribeOpening(projectId, opened);
                _known[editable] = opening?.Known;
                editable.SetOutsideChange(opening?.Mark ?? OutsideChange.None);
            }
            else
            {
                var source = TabSource.ForProject(projectId, MessageText.EscapeControlCharacters(project.DisplayName));
                _tabs.Replace(tab, source, opened, _projects.Schema);
            }
        }
        finally
        {
            _reloading.Remove(tab);
        }
    }

    // The document of the result of a reload, or null when it is no longer editable (what that is has been logged by the tab that
    // TabsViewModel.Replace builds then).
    private static RowsDocument? TryCreateDocument(OpenedEntry entry)
    {
        if (entry.ReadProblem != EntryProblem.None)
        {
            return null;
        }

        try
        {
            return CreateDocument(entry);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
