using TotalWarModTool.Projects;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

// The project half of the explorer: the group and the nodes of the projects, the listings of their files and the context menus.
public sealed partial class ExplorerViewModel
{
    /// <summary>The silent listing of a project. Null: the one of the controller. Tests set it to hold or reorder listings.</summary>
    internal Func<Guid, Task<ProjectFileList?>>? SilentListing { get; set; }

    /// <summary>How often the entries of a pack entry node were built. For tests: they must be built only when a menu asks.</summary>
    internal int EntryActionBuilds { get; private set; }

    /// <summary>The projects whose files are shown, in list order.</summary>
    public IReadOnlyList<Guid> ExpandedProjectIds => ProjectNodes().Where(node => node.IsOpen).Select(node => node.ProjectId).ToList();

    /// <summary>
    /// Expands these projects from the listing that was already read, without a runner run. Projects that are not in the tree or cannot be
    /// used are skipped.
    /// </summary>
    public void ApplyRestoredProjects(IReadOnlyList<(Guid Id, ProjectFileList Files)> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var changed = false;
        foreach (var (id, files) in projects)
        {
            var node = ProjectNodes().FirstOrDefault(candidate => candidate.ProjectId == id);
            if (node is null || !node.IsUsable || node.IsOpen || node.IsLoading)
            {
                continue;
            }

            node.ShowFiles(files);
            node.SetExpandedQuietly(true);
            changed = true;
        }

        if (changed)
        {
            ExpandedPacksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Lists the open projects again, silently (no runner, no window), and replaces their children; sub folders that are open stay open. Called
    /// after the projects were read again (refresh) and after a build or an import started from the explorer.
    /// </summary>
    public async Task RefreshProjectFilesAsync()
    {
        if (_projects is null)
        {
            return;
        }

        foreach (var node in ProjectNodes().Where(candidate => candidate.IsOpen && !candidate.IsLoading).ToList())
        {
            // The nodes were taken before the first await. A node that was collapsed, removed or is being expanded meanwhile is left alone: a
            // runner listing in flight delivers fresh content, and bumping the generation here would throw it away.
            if (!node.IsOpen || node.IsLoading || node.IsDetached)
            {
                continue;
            }

            // A listing that is overtaken by a later one (a newer silent listing, a collapse and expand) must not replace its result.
            var generation = node.BumpGeneration();
            var files = await (SilentListing ?? _projects.ListFilesSilentlyAsync)(node.ProjectId);

            // The node may have been collapsed, removed or broken while the listing ran: then there is nothing to replace.
            if (files is null || node.IsDetached || node.Generation != generation || !node.IsOpen || !node.IsUsable)
            {
                continue;
            }

            node.ShowFiles(files, node.ExpandedFolders());
        }
    }

    // ---- projects --------------------------------------------------------------------------------------------------------------

    // Matches the project nodes with the list by id instead of building them again: a node that stays keeps its children and its expanded
    // state, one that is new is added, one that is gone is removed. Name, emphasis, problem and tool tip come from the state.
    private void ReconcileProjects()
    {
        if (_projects is null || _projectsGroup is null)
        {
            return;
        }

        var before = ExpandedProjectIds;
        var children = _projectsGroup.Children;
        var existing = children.OfType<ProjectNode>().ToDictionary(node => node.ProjectId);
        var wanted = new List<ProjectNode>();
        var activeId = _projects.ActiveId;
        foreach (var state in _projects.Projects)
        {
            if (existing.Remove(state.Id, out var node))
            {
                node.Apply(state, state.Id == activeId);
            }
            else
            {
                node = new ProjectNode(state, state.Id == activeId, new ProjectTreeContext(state.Id, _openProjectFile, Texts, _fileActions), OnProjectExpandedChanged);
            }

            wanted.Add(node);
        }

        // A listing that is still running for a node that is gone must find out that it no longer counts.
        foreach (var gone in existing.Values)
        {
            gone.IsDetached = true;
            children.Remove(gone);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var at = children.IndexOf(wanted[i]);
            if (at < 0)
            {
                children.Insert(i, wanted[i]);
            }
            else if (at != i)
            {
                children.Move(at, i);
            }
        }

        _projectsGroup.Rename(Texts.ExplorerProjectsGroup(wanted.Count));
        if (!before.SequenceEqual(ExpandedProjectIds))
        {
            ExpandedPacksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnProjectExpandedChanged(ProjectNode node, bool isExpanded)
    {
        if (node.IsDetached)
        {
            return;
        }

        if (isExpanded)
        {
            ExpandProject(node);
        }
        else
        {
            CollapseProject(node);
        }
    }

    // async void on purpose, for the same reasons as ExpandPack.
    private async void ExpandProject(ProjectNode node) => await ExpandProjectAsync(node);

    // The expand of a user and the expand that a new file asks for: one path, which can be waited for.
    private async Task ExpandProjectAsync(ProjectNode node)
    {
        if (node.IsOpen || node.IsLoading)
        {
            return;
        }

        if (!node.IsUsable)
        {
            node.SetExpandedQuietly(false);
            return;
        }

        node.IsLoading = true;
        var generation = node.BumpGeneration();
        try
        {
            var files = await _projects!.ListFilesAsync(node.ProjectId);
            if (node.IsDetached || node.Generation != generation)
            {
                // The project left the list, or a newer state of the node replaced what this listing was for.
                return;
            }

            if (!node.IsExpanded)
            {
                // Collapsed again while the listing was running: nothing to show.
                return;
            }

            if (files is null || !node.IsUsable)
            {
                // Cancelled, or nothing could be started, or the project broke meanwhile: back to the collapsed state.
                node.SetExpandedQuietly(false);
                return;
            }

            node.ShowFiles(files);
            ExpandedPacksChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Leave no expanded node without a tree; the exception itself goes on to the global handler.
            if (!node.IsDetached)
            {
                node.SetExpandedQuietly(false);
            }

            throw;
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    private void CollapseProject(ProjectNode node)
    {
        // A listing that is still running ends in ExpandProject, which sees the collapsed node and drops what it read.
        if (node.IsLoading || !node.IsOpen)
        {
            return;
        }

        node.ShowPlaceholder();
        ExpandedPacksChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- context menus ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the entries of every context menu again. For a change that the explorer cannot see itself: the reason why a project cannot be
    /// removed depends on the unsaved changes of its tabs.
    /// </summary>
    public void RefreshProjectActions() => RefreshActions();

    // Builds the entries of every menu again: the reasons, the commands and the texts all depend on what is going on right now.
    private void RefreshActions()
    {
        if (_projects is null || _projectCommands is null)
        {
            return;
        }

        foreach (var node in PackNodes())
        {
            node.ReplaceActions(node.CanExpand ? PackActions(node.Pack) : []);
        }

        var states = _projects.Projects.ToDictionary(state => state.Id);
        foreach (var node in ProjectNodes())
        {
            node.ReplaceActions(states.TryGetValue(node.ProjectId, out var state) ? ProjectActions(state.Id) : []);
        }
    }

    private IReadOnlyList<NodeAction> PackActions(PackSource pack) =>
    [
        MakeAction(Texts.ProjectActionImport, _projectCommands!.ProjectsDisabledReason, async () =>
        {
            await _projectCommands.ImportAsync(pack.Path, pack.Name);
            await RefreshProjectFilesAsync();
        }),
    ];

    private IReadOnlyList<NodeAction> ProjectActions(Guid id)
    {
        var commands = _projectCommands!;
        var files = _files;
        return
        [
            // The new files first, apart from the commands of the project itself.
            .. (files is null ? Array.Empty<NodeAction>() : FileActionsOfProject(files, id)),

            // Plain edits of the list: they need no free runner and are never gated on one.
            MakeAction(Texts.ProjectActionSetActive, null, () =>
            {
                commands.SetActive(id);
                return Task.CompletedTask;
            }, needsRunner: false),
            MakeAction(Texts.BuildMessageTitle, commands.BuildDisabledReason(id), async () =>
            {
                await commands.BuildAsync(id, install: false);
                await RefreshProjectFilesAsync();
            }),
            MakeAction(Texts.ProjectActionBuildAndInstall, commands.InstallDisabledReason(id), async () =>
            {
                await commands.BuildAsync(id, install: true);
                await RefreshProjectFilesAsync();
            }),
            MakeAction(Texts.UninstallMessageTitle, commands.UninstallDisabledReason(id), () => commands.UninstallAsync(id)),
            MakeAction(Texts.ProjectActionRemove, commands.RemoveDisabledReason(id), () =>
            {
                commands.Remove(id);
                return Task.CompletedTask;
            }, needsRunner: false),
        ];
    }

    // The entries of the project node for new files: "New table", "New loc file" and the line that keeps them apart from the rest. Not followed by
    // a listing here: the controller announces every change of the files, and the explorer follows that in one place (OnProjectFilesChanged).
    private NodeAction[] FileActionsOfProject(ProjectFilesViewModel files, Guid id)
    {
        var reason = files.NewFileDisabledReason(id);
        return
        [
            MakeAction(Texts.FileNewTableTitle, reason, () => files.NewTableAsync(id)),
            MakeAction(Texts.FileNewLocTitle, reason, () => files.NewLocAsync(id)),
            NodeAction.Separator,
        ];
    }

    // The menu of a pack entry, built when a menu asks (see EntryNode): one entry, "Add to the project".
    private IReadOnlyList<NodeAction> EntryActions(EntryNode node)
    {
        EntryActionBuilds++;
        var files = _files!;
        return [MakeAction(Texts.FileAddTitle, files.AdoptDisabledReason, () => files.AdoptAsync(node.Pack, node.EntryPath))];
    }

    // The menu of a file of a project, built when a menu asks (see ProjectFileNode): "Rename" and "Delete".
    private IReadOnlyList<NodeAction> FileActions(ProjectFileNode node)
    {
        var files = _files!;
        var id = node.ProjectId;
        var file = node.RelativeFile;
        var reason = files.FileActionDisabledReason(id, file);
        return
        [
            MakeAction(Texts.FileRenameTitle, reason, () => files.RenameAsync(id, file)),
            MakeAction(Texts.FileActionDelete, reason, () => files.DeleteAsync(id, file)),
        ];
    }

    // The files of the projects changed (a file action, or the undo or redo of one): the open projects list again, silently; a closed one stays
    // closed. async void on purpose, as for the other event handlers that start work.
    private async void OnProjectFilesChanged() => await RefreshProjectFilesAsync();

    // A file that was created, added or renamed while it was open should be shown and opened. Its project is expanded first, through the path of a
    // user expand, and waited for: the real runner runs one operation at a time, so the open would be refused while the listing runs. A project
    // that cannot be listed right now stays closed, and the file is opened anyway. async void on purpose, as for ExpandPack.
    private async void OnOpenRequested(Guid projectId, string relativeFile)
    {
        if (ProjectNodes().FirstOrDefault(candidate => candidate.ProjectId == projectId) is { IsUsable: true, IsOpen: false, IsLoading: false, IsDetached: false } node)
        {
            node.SetExpandedQuietly(true);
            await ExpandProjectAsync(node);
        }

        await _openProjectFile(projectId, relativeFile);
    }

    // async void on purpose, as for every command that starts an operation: the controllers handle their failures, a surprise reaches the global handler.
    // An action that needs a free runner can run when there is no reason against it and nothing else runs. Its reason text is the reason of the
    // project first (no game folder, nothing installed, ...), then "busy"; an entry is never disabled without one. A plain edit of the list
    // ignores the runner and is disabled only by its own reason.
    private NodeAction MakeAction(string header, string? disabledReason, Func<Task> run, bool needsRunner = true)
    {
        if (!needsRunner)
        {
            return new NodeAction(header, new RelayCommand(async () => await run(), () => disabledReason is null), disabledReason);
        }

        var shown = disabledReason ?? (_projects!.CanStartOperation ? null : Texts.ProjectBusy);
        return new NodeAction(header, new RelayCommand(async () => await run(), () => disabledReason is null && _projects!.CanStartOperation), shown);
    }
}
