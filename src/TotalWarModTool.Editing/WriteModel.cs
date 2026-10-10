using TotalWarModTool.Objects;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Editing;

/// <summary>Answers whether a Vanilla or Mod pack of the load order holds an entry with this pack path (with '/', compared OrdinalIgnoreCase). A project file whose pack path it answers true for is a copy (Spec 5.4).</summary>
public delegate bool PackEntryExists(string packPath);

/// <summary>What the snapshot must hold for one ObjectChanges: the tables whose project files are read, whether loc files are read, and the pack entries that a file copy needs.</summary>
public sealed record SnapshotRequest(IReadOnlySet<string> Tables, bool Loc, IReadOnlyList<PackEntryRequest> PackEntries)
{
    /// <summary>
    /// Tables of every RowChange (OrdinalIgnoreCase); Loc when Texts is not empty; for every Delete one PackEntryRequest per distinct
    /// (Source.Path, EntryPath): its own Provenance.Source with Provenance.EntryPath when the source is not Project, and every OverriddenLayer
    /// whose Source is not Project. Duplicates removed, order of first appearance.
    /// </summary>
    public static SnapshotRequest For(ObjectChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<PackEntryRequest>();
        var seen = new HashSet<(string Pack, string Entry)>();

        void Add(TableSource source, string entryPath)
        {
            if (source.Kind != SourceKind.Project && seen.Add((source.Path.ToUpperInvariant(), entryPath.ToUpperInvariant())))
            {
                entries.Add(new PackEntryRequest(source, entryPath));
            }
        }

        foreach (var row in changes.Rows)
        {
            tables.Add(row.Table);
            if (row.Kind == RowChangeKind.Delete && row.Provenance is { } provenance)
            {
                Add(provenance.Source, provenance.EntryPath);
                foreach (var layer in provenance.OverriddenLayers)
                {
                    Add(layer.Source, layer.EntryPath);
                }
            }
        }

        return new SnapshotRequest(tables, changes.Texts.Count > 0, entries);
    }
}

public sealed record PackEntryRequest(TableSource Source, string EntryPath);

/// <summary>A project table file: RelativeFile below pack\ with '\', its pack path, fingerprint, text and parsed table. IsCopy: a Vanilla or Mod pack of the load order has an entry with the same pack path.</summary>
public sealed record SnapshotTableFile(string RelativeFile, string PackPath, FileFingerprint Fingerprint, string Text, TsvTable Table, bool IsCopy);

public sealed record SnapshotLocFile(string RelativeFile, string PackPath, FileFingerprint Fingerprint, string Text, TsvLoc Loc, bool IsCopy);

/// <summary>A pack entry adopted as a project file would be (NewFiles.Adopt, version of the source): RelativeFile is where the copy goes.</summary>
public sealed record SnapshotPackEntry(TableSource Source, string EntryPath, string RelativeFile, TsvTable Table);

public enum SnapshotProblemKind { ProjectFileUnreadable, ProjectFileInvalid, PackUnreadable, PackEntryMissing, PackEntryUnreadable, PackEntryNotAdoptable }

/// <summary>File is the project file (relative, '\') or the pack entry path; Source the pack for pack problems. Detail is short technical English.</summary>
public sealed record SnapshotProblem(SnapshotProblemKind Kind, string File, TableSource? Source, string Detail);

/// <summary>
/// The read state the plan computes from. Tables holds every project table file of every requested table (copies included), Locs every project
/// loc file when requested, PackEntries one per request that could be read. Problems: anything that could not be read; the plan then refuses
/// (nothing is written on a problem).
/// </summary>
public sealed record ProjectSnapshot(IReadOnlyList<SnapshotTableFile> Tables, IReadOnlyList<SnapshotLocFile> Locs,
    IReadOnlyList<SnapshotPackEntry> PackEntries, IReadOnlyList<SnapshotProblem> Problems)
{
    public static readonly ProjectSnapshot Empty = new([], [], [], []);
}
