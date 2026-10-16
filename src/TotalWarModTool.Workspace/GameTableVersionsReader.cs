using TotalWarModTool.PackFiles;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Workspace;

/// <summary>A pack or entry of the game that could not be read. <paramref name="Detail"/> is short technical English, not a UI text.</summary>
public sealed record TableVersionsProblem(string PackPath, string? EntryPath, string Detail);

/// <summary>
/// The table versions of the game's vanilla packs. <see cref="Versions"/> is null exactly when the catalog has no vanilla pack.
/// A result with problems is incomplete: it must be treated as "not read" and is never cached.
/// <see cref="Conflicts"/> are short technical English lines for the log. <see cref="CacheWriteProblem"/> is set when a complete
/// result could not be written to the cache; the result stays valid for the session.
/// </summary>
public sealed record GameTableVersionsResult(
    GameTableVersions? Versions,
    IReadOnlyList<FileFingerprint> Sources,
    IReadOnlyList<TableVersionsProblem> Problems,
    IReadOnlyList<string> Conflicts,
    bool FromCache,
    string? CacheWriteProblem = null)
{
    public bool IsComplete => Versions is not null && Problems.Count == 0;
}

/// <summary>Reads the version of every db table in the vanilla packs from the first bytes of each entry.</summary>
public static class GameTableVersionsReader
{
    public static GameTableVersionsResult Read(IReadOnlyList<PackSource> catalogPacks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalogPacks);

        var versions = new Dictionary<string, (int Version, string Pack)>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<FileFingerprint>();
        var problems = new List<TableVersionsProblem>();
        var conflicts = new List<string>();
        var vanillaCount = 0;

        foreach (var pack in catalogPacks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pack.Origin != PackOrigin.Vanilla)
            {
                continue;
            }

            vanillaCount++;
            if (pack.Fingerprint is not null)
            {
                sources.Add(pack.Fingerprint);
            }

            ReadPack(pack, versions, problems, conflicts, cancellationToken);
        }

        if (vanillaCount == 0)
        {
            return new GameTableVersionsResult(null, sources, problems, conflicts, FromCache: false);
        }

        var map = versions.ToDictionary(pair => pair.Key, pair => pair.Value.Version, StringComparer.OrdinalIgnoreCase);
        return new GameTableVersionsResult(new GameTableVersions(map), sources, problems, conflicts, FromCache: false);
    }

    private static void ReadPack(
        PackSource pack,
        Dictionary<string, (int Version, string Pack)> versions,
        List<TableVersionsProblem> problems,
        List<string> conflicts,
        CancellationToken cancellationToken)
    {
        if (pack.Problem != PackProblem.None || pack.Fingerprint is null)
        {
            problems.Add(new TableVersionsProblem(pack.Path, null, $"The pack is not readable ({pack.Problem}): {pack.Detail}"));
            return;
        }

        var indexResult = PackReader.ReadIndex(pack.Path);
        if (indexResult.Index is not { } index)
        {
            problems.Add(new TableVersionsProblem(pack.Path, null, $"The pack index could not be read ({indexResult.Problem}): {indexResult.Detail}"));
            return;
        }

        foreach (var entry in index.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!DbTablePath.TryGetTableName(entry.Path, out var table))
            {
                continue;
            }

            var content = PackReader.ReadEntryPrefix(index, entry, DbTableDecoder.TableHeaderBytes, cancellationToken);
            if (content.Problem != EntryProblem.None || content.Bytes is null)
            {
                problems.Add(new TableVersionsProblem(pack.Path, entry.Path, $"The entry could not be read ({content.Problem}): {content.Detail}"));
                continue;
            }

            if (DbTableDecoder.ReadHeader(content.Bytes) is not { } header)
            {
                problems.Add(new TableVersionsProblem(pack.Path, entry.Path, "The table header is broken or cut off."));
                continue;
            }

            if (versions.TryGetValue(table, out var earlier) && earlier.Version != header.Version)
            {
                conflicts.Add($"{table}: version {earlier.Version} in {earlier.Pack}, version {header.Version} in {pack.Name}; the later one is used");
            }

            versions[table] = (header.Version, pack.Name);
        }
    }
}
