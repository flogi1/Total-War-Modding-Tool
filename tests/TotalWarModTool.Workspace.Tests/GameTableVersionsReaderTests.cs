using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Workspace.Tests;

public class GameTableVersionsReaderTests
{
    internal static byte[] Zstd(byte[] data) { using var c = new ZstdSharp.Compressor(); return c.Wrap(data).ToArray(); }

    /// <summary>A table with no rows. Version 0 has no version marker.</summary>
    internal static byte[] Table(int version) =>
        version == 0
            ? [0, 0, 0, 0, 0]
            : [0xFC, 0xFD, 0xFE, 0xFF, .. BitConverter.GetBytes(version), 0, 0, 0, 0, 0];

    internal static PackSource Source(string path, PackOrigin origin) =>
        new(path, Path.GetFileName(path), origin, null, FileFingerprint.TryRead(path), null, PackProblem.None, null);

    [Fact] public void Reads_the_version_of_every_db_table_of_vanilla_packs()
    {
        using var dir = new TempDir();
        var a = new PackBuilder(packType: 1)
            .AddEntry("db/units_tables/data__", Table(3))
            .AddEntry("db/old_tables/data__", PackBuilder.Stored(Zstd(Table(0)), Table(0).Length), compressed: true)
            .AddEntry("text/readme.txt", [1, 2, 3])
            .WriteTo(dir.File("a.pack"));
        var r = GameTableVersionsReader.Read([Source(a, PackOrigin.Vanilla)], CancellationToken.None);
        Assert.True(r.IsComplete);
        Assert.False(r.FromCache);
        Assert.Equal(3, r.Versions!.VersionOf("UNITS_TABLES"));
        Assert.Equal(0, r.Versions.VersionOf("old_tables"));
        Assert.Equal(2, r.Versions.Versions.Count);
        Assert.Single(r.Sources);
        Assert.Empty(r.Conflicts);
    }

    [Fact] public void Mod_and_workshop_packs_do_not_count()
    {
        using var dir = new TempDir();
        var van = new PackBuilder().AddEntry("db/a_tables/data__", Table(1)).WriteTo(dir.File("van.pack"));
        var mod = new PackBuilder().AddEntry("db/b_tables/data__", Table(2)).WriteTo(dir.File("mod.pack"));
        var ws = new PackBuilder().AddEntry("db/a_tables/data__", Table(9)).WriteTo(dir.File("ws.pack"));
        var r = GameTableVersionsReader.Read([Source(van, PackOrigin.Vanilla), Source(mod, PackOrigin.DataFolderMod), Source(ws, PackOrigin.Workshop)], CancellationToken.None);
        Assert.Equal(1, r.Versions!.VersionOf("a_tables"));
        Assert.Null(r.Versions.VersionOf("b_tables"));
        Assert.Single(r.Sources);
    }

    [Fact] public void The_later_vanilla_pack_wins_and_the_conflict_is_named()
    {
        using var dir = new TempDir();
        var first = new PackBuilder().AddEntry("db/a_tables/data__", Table(1)).WriteTo(dir.File("first.pack"));
        var second = new PackBuilder().AddEntry("db/a_tables/data__", Table(2)).WriteTo(dir.File("second.pack"));
        var r = GameTableVersionsReader.Read([Source(first, PackOrigin.Vanilla), Source(second, PackOrigin.Vanilla)], CancellationToken.None);
        Assert.Equal(2, r.Versions!.VersionOf("a_tables"));
        var conflict = Assert.Single(r.Conflicts);
        Assert.Contains("a_tables", conflict);
        Assert.Contains("first.pack", conflict);
        Assert.Contains("second.pack", conflict);
        Assert.Contains("version 1", conflict);
        Assert.Contains("version 2", conflict);
    }

    [Fact] public void An_unreadable_pack_makes_the_result_incomplete_and_nothing_is_cached()
    {
        using var dir = new TempDir();
        var pack = new PackBuilder().AddEntry("db/a_tables/data__", Table(1)).WriteTo(dir.File("locked.pack"));
        var source = Source(pack, PackOrigin.Vanilla);
        var cache = dir.File("cache.json");
        using (new FileStream(pack, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var r = GameTableVersionsCache.LoadOrRead(cache, [source], CancellationToken.None);
            Assert.False(r.IsComplete);
            var problem = Assert.Single(r.Problems);
            Assert.Equal(pack, problem.PackPath);
            Assert.Null(problem.EntryPath);
        }

        Assert.False(File.Exists(cache));
    }

    [Fact] public void A_broken_header_makes_the_result_incomplete()
    {
        using var dir = new TempDir();
        var pack = new PackBuilder()
            .AddEntry("db/a_tables/data__", Table(1))
            .AddEntry("db/b_tables/data__", [0xFC, 0xFD, 0xFE, 0xFF, 1])
            .WriteTo(dir.File("p.pack"));
        var r = GameTableVersionsReader.Read([Source(pack, PackOrigin.Vanilla)], CancellationToken.None);
        Assert.False(r.IsComplete);
        var problem = Assert.Single(r.Problems);
        Assert.Equal((pack, "db/b_tables/data__"), (problem.PackPath, problem.EntryPath));
    }

    [Fact] public void No_vanilla_pack_gives_no_versions()
    {
        using var dir = new TempDir();
        var mod = new PackBuilder().AddEntry("db/a_tables/data__", Table(1)).WriteTo(dir.File("mod.pack"));
        var r = GameTableVersionsReader.Read([Source(mod, PackOrigin.DataFolderMod)], CancellationToken.None);
        Assert.Null(r.Versions);
        Assert.False(r.IsComplete);
    }
}
