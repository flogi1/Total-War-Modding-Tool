using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// One row of the explorer tree. <see cref="IsExpanded"/> is bound two-way by the tree and set by the view model; only a change made through
/// the property runs <see cref="OnExpandedChanged"/>. The view model corrects the state of a node with <see cref="SetExpandedQuietly"/>.
/// </summary>
public abstract class ExplorerNode : ObservableObject
{
    private readonly ChildCollection _children = new();
    private string _name;
    private string? _toolTip;
    private bool _hasProblem;
    private bool _isExpanded;

    protected ExplorerNode(string name)
    {
        _name = name;
    }

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    /// <summary>Replaces the name of a node whose name is text of the current language (a group).</summary>
    internal void Rename(string name) => Name = name;

    /// <summary>The name, also what screen readers and UI automation call the row.</summary>
    public override string ToString() => Name;

    public string? ToolTip
    {
        get => _toolTip;
        protected set => SetProperty(ref _toolTip, value);
    }

    public bool HasProblem
    {
        get => _hasProblem;
        protected set => SetProperty(ref _hasProblem, value);
    }

    public ObservableCollection<ExplorerNode> Children => _children;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnExpandedChanged(value);
            }
        }
    }

    /// <summary>Runs after <see cref="IsExpanded"/> changed through the property.</summary>
    protected virtual void OnExpandedChanged(bool isExpanded)
    {
    }

    /// <summary>Changes <see cref="IsExpanded"/> and tells the view, without running <see cref="OnExpandedChanged"/>.</summary>
    internal void SetExpandedQuietly(bool isExpanded) => SetProperty(ref _isExpanded, isExpanded, nameof(IsExpanded));

    /// <summary>Replaces all children with one change notification: a folder can hold tens of thousands of nodes.</summary>
    internal void ReplaceChildren(IEnumerable<ExplorerNode> children) => _children.ReplaceAll(children);

    private sealed class ChildCollection : ObservableCollection<ExplorerNode>
    {
        public void ReplaceAll(IEnumerable<ExplorerNode> items)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}

/// <summary>The one child that makes a node expandable before its real children exist. Shown without content.</summary>
public sealed class PlaceholderNode : ExplorerNode
{
    public PlaceholderNode() : base(string.Empty)
    {
    }
}

/// <summary>The packs of one origin. Name: "&lt;origin name&gt; (&lt;count&gt;)".</summary>
public sealed class GroupNode : ExplorerNode
{
    internal GroupNode(PackOrigin origin, string name, IReadOnlyList<PackNode> packs, bool isExpanded) : base(name)
    {
        Origin = origin;
        ReplaceChildren(packs);
        SetExpandedQuietly(isExpanded);
    }

    public PackOrigin Origin { get; }
}

/// <summary>
/// A pack. A pack that can be expanded carries a <see cref="PlaceholderNode"/> until the view model has read its index; one that cannot
/// (<see cref="CanExpand"/> false) has no children. The view model owns the loading state.
/// </summary>
public sealed class PackNode : ExplorerNode
{
    private readonly Func<PackSource, string, Task> _openEntry;
    private readonly Action<PackNode, bool> _expandedChanged;
    private readonly bool _baseHasProblem;
    private readonly string? _baseToolTip;

    internal PackNode(PackSource pack, AppTexts texts, string? expectedFormat, Func<PackSource, string, Task> openEntry,
        Action<PackNode, bool> expandedChanged)
        : base(pack.Origin == PackOrigin.Workshop && pack.WorkshopId is not null ? $"{pack.Name} ({pack.WorkshopId})" : pack.Name)
    {
        Pack = pack;
        _openEntry = openEntry;
        _expandedChanged = expandedChanged;

        ToolTip = pack.Path;
        if (pack.Problem != PackProblem.None)
        {
            HasProblem = true;
            ToolTip = Describe(texts.PackProblemText(pack.Problem, expectedFormat), pack.Detail);
            CanExpand = false;
        }
        else if (pack.Header is { HasEncryptedIndex: true })
        {
            HasProblem = true;
            ToolTip = texts.ExplorerEncrypted;
            CanExpand = false;
        }
        else
        {
            CanExpand = true;
            if (pack.Header is { HasEncryptedData: true })
            {
                HasProblem = true;
                ToolTip = texts.ExplorerEncrypted;
            }

            ReplaceChildren([new PlaceholderNode()]);
        }

        _baseHasProblem = HasProblem;
        _baseToolTip = ToolTip;
    }

    public PackSource Pack { get; }

    public bool CanExpand { get; }

    /// <summary>The children show the tree of the pack (set by the view model).</summary>
    internal bool IsOpen { get; private set; }

    /// <summary>The index is being read (set by the view model).</summary>
    internal bool IsLoading { get; set; }

    /// <summary>The node belongs to a tree that was replaced; its results no longer count (set by the view model).</summary>
    internal bool IsDetached { get; set; }

    internal static string Describe(string text, string? detail) =>
        string.IsNullOrEmpty(detail) ? text : text + "\n\n" + detail;

    /// <summary>Shows the tree of the pack as the children and forgets an earlier mark that came from a failed read.</summary>
    internal void ShowContent(PackContent content)
    {
        IsOpen = true;
        ClearIndexProblem();
        ReplaceChildren(FolderNode.CreateChildren(content.Tree, Pack, _openEntry));
    }

    /// <summary>Back to the state of a collapsed pack: one placeholder as the child.</summary>
    internal void ShowPlaceholder()
    {
        IsOpen = false;
        ReplaceChildren([new PlaceholderNode()]);
    }

    internal void MarkIndexProblem(string toolTip)
    {
        HasProblem = true;
        ToolTip = toolTip;
    }

    private void ClearIndexProblem()
    {
        HasProblem = _baseHasProblem;
        ToolTip = _baseToolTip;
    }

    protected override void OnExpandedChanged(bool isExpanded) => _expandedChanged(this, isExpanded);
}

/// <summary>A folder inside a pack. Its children are created when it is expanded for the first time.</summary>
public sealed class FolderNode : ExplorerNode
{
    private readonly PackTreeNode _folder;
    private readonly PackSource _pack;
    private readonly Func<PackSource, string, Task> _openEntry;
    private bool _filled;

    internal FolderNode(PackTreeNode folder, PackSource pack, Func<PackSource, string, Task> openEntry) : base(folder.Name)
    {
        _folder = folder;
        _pack = pack;
        _openEntry = openEntry;
        if (folder.Children.Count > 0)
        {
            ReplaceChildren([new PlaceholderNode()]);
        }
    }

    /// <summary>The nodes for the children of a tree node, in the order of the tree (folders first).</summary>
    internal static IEnumerable<ExplorerNode> CreateChildren(PackTreeNode parent, PackSource pack, Func<PackSource, string, Task> openEntry) =>
        parent.Children.Select(child => child.Entry is null
            ? (ExplorerNode)new FolderNode(child, pack, openEntry)
            : new EntryNode(child, pack, openEntry));

    protected override void OnExpandedChanged(bool isExpanded)
    {
        if (isExpanded && !_filled)
        {
            _filled = true;
            ReplaceChildren(CreateChildren(_folder, _pack, _openEntry));
        }
    }
}

/// <summary>A file of a pack. <see cref="EntryPath"/> is the path of its entry inside the pack, as the pack delivers it.</summary>
public sealed class EntryNode : ExplorerNode
{
    internal EntryNode(PackTreeNode file, PackSource pack, Func<PackSource, string, Task> openEntry) : base(file.Name)
    {
        Pack = pack;
        EntryPath = file.Entry!.Path;

        // async void on purpose: a failure of the open action surfaces on the dispatcher, where the global handler shows the crash dialog.
        OpenCommand = new RelayCommand(async () => await openEntry(pack, EntryPath));
    }

    public PackSource Pack { get; }

    public string EntryPath { get; }

    public ICommand OpenCommand { get; }
}
