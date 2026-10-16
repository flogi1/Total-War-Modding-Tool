using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Workspace.Tests;

public class GameTableVersionsCacheTests
{
    private static string Pack(TempDir dir, string name, int version) =>
        new PackBuilder().AddEntry("db/a_tables/data__", GameTableVersionsReaderTests.Table(version)).WriteTo(dir.File(name));

    private static PackSource Source(string path) => GameTableVersionsReaderTests.Source(path, PackOrigin.Vanilla);

    [Fact] public void A_complete_result_is_saved_and_loaded()
    {
        using var dir = new TempDir();
        var packs = new[] { Source(Pack(dir, "a.pack", 4)) };
        var cache = dir.File("cache.json");
        var first = GameTableVersionsCache.LoadOrRead(cache, packs, CancellationToken.None);
        Assert.False(first.FromCache);
        Assert.Null(first.CacheWriteProblem);
        Assert.True(File.Exists(cache));
        var second = GameTableVersionsCache.LoadOrRead(cache, packs, CancellationToken.None);
        Assert.True(second.FromCache);
        Assert.True(second.IsComplete);
        Assert.Equal(4, second.Versions!.VersionOf("a_tables"));
        Assert.Equal(packs[0].Fingerprint, Assert.Single(second.Sources));
    }

    [Fact] public void A_cache_with_another_fingerprint_is_read_again()
    {
        using var dir = new TempDir();
        var path = Pack(dir, "a.pack", 4);
        var cache = dir.File("cache.json");
        GameTableVersionsCache.LoadOrRead(cache, [Source(path)], CancellationToken.None);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));
        var changed = new[] { Source(path) };
        Assert.Null(GameTableVersionsCache.TryLoad(cache, changed));
        Assert.False(GameTableVersionsCache.LoadOrRead(cache, changed, CancellationToken.None).FromCache);
    }

    private static GameTableVersionsResult Cached(string cache, out PackSource[] packs, TempDir dir)
    {
        packs = [Source(Pack(dir, "a.pack", 4)), Source(Pack(dir, "b.pack", 4))];
        return GameTableVersionsCache.LoadOrRead(cache, packs, CancellationToken.None);
    }

    private static PackSource Altered(PackSource source, Func<FileFingerprint, FileFingerprint> change) =>
        source with { Fingerprint = change(source.Fingerprint!) };

    [Fact] public void A_changed_length_is_read_again()
    {
        using var dir = new TempDir();
        var cache = dir.File("cache.json");
        Cached(cache, out var packs, dir);
        var changed = new[] { Altered(packs[0], f => f with { Length = f.Length + 1 }), packs[1] };
        Assert.Null(GameTableVersionsCache.TryLoad(cache, changed));
    }

    [Fact] public void A_changed_pack_count_is_read_again()
    {
        using var dir = new TempDir();
        var cache = dir.File("cache.json");
        Cached(cache, out var packs, dir);
        Assert.Null(GameTableVersionsCache.TryLoad(cache, [packs[0]]));
        Assert.Null(GameTableVersionsCache.TryLoad(cache, [.. packs, Source(Pack(dir, "c.pack", 4))]));
    }

    [Fact] public void A_swapped_pack_order_is_read_again()
    {
        using var dir = new TempDir();
        var cache = dir.File("cache.json");
        Cached(cache, out var packs, dir);
        Assert.Null(GameTableVersionsCache.TryLoad(cache, [packs[1], packs[0]]));
    }

    [Fact] public void A_path_that_differs_only_in_case_is_still_a_hit()
    {
        using var dir = new TempDir();
        var cache = dir.File("cache.json");
        Cached(cache, out var packs, dir);
        var recased = new[] { Altered(packs[0], f => f with { Path = f.Path.ToUpperInvariant() }), packs[1] };
        Assert.True(GameTableVersionsCache.TryLoad(cache, recased)!.FromCache);
    }

    [Fact] public void Without_vanilla_packs_nothing_is_loaded_from_the_cache()
    {
        using var dir = new TempDir();
        var cache = dir.File("cache.json");
        Cached(cache, out _, dir);
        Assert.Null(GameTableVersionsCache.TryLoad(cache, []));
    }

    [Fact] public void A_broken_cache_is_moved_to_bad_and_read_again()
    {
        using var dir = new TempDir();
        var packs = new[] { Source(Pack(dir, "a.pack", 4)) };
        var cache = dir.File("cache.json");
        File.WriteAllText(cache, "{ not json");
        var r = GameTableVersionsCache.LoadOrRead(cache, packs, CancellationToken.None);
        Assert.False(r.FromCache);
        Assert.True(r.IsComplete);
        Assert.Equal("{ not json", File.ReadAllText(cache + ".bad"));
        Assert.True(GameTableVersionsCache.LoadOrRead(cache, packs, CancellationToken.None).FromCache);
    }

    [Fact] public void Saving_an_incomplete_result_throws()
    {
        using var dir = new TempDir();
        var incomplete = new GameTableVersionsResult(null, [], [], [], FromCache: false);
        Assert.Throws<ArgumentException>(() => GameTableVersionsCache.Save(dir.File("cache.json"), incomplete));
        Assert.False(File.Exists(dir.File("cache.json")));
    }

    [Fact] public void A_write_failure_is_reported_and_the_result_stays_complete()
    {
        using var dir = new TempDir();
        var packs = new[] { Source(Pack(dir, "a.pack", 4)) };
        var blocked = dir.File("blocked");
        Directory.CreateDirectory(blocked);
        var r = GameTableVersionsCache.LoadOrRead(blocked, packs, CancellationToken.None);
        Assert.True(r.IsComplete);
        Assert.NotNull(r.CacheWriteProblem);
    }
}
