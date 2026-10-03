using TotalWarModTool.Storage;

namespace TotalWarModTool.Session.Tests;

public class SessionStoreTests
{
    [Fact] public void Round_trip_keeps_tabs_active_tab_and_expanded_packs()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        Assert.Equal(Path.Combine(dir.Path, "games", "testgame", "session.json"), store.FilePath);
        var state = new SessionState([new(@"D:\a.pack", "db/x_tables/data__"), new(@"D:\b.pack", "script/main.lua")], 1, [@"D:\a.pack"]);
        store.Save(state);
        var loaded = store.Load();
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal(state.Tabs, loaded.State.Tabs);
        Assert.Equal(1, loaded.State.ActiveTab);
        Assert.Equal(state.ExpandedPacks, loaded.State.ExpandedPacks);
    }

    [Fact] public void Missing_file_gives_the_empty_state()
    {
        using var dir = new TempDir();
        var loaded = new SessionStore(dir.Path, "testgame").Load();
        Assert.Equal((LoadStatus.Missing, 0, -1), (loaded.Status, loaded.State.Tabs.Count, loaded.State.ActiveTab));
    }

    [Fact] public void Corrupt_file_is_copied_to_bad_and_the_empty_state_is_used()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "[1,2");
        var loaded = store.Load();
        Assert.Equal((LoadStatus.Corrupt, 0), (loaded.Status, loaded.State.Tabs.Count));
        Assert.True(File.Exists(loaded.BadCopyPath));
    }

    [Fact] public void Load_repairs_the_state()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """
            {"schemaVersion":1,"activeTab":3,"expandedPacks":["D:\\a.pack","","d:\\A.PACK",null],
             "tabs":[{"packPath":"","entryPath":"x"},{"packPath":"D:\\a.pack","entryPath":"db/x"},
                     {"packPath":"d:\\A.pack","entryPath":"DB/X"},{"packPath":"D:\\a.pack","entryPath":null},
                     {"packPath":"D:\\b.pack","entryPath":"y"},null]}
            """);
        var state = store.Load().State;
        Assert.Equal([new SessionTab(@"D:\a.pack", "db/x"), new SessionTab(@"D:\b.pack", "y")], state.Tabs);
        Assert.Equal(0, state.ActiveTab);                      // index 3 pointed at a tab that was dropped as invalid
        Assert.Equal([@"D:\a.pack"], state.ExpandedPacks);
    }

    [Fact] public void Active_tab_follows_its_tab_when_earlier_tabs_are_dropped()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """
            {"schemaVersion":1,"activeTab":2,"tabs":[{"packPath":"","entryPath":"x"},{"packPath":"D:\\a.pack","entryPath":"a"},{"packPath":"D:\\b.pack","entryPath":"b"}]}
            """);
        var state = store.Load().State;
        Assert.Equal((2, 1), (state.Tabs.Count, state.ActiveTab));
        Assert.Equal("b", state.Tabs[state.ActiveTab].EntryPath);
    }

    [Fact] public void More_tabs_than_the_limit_are_cut()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        store.Save(new SessionState(Enumerable.Range(0, SessionStore.MaxTabs + 5).Select(i => new SessionTab(@"D:\a.pack", $"e{i}")).ToList(), 0, []));
        Assert.Equal(SessionStore.MaxTabs, store.Load().State.Tabs.Count);
    }
}
