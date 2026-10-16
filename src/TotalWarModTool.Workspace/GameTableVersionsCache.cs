using TotalWarModTool.PackFiles;
using TotalWarModTool.Storage;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Workspace;

/// <summary>
/// Cache file of the game's table versions. It is valid as long as the fingerprints of the vanilla packs (path, size, write time)
/// equal the ones it was written with, in the same order. Only complete results are saved.
/// </summary>
public static class GameTableVersionsCache
{
    private const int SchemaVersion = 1;

    public static GameTableVersionsResult? TryLoad(string path, IReadOnlyList<PackSource> catalogPacks)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(catalogPacks);

        var loaded = JsonFileStore.Load(path, SchemaVersion, () => new CacheFile());
        if (loaded.Status != LoadStatus.Loaded || loaded.Value.Sources is not { } stored || loaded.Value.Versions is not { } versions)
        {
            return null;
        }

        var current = catalogPacks.Where(pack => pack.Origin == PackOrigin.Vanilla).Select(pack => pack.Fingerprint).ToList();
        if (current.Count != stored.Count)
        {
            return null;
        }

        var sources = new List<FileFingerprint>(current.Count);
        for (var i = 0; i < current.Count; i++)
        {
            var now = current[i];
            var then = stored[i];
            if (now is null || then?.Path is null
                || !string.Equals(now.Path, then.Path, StringComparison.OrdinalIgnoreCase)
                || now.Length != then.Length
                || now.LastWriteTimeUtc != then.LastWriteTimeUtc)
            {
                return null;
            }

            sources.Add(now);
        }

        return new GameTableVersionsResult(new GameTableVersions(versions), sources, [], [], FromCache: true);
    }

    public static void Save(string path, GameTableVersionsResult result)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(result);
        if (!result.IsComplete)
        {
            throw new ArgumentException("Only a complete result can be cached.", nameof(result));
        }

        var file = new CacheFile
        {
            Sources = result.Sources.Select(s => (FileFingerprint?)s).ToList(),
            Versions = new Dictionary<string, int>(result.Versions!.Versions, StringComparer.OrdinalIgnoreCase),
        };
        JsonFileStore.Save(path, SchemaVersion, file);
    }

    /// <summary>A cache hit, or a fresh read. A complete fresh result is saved; a write failure is reported in the result and does not invalidate it.</summary>
    public static GameTableVersionsResult LoadOrRead(string path, IReadOnlyList<PackSource> catalogPacks, CancellationToken cancellationToken)
    {
        if (TryLoad(path, catalogPacks) is { } cached)
        {
            return cached;
        }

        var result = GameTableVersionsReader.Read(catalogPacks, cancellationToken);
        if (!result.IsComplete)
        {
            return result;
        }

        try
        {
            Save(path, result);
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return result with { CacheWriteProblem = ex.Message };
        }
    }

    // Path is nullable only because a hand-edited file may omit it.
    private sealed class CacheFile
    {
        public List<FileFingerprint?>? Sources { get; set; } = [];
        public Dictionary<string, int>? Versions { get; set; } = [];
    }
}
