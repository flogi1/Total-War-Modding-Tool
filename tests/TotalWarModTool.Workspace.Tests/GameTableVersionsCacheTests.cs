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
