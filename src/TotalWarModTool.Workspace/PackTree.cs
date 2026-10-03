using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Workspace;

/// <summary>
/// A folder (<see cref="Entry"/> is null) or a file of a pack. <see cref="Path"/> is the full path inside the pack with '/' and is
/// empty for the root. For a file it is the path of its entry; for a folder it is built from the names above it, on first use.
/// </summary>
public sealed class PackTreeNode
{
    private static readonly IReadOnlyList<PackTreeNode> NoChildren = [];

    private readonly PackTreeNode? _parent;
    private readonly List<PackTreeNode>? _children;
    private string? _path;

    // Only used while the tree is built; cleared afterwards.
    private Dictionary<string, PackTreeNode>? _folderByName;
    private HashSet<string>? _fileNames;

    internal PackTreeNode(string name, PackTreeNode? parent, PackEntry? entry)
    {
        Name = name;
        _parent = parent;
        Entry = entry;
        if (entry is null)
        {
            _children = [];
        }
        else
        {
            _path = entry.Path;
        }
    }

    public string Name { get; }

    public string Path => _path ??= BuildPath();

    public PackEntry? Entry { get; }

    /// <summary>Folders first, then files, each sorted by name, ordinal ignoring case. Empty for a file.</summary>
    public IReadOnlyList<PackTreeNode> Children => _children ?? NoChildren;

    internal PackTreeNode GetOrAddFolder(string name, List<PackTreeNode> allFolders)
    {
        _folderByName ??= new Dictionary<string, PackTreeNode>(StringComparer.OrdinalIgnoreCase);
        if (!_folderByName.TryGetValue(name, out var folder))
        {
            folder = new PackTreeNode(name, this, null);
            _folderByName.Add(name, folder);
            _children!.Add(folder);
            allFolders.Add(folder);
        }

        return folder;
    }

    internal void AddFile(string name, PackEntry entry)
    {
        _fileNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_fileNames.Add(name))
        {
            _children!.Add(new PackTreeNode(name, this, entry));
        }
    }

    internal void Seal()
    {
        _folderByName = null;
        _fileNames = null;
        _children?.Sort(Compare);
    }

    private static int Compare(PackTreeNode a, PackTreeNode b)
    {
        var byKind = (a.Entry is null ? 0 : 1).CompareTo(b.Entry is null ? 0 : 1);
        return byKind != 0 ? byKind : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
    }

    // Walks up instead of recursing: a path can have thousands of segments.
    private string BuildPath()
    {
        var names = new List<string>();
        for (var node = this; node._parent is not null; node = node._parent)
        {
            names.Add(node.Name);
        }

        names.Reverse();
        return string.Join('/', names);
    }
}

public static class PackTree
{
    private const int CancellationCheckInterval = 1000;

    /// <summary>
    /// Builds the folder tree of an index without recursion. Empty path segments and entries with an empty path are skipped, folders
    /// that differ only in case merge (the first spelling counts), and of duplicate file paths the first entry counts.
    /// Throws <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    public static PackTreeNode Build(IReadOnlyList<PackEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var root = new PackTreeNode(string.Empty, null, null);
        var allFolders = new List<PackTreeNode> { root };
        for (var i = 0; i < entries.Count; i++)
        {
            if (i % CancellationCheckInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var entry = entries[i];
            var segments = entry.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var folder = root;
            for (var s = 0; s < segments.Length - 1; s++)
            {
                folder = folder.GetOrAddFolder(segments[s], allFolders);
            }

            folder.AddFile(segments[^1], entry);
        }

        for (var i = 0; i < allFolders.Count; i++)
        {
            if (i % CancellationCheckInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            allFolders[i].Seal();
        }

        return root;
    }
}
