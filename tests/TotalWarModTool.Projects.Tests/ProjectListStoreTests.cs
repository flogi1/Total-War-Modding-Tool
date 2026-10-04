using TotalWarModTool.Storage;

namespace TotalWarModTool.Projects.Tests;

public class ProjectListStoreTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Save_then_Load_keeps_everything_to_the_tick()
    {
        using var dir = new TempDir();
        var store = new ProjectListStore(dir.Path, "warhammer_3");
        Assert.Equal(Path.Combine(dir.Path, "games", "warhammer_3", "projects.json"), store.FilePath);
        var time = new DateTime(638_000_000_000_000_001, DateTimeKind.Utc);
        var list = new ProjectList([new(A, @"C:\a", new InstalledPack("a.pack", 12345, time)), new(B, @"D:\b", null)], B);
        store.Save(list);
        var loaded = store.Load();
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal(list.Projects, loaded.List.Projects);
        Assert.Equal((B, DateTimeKind.Utc), (loaded.List.ActiveId!.Value, loaded.List.Projects[0].Installed!.LastWriteTimeUtc.Kind));
    }

    [Fact]
    public void A_missing_file_is_an_empty_list_and_a_broken_file_goes_to_bad()
    {
        using var dir = new TempDir();
        var store = new ProjectListStore(dir.Path, "g");
        Assert.Equal((LoadStatus.Missing, 0), (store.Load().Status, store.Load().List.Projects.Count));
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "{ broken");
        var loaded = store.Load();
        Assert.Equal((LoadStatus.Corrupt, 0), (loaded.Status, loaded.List.Projects.Count));
        Assert.True(File.Exists(store.FilePath + ".bad"));
    }

    [Fact]
    public void Load_repairs_the_list()
    {
        using var dir = new TempDir();
        var store = new ProjectListStore(dir.Path, "g");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """
            {"schemaVersion":1,"activeId":"99999999-9999-9999-9999-999999999999","projects":[
              null,
              {"id":"nope","path":"C:\\x"},
              {"id":"11111111-1111-1111-1111-111111111111","path":"relative\\a"},
              {"id":"11111111-1111-1111-1111-111111111111","path":"C:\\a","installed":{"fileName":"","length":1,"lastWriteTimeUtc":"2026-01-02T03:04:05Z"}},
              {"id":"11111111-1111-1111-1111-111111111111","path":"C:\\again"},
              {"id":"22222222-2222-2222-2222-222222222222","path":"c:\\A"},
              {"id":"22222222-2222-2222-2222-222222222222","path":"C:\\b","installed":{"fileName":"b.pack","length":-1,"lastWriteTimeUtc":"2026-01-02T03:04:05Z"}}
            ]}
            """);
        var list = store.Load().List;
        Assert.Equal([(A, @"C:\a"), (B, @"C:\b")], list.Projects.Select(p => (p.Id, p.Path)));
        Assert.All(list.Projects, p => Assert.Null(p.Installed));
        Assert.Equal(A, list.ActiveId);
    }

    [Fact]
    public void Load_reads_an_installed_pack_given_as_iso_text()
    {
        using var dir = new TempDir();
        var store = new ProjectListStore(dir.Path, "g");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """
            {"schemaVersion":1,"projects":[
              {"id":"11111111-1111-1111-1111-111111111111","path":"C:\\a","installed":{"fileName":"a.pack","length":7,"lastWriteTimeUtc":"2026-01-02T03:04:05Z"}},
              {"id":"22222222-2222-2222-2222-222222222222","path":"C:\\b","installed":{"fileName":"b.pack","length":7,"lastWriteTimeUtc":"not a time"}}
            ]}
            """);
        var list = store.Load().List;
        Assert.Equal(new InstalledPack("a.pack", 7, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)), list.Projects[0].Installed);
        Assert.Null(list.Projects[1].Installed);
    }
}