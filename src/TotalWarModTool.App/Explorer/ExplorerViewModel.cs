using System.Collections.ObjectModel;
using System.Windows.Input;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// The pack explorer: the packs of the game grouped by origin as a tree, shown instead by a message when there is no tree to show.
/// A pack is read when its node is expanded and released when it is collapsed; the controller holds the index meanwhile.
/// Lives on the UI thread. Everything the user starts runs through the controller (and with it the progress component).
/// </summary>
public sealed class ExplorerViewModel : ObservableObject
{
    private readonly WorkspaceController _workspace;
    private readonly Func<PackSource, string, Task> _openEntry;

    // The state of the groups survives a rebuild, also while a message hides the tree.
    private readonly Dictionary<PackOrigin, bool> _groupExpanded = [];

    private string? _message;
    private bool _showOpenSettings;

    // What the tree was built from. A change of the controller that leaves all three alone (it is busy, or finished a silent run) needs no rebuild.
    private bool _built;
    private GameStatus _builtStatus;
    private bool _builtManualPathInvalid;
    private PackCatalogResult? _builtCatalog;

    public ExplorerViewModel(WorkspaceController workspace, AppTexts texts, Func<PackSource, string, Task> openEntry, Action openSettings)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(openEntry);
        ArgumentNullException.ThrowIfNull(openSettings);

        _workspace = workspace;
        Texts = texts;
        _openEntry = openEntry;
        OpenSettingsCommand = new RelayCommand(openSettings);

        Rebuild();
        workspace.Changed += (_, _) => Rebuild();
        workspace.PackChanged += (_, pack) => OnPackChanged(pack);
        texts.PropertyChanged += (_, _) => RefreshTexts();
    }

    public AppTexts Texts { get; }

    public ObservableCollection<ExplorerNode> Groups { get; } = [];

    /// <summary>Shown instead of the tree; null when the tree is shown.</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
                OnPropertyChanged(nameof(ShowTree));
            }
        }
    }

    public bool HasMessage => _message is not null;

    public bool ShowTree => _message is null;

    /// <summary>True with the "not found" messages: the way out is the settings.</summary>
    public bool ShowOpenSettings
    {
        get => _showOpenSettings;
        private set => SetProperty(ref _showOpenSettings, value);
    }

    public ICommand OpenSettingsCommand { get; }

    /// <summary>The packs whose tree is shown, in catalog order.</summary>
    public IReadOnlyList<string> ExpandedPackPaths => PackNodes().Where(node => node.IsOpen).Select(node => node.Pack.Path).ToList();

    /// <summary>The set of <see cref="ExpandedPackPaths"/> changed.</summary>
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

    private IEnumerable<PackNode> PackNodes() => Groups.SelectMany(group => group.Children).OfType<PackNode>();

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

        Groups.Clear();
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
        new(pack, Texts, _workspace.Profile?.PackFormat, _openEntry, OnPackExpandedChanged);

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
