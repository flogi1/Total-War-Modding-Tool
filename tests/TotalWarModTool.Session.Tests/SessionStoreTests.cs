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

    private static SessionTab ObjectTab(string table, string[] firstKey, params SessionObjectRef[] history) =>
        new("object:" + table, table + "|" + string.Concat(firstKey), new SessionObjectTab(history, history.Length - 1));

    [Fact] public void Round_trip_keeps_object_tabs_with_history_and_position()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        var history = new[]
        {
            new SessionObjectRef("x_tables", ["k1"], @"D:\mod.pack"),
            new SessionObjectRef("y_tables", ["k2", "v"], null),
            new SessionObjectRef("x_tables", ["k3"], null),
        };
        store.Save(new SessionState([new(@"D:\a.pack", "a.txt"), new("object:x_tables", "x_tables|k1", new SessionObjectTab(history, 1))], 1, []));

        var state = store.Load().State;

        Assert.Equal(2, state.Tabs.Count);
        Assert.Null(state.Tabs[0].Object);
        var tab = state.Tabs[1];
        Assert.Equal(("object:x_tables", "x_tables|k1"), (tab.PackPath, tab.EntryPath));
        var loaded = tab.Object!;
        Assert.Equal(1, loaded.Position);
        Assert.Equal(3, loaded.History.Count);
        for (var i = 0; i < history.Length; i++)
        {
            Assert.Equal(history[i].Table, loaded.History[i].Table);
            Assert.Equal(history[i].KeyValues, loaded.History[i].KeyValues);
            Assert.Equal(history[i].RootPackPath, loaded.History[i].RootPackPath);
        }

        Assert.Equal(1, state.ActiveTab);
    }

    // Review Focus 1: the key values are data and come back exactly, whatever they contain.
    [Fact] public void An_object_tab_with_special_characters_in_the_key_comes_back_as_it_was()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        string[] key = ["a/b", "c:d|e", " f "];
        store.Save(new SessionState([ObjectTab("x_tables", key, new SessionObjectRef("x_tables", key, null))], 0, []));

        var loaded = store.Load().State.Tabs.Single().Object!;

        Assert.Equal(key, Assert.Single(loaded.History).KeyValues);
    }

    [Fact] public void A_file_without_the_object_field_loads_as_before()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """
            {"schemaVersion":1,"activeTab":1,"expandedPacks":[],
             "tabs":[{"packPath":"D:\\a.pack","entryPath":"a.txt"},{"packPath":"D:\\b.pack","entryPath":"b.txt"}]}
            """);

        var loaded = store.Load();

        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal([new SessionTab(@"D:\a.pack", "a.txt"), new SessionTab(@"D:\b.pack", "b.txt")], loaded.State.Tabs);
        Assert.All(loaded.State.Tabs, tab => Assert.Null(tab.Object));
        Assert.Equal(1, loaded.State.ActiveTab);
    }

    [Fact] public void Repair_drops_object_tabs_without_history_or_table_and_clamps_the_position()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """
            {"schemaVersion":1,"activeTab":6,"tabs":[
              {"packPath":"object:x","entryPath":"e1","object":{"history":[],"position":0}},
              {"packPath":"object:x","entryPath":"e2","object":{"history":null,"position":0}},
              {"packPath":"object:x","entryPath":"e3","object":{"position":0}},
              {"packPath":"object:x","entryPath":"e4","object":{"history":[{"table":"","keyValues":["k"]}],"position":0}},
              {"packPath":"object:x","entryPath":"e5","object":{"history":[{"table":"x_tables","keyValues":[]}],"position":0}},
              {"packPath":"object:x","entryPath":"e6","object":{"history":[{"table":"x_tables","keyValues":["a",null]}],"position":0}},
              {"packPath":"object:x","entryPath":"e7","object":{"history":[{"table":"x_tables","keyValues":["k1"]},null],"position":0}},
              {"packPath":"object:x","entryPath":"ok","object":{"history":[{"table":"x_tables","keyValues":["k1"]},{"table":"x_tables","keyValues":["k2"],"rootPackPath":"D:\\m.pack"}],"position":9}},
              {"packPath":"object:x","entryPath":"neg","object":{"history":[{"table":"x_tables","keyValues":["k1"]},{"table":"x_tables","keyValues":["k2"]}],"position":-4}},
              {"packPath":"object:x","entryPath":"nopos","object":{"history":[{"table":"x_tables","keyValues":["k1"]}]}}
            ]}
            """);

        var state = store.Load().State;

        Assert.Equal(["ok", "neg", "nopos"], state.Tabs.Select(t => t.EntryPath));
        var ok = state.Tabs[0].Object!;
        Assert.Equal((2, 1), (ok.History.Count, ok.Position));
        Assert.Equal(@"D:\m.pack", ok.History[1].RootPackPath);
        Assert.Equal(0, state.Tabs[1].Object!.Position);
        Assert.Equal(0, state.Tabs[2].Object!.Position);
        Assert.Equal(0, state.ActiveTab);                    // index 6 pointed at a dropped tab
    }

    [Fact] public void A_history_longer_than_the_limit_keeps_its_last_entries_and_the_position_follows()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        var history = Enumerable.Range(0, 60).Select(i => new SessionObjectRef("x_tables", [$"k{i}"], null)).ToList();
        store.Save(new SessionState([new("object:x_tables", "x_tables|k0", new SessionObjectTab(history, 55))], 0, []));

        var loaded = store.Load().State.Tabs.Single().Object!;

        Assert.Equal(50, loaded.History.Count);
        Assert.Equal("k10", loaded.History[0].KeyValues[0]);
        Assert.Equal("k59", loaded.History[^1].KeyValues[0]);
        Assert.Equal("k55", loaded.History[loaded.Position].KeyValues[0]);
    }

    [Fact] public void Two_object_tabs_of_the_same_object_both_survive()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, "testgame");
        var one = ObjectTab("x_tables", ["k1"], new SessionObjectRef("x_tables", ["k1"], null));
        var two = ObjectTab("x_tables", ["k1"], new SessionObjectRef("x_tables", ["k1"], null), new SessionObjectRef("x_tables", ["k2"], null));
        store.Save(new SessionState([one, two], 1, []));

        var state = store.Load().State;

        Assert.Equal(2, state.Tabs.Count);
        Assert.Equal((1, 2), (state.Tabs[0].Object!.History.Count, state.Tabs[1].Object!.History.Count));
        Assert.Equal(1, state.ActiveTab);
    }
}
