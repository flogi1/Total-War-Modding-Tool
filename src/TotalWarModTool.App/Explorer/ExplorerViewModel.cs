using System.Collections.ObjectModel;
using System.Windows.Input;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// The explorer: the mod projects of the game (a group in front, when projects are available) and the packs of the game grouped by origin
/// as a tree. A message about the packs is shown above the tree, or instead of it when there is no group to show.
/// A pack is read when its node is expanded and released when it is collapsed; the controller holds the index meanwhile.
/// A project is listed when its node is expanded; the list is dropped when it is collapsed.
/// Lives on the UI thread. Everything the user starts runs through the controllers (and with it the progress component).
/// </summary>
public sealed partial class ExplorerViewModel : ObservableObject
{
    private readonly WorkspaceController _workspace;
    private readonly Func<PackSource, string, Task> _openEntry;
    private readonly ProjectsController? _projects;
    private readonly ProjectsViewModel? _projectCommands;
    private readonly Func<Guid, string, Task> _openProjectFile;

    // The file operations; null without them. The builders are one delegate each, shared by all entry nodes and all project file nodes.
    private readonly ProjectFilesViewModel? _files;
    private readonly Func<EntryNode, IReadOnlyList<NodeAction>>? _entryActions;
    private readonly Func<ProjectFileNode, IReadOnlyList<NodeAction>>? _fileActions;

    // The group of the projects is created once and survives every rebuild of the pack groups, with its expanded project nodes.
    private readonly ProjectsGroupNode? _projectsGroup;

    // The state of the groups survives a rebuild, also while a message hides the tree.
    private readonly Dictionary<PackOrigin, bool> _groupExpanded = [];

    private string? _message;
    private bool _showOpenSettings;

    // What the tree was built from. A change of the controller that leaves all three alone (it is busy, or finished a silent run) needs no rebuild.
    private bool _built;
    private GameStatus _builtStatus;
    private bool _builtManualPathInvalid;
    private PackCatalogResult? _builtCatalog;

    /// <param name="projects">The mod projects; null, or one that is not available (no game profile), gives an explorer without the group of projects.</param>
    /// <param name="projectCommands">The commands behind the context menus of the project and pack nodes; without it those nodes have no menu.</param>
    /// <param name="openProjectFile">Opens a file of a project (id, path below the pack folder with '\'). Passed in so the explorer knows no tabs.</param>
    /// <param name="projectFiles">
    /// The file commands (new table, new loc file, add to the project, rename, delete); without them the nodes of files and entries have no menu
    /// and the project nodes have no entries for new files. A file that it asks to open is opened through <paramref name="openProjectFile"/>, after its project was expanded.
    /// </param>
    public ExplorerViewModel(WorkspaceController workspace, AppTexts texts, Func<PackSource, string, Task> openEntry, Action openSettings,
        ProjectsController? projects = null, ProjectsViewModel? projectCommands = null, Func<Guid, string, Task>? openProjectFile = null,
        ProjectFilesViewModel? projectFiles = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(openEntry);
        ArgumentNullException.ThrowIfNull(openSettings);

        _workspace = workspace;
        Texts = texts;
        _openEntry = openEntry;
        _openProjectFile = openProjectFile ?? ((_, _) => Task.CompletedTask);
        OpenSettingsCommand = new RelayCommand(openSettings);

        // The tree is shown whenever there is a group in it; a message then stands above it.
        Groups.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowTree));

        if (projects is { IsAvailable: true })
        {
            _projects = projects;
            _projectCommands = projectCommands;
            if (projectFiles is not null)
            {
                _files = projectFiles;
                _entryActions = EntryActions;
                _fileActions = FileActions;

                // A file that was created, added or renamed while it was open: show it and open it.
                projectFiles.OpenRequested += (_, request) => OnOpenRequested(request.ProjectId, request.RelativeFile);

                // The one place that follows file actions and the undo and redo of them: what is open lists again, silently.
                projectFiles.FilesChanged += (_, _) => OnProjectFilesChanged();
            }

            _projectsGroup = new ProjectsGroupNode(texts.ExplorerProjectsGroup(0), isExpanded: true);
            Groups.Add(_projectsGroup);
            ReconcileProjects();
            projects.Changed += (_, _) =>
            {
                ReconcileProjects();
                RefreshActions();
            };
        }

        Rebuild();
        RefreshActions();

        // The one place that rebuilds the menus after a change of the workspace (the controller's OperationStateChanged comes from the same
        // change): after the tree, so that new pack nodes get their entries too.
        workspace.Changed += (_, _) =>
        {
            Rebuild();
            RefreshActions();
        };
        workspace.PackChanged += (_, pack) => OnPackChanged(pack);
        texts.PropertyChanged += (_, _) => RefreshTexts();
    }

    public AppTexts Texts { get; }

    /// <summary>The group of the projects (first, when there is one), then the groups of the packs.</summary>
    public ObservableCollection<ExplorerNode> Groups { get; } = [];

    /// <summary>Shown above the tree, or instead of it when the tree has no group; null when there is nothing to say.</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => _message is not null;

    /// <summary>The tree has at least one group.</summary>
    public bool ShowTree => Groups.Count > 0;

    /// <summary>True with the "not found" messages: the way out is the settings.</summary>
    public bool ShowOpenSettings
    {
        get => _showOpenSettings;
        private set => SetProperty(ref _showOpenSettings, value);
    }

    public ICommand OpenSettingsCommand { get; }

    /// <summary>The packs whose tree is shown, in catalog order.</summary>
    public IReadOnlyList<string> ExpandedPackPaths => PackNodes().Where(node => node.IsOpen).Select(node => node.Pack.Path).ToList();

    /// <summary>The set of <see cref="ExpandedPackPaths"/> or of <see cref="ExpandedProjectIds"/> changed.</summary>
    public event EventHandler? ExpandedPacksChanged;

    /// <summary>Expands these packs from the content the controller already read, without a runner run. Packs that are not in the tree are skipped.</summary>
    public void ApplyRestored(IReadOnlyList<RestoredPack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        var changed = false;
        foreach (var restored in packs)
        {
            var node = FindNode(restored.Pack.Path);
            if (node is null || !node.CanExpand || node.IsOpen || node.IsLoading)
            {
                continue;
            }

            node.ShowContent(restored.Content);
            node.SetExpandedQuietly(true);
            changed = true;
        }

        if (changed)
        {
            ExpandedPacksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- tree ------------------------------------------------------------------------------------------------------------------

    private IEnumerable<PackNode> PackNodes() => Groups.OfType<GroupNode>().SelectMany(group => group.Children).OfType<PackNode>();

    private IEnumerable<ProjectNode> ProjectNodes() => _projectsGroup?.Children.OfType<ProjectNode>() ?? [];

    private PackNode? FindNode(string path) =>
        PackNodes().FirstOrDefault(node => string.Equals(node.Pack.Path, path, StringComparison.OrdinalIgnoreCase));

    private (string? Text, bool ShowOpenSettings) ComputeMessage()
    {
        var texts = Texts;
        var catalog = _workspace.Catalog;
        return _workspace.Status switch
        {
            GameStatus.NoProfile => (texts.ExplorerNoProfile, false),
            GameStatus.NotFound => (_workspace.ManualPathInvalid ? texts.ExplorerManualPathInvalid : texts.ExplorerGameNotFound, true),
            _ when catalog is null => (texts.ExplorerLoading, false),
            _ when catalog.Packs.Count == 0 => (texts.ExplorerNoPacks, false),
            _ => (null, false),
        };
    }

    // A language switch: the texts of the groups and the message are computed again. The nodes stay, so nothing the user expanded changes.
    private void RefreshTexts()
    {
        foreach (var group in Groups.OfType<GroupNode>())
        {
            group.Rename(Texts.ExplorerGroup(Texts.OriginName(group.Origin), group.Children.Count));
        }

        if (_message is not null)
        {
            (Message, ShowOpenSettings) = ComputeMessage();
        }

        // The group name, the tool tips of broken projects and the menus are texts of the current language.
        ReconcileProjects();
        RefreshActions();
    }

    private void Rebuild()
    {
        var catalog = _workspace.Catalog;
        if (_built && _builtStatus == _workspace.Status && _builtManualPathInvalid == _workspace.ManualPathInvalid && ReferenceEquals(_builtCatalog, catalog))
        {
            return;
        }

        _built = true;
        _builtStatus = _workspace.Status;
        _builtManualPathInvalid = _workspace.ManualPathInvalid;
        _builtCatalog = catalog;

        // What was open before: a pack stays open when it was expanded (also one whose read is still running) and the controller still holds its index.
        var before = ExpandedPackPaths;
        var wasExpanded = PackNodes().Where(node => node.IsExpanded).Select(node => node.Pack.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in Groups.OfType<GroupNode>())
        {
            _groupExpanded[group.Origin] = group.IsExpanded;
        }

        // A read that is still running delivers into a node of the old tree; it must find out that it no longer counts.
        foreach (var node in PackNodes())
        {
            node.IsDetached = true;
        }

        // Only the groups of the packs go; the group of the projects stays in front with its expanded project nodes.
        for (var i = Groups.Count - 1; i >= 0; i--)
        {
            if (Groups[i] is not ProjectsGroupNode)
            {
                Groups.RemoveAt(i);
            }
        }

        var (text, showOpenSettings) = ComputeMessage();
        if (text is null && catalog is not null)
        {
            foreach (var origin in Enum.GetValues<PackOrigin>())
            {
                var packs = catalog.Packs.Where(pack => pack.Origin == origin).Select(CreatePackNode).ToList();
                if (packs.Count == 0)
                {
                    continue;
                }

                foreach (var node in packs.Where(node => node.CanExpand && wasExpanded.Contains(node.Pack.Path)))
                {
                    if (_workspace.OpenContent(node.Pack) is { } content)
                    {
                        node.ShowContent(content);
                        node.SetExpandedQuietly(true);
                    }
                }

                var expanded = _groupExpanded.TryGetValue(origin, out var known) ? known : origin == PackOrigin.DataFolderMod;
                Groups.Add(new GroupNode(origin, Texts.ExplorerGroup(Texts.OriginName(origin), packs.Count), packs, expanded));
            }
        }

        Message = text;
        ShowOpenSettings = showOpenSettings;
        if (!before.SequenceEqual(ExpandedPackPaths))
        {
            ExpandedPacksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private PackNode CreatePackNode(PackSource pack) =>
        new(pack, Texts, _workspace.Profile?.PackFormat, _openEntry, OnPackExpandedChanged, _entryActions);

    // ---- expand and collapse ---------------------------------------------------------------------------------------------------

    private void OnPackExpandedChanged(PackNode node, bool isExpanded)
    {
        if (node.IsDetached)
        {
            return;
        }

        if (isExpanded)
        {
            ExpandPack(node);
        }
        else
        {
            CollapsePack(node);
        }
    }

    // async void on purpose: the setter of IsExpanded cannot wait, and the view model has no log. A surprise reaches the dispatcher, where the
    // global handler shows the crash dialog. The runner and the controller handle the expected failures (cancel, broken pack) themselves.
    private async void ExpandPack(PackNode node)
    {
        if (node.IsOpen || node.IsLoading)
        {
            return;
        }

        if (!node.CanExpand)
        {
            node.SetExpandedQuietly(false);
            return;
        }

        node.IsLoading = true;
        try
        {
            var content = await _workspace.OpenPackAsync(node.Pack);
            if (node.IsDetached)
            {
                // The tree was rebuilt meanwhile; the new tree decided what stays open.
                return;
            }

            if (!node.IsExpanded)
            {
                // Collapsed again while the read was running: nothing to show, nothing to keep.
                if (content is not null)
                {
                    _workspace.ClosePack(node.Pack);
                }

                return;
            }

            if (content is null)
            {
                node.SetExpandedQuietly(false);
                if (_workspace.LastIndexProblem(node.Pack) is { } problem)
                {
                    node.MarkIndexProblem(MessageText.WithDetail(Texts.PackProblemText(problem.Problem, _workspace.Profile?.PackFormat), problem.Detail));
                }

                return;
            }

            node.ShowContent(content);
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

    private void CollapsePack(PackNode node)
    {
        // A read that is still running ends in ExpandPack, which sees the collapsed node and drops what it read.
        if (node.IsLoading || !node.IsOpen)
        {
            return;
        }

        _workspace.ClosePack(node.Pack);
        node.ShowPlaceholder();
        ExpandedPacksChanged?.Invoke(this, EventArgs.Empty);
    }

    // The cached index of this pack was replaced or dropped: the tree on screen is out of date. The normal collapse also releases a replacement.
    private void OnPackChanged(PackSource pack)
    {
        if (FindNode(pack.Path) is { IsOpen: true } node)
        {
            node.IsExpanded = false;
        }
    }
}
