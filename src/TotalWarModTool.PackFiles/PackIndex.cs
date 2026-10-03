namespace TotalWarModTool.PackFiles;

/// <summary>Header, dependency names and file list of a pack. Entry paths use '/' as separator.</summary>
public sealed class PackIndex
{
    private readonly Dictionary<string, PackEntry> _byPath;

    internal PackIndex(FileFingerprint fingerprint, PackHeader header, IReadOnlyList<string> dependencies, IReadOnlyList<PackEntry> entries)
    {
        Fingerprint = fingerprint;
        Header = header;
        Dependencies = dependencies;
        Entries = entries;
        _byPath = new Dictionary<string, PackEntry>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            _byPath.TryAdd(entry.Path, entry);
        }
    }

    public FileFingerprint Fingerprint { get; }

    public PackHeader Header { get; }

    public IReadOnlyList<string> Dependencies { get; }

    public IReadOnlyList<PackEntry> Entries { get; }

    /// <summary>Accepts '/' and '\' and ignores case. With duplicate paths the first entry wins.</summary>
    public PackEntry? Find(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _byPath.GetValueOrDefault(path.Replace('\\', '/'));
    }
}

public sealed record PackIndexResult(PackIndex? Index, PackProblem Problem, string? Detail);
