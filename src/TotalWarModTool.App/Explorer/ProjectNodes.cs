using TotalWarModTool.Projects;

namespace TotalWarModTool.App;

/// <summary>The mod projects of the game. Name: "Projects (&lt;count&gt;)". Its project nodes are matched by id when the list changes, so the group lives as long as the explorer.</summary>
public sealed class ProjectsGroupNode : ExplorerNode
{
    internal ProjectsGroupNode(string name, bool isExpanded) : base(name)
    {
        SetExpandedQuietly(isExpanded);
    }
}

/// <summary>What the nodes below one project share: its id, the open action and the texts. Also the one place that makes file names safe to show.</summary>
internal sealed class ProjectTreeContext(Guid projectId, Func<Guid, string, Task> openFile, AppTexts texts)
{
    private const int MaxNameChars = 260;

    /// <summary>The longest tool tip text (a folder, a detail of the library) that is shown; a longer one is cut.</summary>
    public const int MaxToolTipChars = 1000;

    public Guid ProjectId { get; } = projectId;

    public Func<Guid, string, Task> OpenFile { get; } = openFile;

    public AppTexts Texts { get; } = texts;

    /// <summary>Foreign text (a file name, a path, a project name) as display text: control characters escaped, a long text cut.</summary>
    public string Display(string text, int maxChars = MaxNameChars) =>
        DisplayText.Safe(text, maxChars, " (" + Texts.TextShortened + ")");
}

/// <summary>A folder or file of the listing of a project, in the order the explorer shows it. Built once per listing, nodes are made from it on demand.</summary>
internal sealed class ProjectTreeEntry
{
    private Dictionary<string, ProjectTreeEntry>? _folders = new(StringComparer.OrdinalIgnoreCase);
    private List<ProjectTreeEntry>? _files = [];

    private ProjectTreeEntry(string name, string relativePath, ProjectFileItem? file)
    {
        Name = name;
        RelativePath = relativePath;
        File = file;
    }

    public string Name { get; }

    /// <summary>The path below the pack folder, with '\'.</summary>
    public string RelativePath { get; }

    /// <summary>Set for a file, null for a folder.</summary>
    public ProjectFileItem? File { get; }

    /// <summary>Folders first, then files; each ordinal and ignoring case.</summary>
    public IReadOnlyList<ProjectTreeEntry> Children { get; private set; } = [];

    /// <summary>The top level of the tree of <paramref name="files"/> (paths with '\').</summary>
    public static IReadOnlyList<ProjectTreeEntry> Build(IEnumerable<ProjectFileItem> files)
    {
        var root = new ProjectTreeEntry(string.Empty, string.Empty, null);
        foreach (var file in files)
        {
            var segments = file.RelativeFile.Split('\\');
            var current = root;
            var path = string.Empty;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                path = path.Length == 0 ? segments[i] : path + "\\" + segments[i];
                current = current.GetOrAddFolder(segments[i], path);
            }

            current._files!.Add(new ProjectTreeEntry(segments[^1], file.RelativeFile, file));
        }

        root.Seal();
        return root.Children;
    }

    private ProjectTreeEntry GetOrAddFolder(string name, string path)
    {
        if (!_folders!.TryGetValue(name, out var folder))
        {
            folder = new ProjectTreeEntry(name, path, null);
            _folders[name] = folder;
        }

        return folder;
    }

    private void Seal()
    {
        var folders = _folders!.Values.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Name, StringComparer.Ordinal);
        var files = _files!.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Name, StringComparer.Ordinal);
        Children = [.. folders, .. files];
        _folders = null;
        _files = null;
        foreach (var child in Children.Where(entry => entry.File is null))
        {
            child.Seal();
        }
    }
}

/// <summary>
/// A mod project of the list. A usable project carries a <see cref="PlaceholderNode"/> until the view model has listed its pack folder; one that
/// cannot be used (<see cref="HasProblem"/>) has no children and does not expand. The view model owns the loading state.
/// </summary>
public sealed class ProjectNode : ExplorerNode
{
    private const int MaxNameChars = 100;

    private readonly ProjectTreeContext _context;
    private readonly Action<ProjectNode, bool> _expandedChanged;
    private int _generation;

    internal ProjectNode(ProjectState state, bool isActive, ProjectTreeContext context, Action<ProjectNode, bool> expandedChanged) : base(string.Empty)
    {
        _context = context;
        _expandedChanged = expandedChanged;
        ProjectId = state.Id;
        Apply(state, isActive);
    }

    public Guid ProjectId { get; }

    /// <summary>The project file could be read; the node can expand.</summary>
    public bool IsUsable { get; private set; }

    /// <summary>The children show the files of the project (set by the view model).</summary>
    internal bool IsOpen { get; private set; }

    /// <summary>The files are being listed (set by the view model).</summary>
    internal bool IsLoading { get; set; }

    /// <summary>The project left the list; results that arrive for this node no longer count (set by the view model).</summary>
    internal bool IsDetached { get; set; }

    /// <summary>
    /// Counts the changes of what the node shows or is about to show: an expand starts, a silent listing starts, files are shown, the placeholder
    /// is shown. A listing remembers the value before it waits and applies its result only when the value is still the same, so an older
    /// listing never replaces the result of a newer one.
    /// </summary>
    internal int Generation => _generation;

    /// <summary>Starts a new generation; the result of every earlier listing no longer counts. Returns the new value.</summary>
    internal int BumpGeneration() => ++_generation;

    /// <summary>Takes name, emphasis, problem and tool tip from the state of the project. The children and the expanded state stay, unless the project became unusable.</summary>
    internal void Apply(ProjectState state, bool isActive)
    {
        Rename(_context.Display(state.DisplayName, MaxNameChars));
        IsEmphasized = isActive;
        IsUsable = state.IsUsable;
        if (state.IsUsable)
        {
            HasProblem = false;
            ToolTip = _context.Display(state.Folder, ProjectTreeContext.MaxToolTipChars);
            if (!IsOpen && Children.Count == 0)
            {
                ReplaceChildren([new PlaceholderNode()]);
            }

            return;
        }

        HasProblem = true;
        ToolTip = MessageText.WithDetail(_context.Texts.ProjectProblemText(state.Problem),
            state.Detail is null ? null : _context.Display(state.Detail, ProjectTreeContext.MaxToolTipChars));
        if (Children.Count > 0)
        {
            IsOpen = false;
            _generation++;
            ReplaceChildren([]);
        }

        if (IsExpanded)
        {
            SetExpandedQuietly(false);
        }
    }

    /// <summary>
    /// Shows the files of the listing as the children. The sub folders in <paramref name="expandedFolders"/> (relative paths with '\')
    /// come back expanded.
    /// </summary>
    internal void ShowFiles(ProjectFileList list, ISet<string>? expandedFolders = null)
    {
        IsOpen = true;
        _generation++;
        var children = ProjectFolderNode.CreateChildren(ProjectTreeEntry.Build(list.Files), _context, expandedFolders)
            .Concat(list.Problems.Select(problem => ProjectFolderNode.ForProblem(problem, _context)))
            .ToList();

        // A project without its pack folder is not empty by design: the node says what is missing.
        if (!list.PackFolderExists)
        {
            children.Add(ProjectFolderNode.ForMissingPackFolder(_context));
        }

        ReplaceChildren(children);
    }

    /// <summary>Back to the state of a collapsed project: one placeholder as the child.</summary>
    internal void ShowPlaceholder()
    {
        IsOpen = false;
        _generation++;
        ReplaceChildren([new PlaceholderNode()]);
    }

    /// <summary>The relative paths (with '\') of the sub folders that are expanded now.</summary>
    internal HashSet<string> ExpandedFolders()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(this);
        return result;

        void Collect(ExplorerNode parent)
        {
            foreach (var folder in parent.Children.OfType<ProjectFolderNode>().Where(folder => folder.IsExpanded))
            {
                result.Add(folder.RelativePath);
                Collect(folder);
            }
        }
    }

    protected override void OnExpandedChanged(bool isExpanded) => _expandedChanged(this, isExpanded);
}

/// <summary>
/// A folder below the pack folder of a project; its children are created when it is expanded for the first time. A folder that is a link or
/// cannot be listed is a node with <see cref="ExplorerNode.HasProblem"/> and no children.
/// </summary>
public sealed class ProjectFolderNode : ExplorerNode
{
    private readonly ProjectTreeEntry? _folder;
    private readonly ProjectTreeContext _context;
    private bool _filled;

    internal ProjectFolderNode(ProjectTreeEntry folder, ProjectTreeContext context, ISet<string>? expandedFolders) : base(context.Display(folder.Name))
    {
        _folder = folder;
        _context = context;
        RelativePath = folder.RelativePath;
        ToolTip = context.Display(folder.RelativePath);
        if (folder.Children.Count == 0)
        {
            return;
        }

        if (expandedFolders is not null && expandedFolders.Contains(folder.RelativePath))
        {
            _filled = true;
            ReplaceChildren(CreateChildren(folder.Children, context, expandedFolders));
            SetExpandedQuietly(true);
        }
        else
        {
            ReplaceChildren([new PlaceholderNode()]);
        }
    }

    private ProjectFolderNode(string name, string relativePath, string toolTip, ProjectTreeContext context) : base(name)
    {
        _context = context;
        RelativePath = relativePath;
        ToolTip = toolTip;
        HasProblem = true;
    }

    /// <summary>The path below the pack folder, with '\'.</summary>
    public string RelativePath { get; }

    internal static ProjectFolderNode ForProblem(ProjectFileProblem problem, ProjectTreeContext context) =>
        new(context.Display(problem.RelativePath.Length == 0 ? ProjectFile.PackFolder : problem.RelativePath), problem.RelativePath,
            context.Display(problem.Detail, ProjectTreeContext.MaxToolTipChars), context);

    internal static ProjectFolderNode ForMissingPackFolder(ProjectTreeContext context) =>
        new(context.Display(ProjectFile.PackFolder), string.Empty, context.Texts.ProjectPackFolderMissing, context);

    /// <summary>The nodes for the children of a tree entry, in the order of the tree (folders first).</summary>
    internal static IEnumerable<ExplorerNode> CreateChildren(IReadOnlyList<ProjectTreeEntry> entries, ProjectTreeContext context, ISet<string>? expandedFolders) =>
        entries.Select(entry => entry.File is null
            ? (ExplorerNode)new ProjectFolderNode(entry, context, expandedFolders)
            : new ProjectFileNode(entry, context));

    protected override void OnExpandedChanged(bool isExpanded)
    {
        if (isExpanded && !_filled && _folder is not null)
        {
            _filled = true;
            ReplaceChildren(CreateChildren(_folder.Children, _context, null));
        }
    }
}

/// <summary>A file of a project. <see cref="RelativeFile"/> is the path below the pack folder as the listing delivers it, with '\'.</summary>
public sealed class ProjectFileNode : ExplorerNode
{
    internal ProjectFileNode(ProjectTreeEntry file, ProjectTreeContext context) : base(context.Display(file.Name))
    {
        ProjectId = context.ProjectId;
        RelativeFile = file.File!.RelativeFile;
        ToolTip = context.Display(RelativeFile);

        // async void on purpose: a failure of the open action surfaces on the dispatcher, where the global handler shows the crash dialog.
        OpenCommand = new RelayCommand(async () => await context.OpenFile(ProjectId, RelativeFile));
    }

    public Guid ProjectId { get; }

    public string RelativeFile { get; }

    public System.Windows.Input.ICommand OpenCommand { get; }
}
