using TotalWarModTool.Session;
using TotalWarModTool.Storage;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class SessionCoordinatorTests
{
    private static async Task<(TestGame Game, WorkspaceController C, MainViewModel Vm, SessionStore Store)> Create(Action<TestGame> arrange)
    {
        var game = new TestGame(); arrange(game);
        var c = game.NewController();
        await c.LoadAsync();
        return (game, c, new MainViewModel(game.Texts, game.Paths, _ => { }, game.Runner, () => { }, c), new SessionStore(game.Paths.RoamingRoot, "testgame"));
    }
    private static SessionCoordinator Coordinator(TestGame game, WorkspaceController c, MainViewModel vm, SessionStore store) =>
        new(store, store.Load(), c, vm.Explorer, vm.Tabs, game.Log);

    [Fact] public async Task Restore_reopens_tabs_selection_and_expanded_packs()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", "one"u8.ToArray()), ("db/x_tables/data__", TestGame.TableBytes)));
        using (game)
        {
            var path = c.Catalog!.Packs[0].Path;
            store.Save(new SessionState([new(path.ToUpperInvariant(), "A.TXT"), new(path, "db/x_tables/data__")], 1, [path]));
            var session = Coordinator(game, c, vm, store);
            await session.RestoreAsync();
            Assert.Equal(["a.txt", "data__"], vm.Tabs.Tabs.Select(t => t.Title));
            Assert.Equal(path, vm.Tabs.Tabs[0].PackPath);                              // the spelling of the catalog
            Assert.IsType<TableTabViewModel>(vm.Tabs.SelectedTab);
            Assert.Equal([path], vm.Explorer.ExpandedPackPaths);
            Assert.Empty(game.Runner.Titles);                                          // silent
        }
    }

    // Review Focus 2
    [Fact] public async Task Restore_drops_tabs_of_missing_packs_and_entries()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1]), ("b.txt", [1])));
        using (game)
        {
            var path = c.Catalog!.Packs[0].Path;
            store.Save(new SessionState([new(@"D:\gone.pack", "x.txt"), new(path, "gone.txt"), new(path, "b.txt"), new(path, "a.txt")], 2, [@"D:\gone.pack"]));
            var session = Coordinator(game, c, vm, store); var dirty = 0; session.Dirty += (_, _) => dirty++;
            await session.RestoreAsync();
            Assert.Equal(["b.txt", "a.txt"], vm.Tabs.Tabs.Select(t => t.Title));
            Assert.Equal("b.txt", vm.Tabs.SelectedTab!.Title);                         // the tab that was active before
            Assert.Empty(vm.Explorer.ExpandedPackPaths);
            Assert.Equal(1, dirty);
            Assert.True(game.Log.Infos.Count >= 2);                                    // one entry per dropped tab, written by the controller
        }
    }

    [Fact] public async Task Save_writes_tabs_selection_and_expanded_packs()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1]), ("b.txt", [1])));
        using (game)
        {
            var session = Coordinator(game, c, vm, store); var dirty = 0; session.Dirty += (_, _) => dirty++;
            await session.RestoreAsync();
            var pack = c.Catalog!.Packs[0];
            await vm.OpenEntryAsync(pack, "a.txt"); await vm.OpenEntryAsync(pack, "b.txt");
            vm.Tabs.SelectedTab = vm.Tabs.Tabs[0];
            vm.Explorer.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single().IsExpanded = true;
            Assert.True(dirty >= 3);
            session.Save();
            var saved = store.Load().State;
            Assert.Equal([new SessionTab(pack.Path, "a.txt"), new SessionTab(pack.Path, "b.txt")], saved.Tabs);
            Assert.Equal((0, pack.Path), (saved.ActiveTab, Assert.Single(saved.ExpandedPacks)));
        }
    }

    [Fact] public async Task Nothing_is_saved_before_the_restore_ended_or_without_a_catalog()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var path = c.Catalog!.Packs[0].Path;
            store.Save(new SessionState([new(path, "a.txt")], 0, []));
            var session = Coordinator(game, c, vm, store);
            session.Save();                                                            // before RestoreAsync
            Assert.Single(store.Load().State.Tabs);
            game.GameSettings.InstallPath = game.Root.File("nowhere");                 // catalog gone
            await session.RestoreAsync();
            session.Save();
            Assert.Single(store.Load().State.Tabs);
        }
    }

    // The game is missing at the start and found later: the stored session was never restored, so it must not be overwritten.
    [Fact] public async Task A_game_found_after_a_restore_without_catalog_does_not_overwrite_the_stored_session()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1]), ("b.txt", [1])));
        using (game)
        {
            var pack = c.Catalog!.Packs[0];
            store.Save(new SessionState([new(pack.Path, "a.txt"), new(pack.Path, "b.txt")], 1, []));
            game.GameSettings.InstallPath = game.Root.File("nowhere");                 // not found at the start
            Assert.Null(c.Catalog);
            var session = Coordinator(game, c, vm, store); var dirty = 0; session.Dirty += (_, _) => dirty++;
            await session.RestoreAsync();
            game.GameSettings.InstallPath = game.InstallPath;                          // the user chooses the folder: refreshed at once by the fake runner
            Assert.NotNull(c.Catalog);
            await vm.OpenEntryAsync(c.Catalog!.Packs[0], "a.txt");
            session.Save();
            Assert.Equal(0, dirty);                                                    // the restore did not end: no Dirty either
            Assert.Equal(["a.txt", "b.txt"], store.Load().State.Tabs.Select(t => t.EntryPath));
        }
    }

    [Fact] public async Task A_session_file_of_a_newer_version_is_never_overwritten()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
            File.WriteAllText(store.FilePath, """{"schemaVersion":99,"tabs":[]}""");
            var before = File.ReadAllText(store.FilePath);
            var session = Coordinator(game, c, vm, store);
            await session.RestoreAsync();
            await vm.OpenEntryAsync(c.Catalog!.Packs[0], "a.txt");
            session.Save();
            Assert.Equal(before, File.ReadAllText(store.FilePath));
            Assert.NotEmpty(game.Log.Warnings);
        }
    }

    // The rules of the brief that the tests above do not pin down.
    [Fact] public async Task Dirty_is_silent_during_the_restore_and_comes_after_it()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1]), ("b.txt", [1])));
        using (game)
        {
            var path = c.Catalog!.Packs[0].Path;
            store.Save(new SessionState([new(path, "a.txt")], 0, [path]));
            var session = Coordinator(game, c, vm, store); var dirty = 0; session.Dirty += (_, _) => dirty++;
            await session.RestoreAsync();
            Assert.Equal(0, dirty);                                                    // the restore changed tabs and packs; nothing was dropped
            vm.Tabs.SelectedTab = null;
            Assert.Equal(1, dirty);
        }
    }

    [Fact] public async Task A_tab_that_is_already_open_is_not_created_twice_by_the_restore()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1]), ("b.txt", [1])));
        using (game)
        {
            var pack = c.Catalog!.Packs[0];
            store.Save(new SessionState([new(pack.Path, "a.txt"), new(pack.Path, "b.txt")], 1, []));
            var session = Coordinator(game, c, vm, store);
            await vm.OpenEntryAsync(pack, "a.txt");                                    // the user was faster than the restore
            await session.RestoreAsync();
            Assert.Equal(["a.txt", "b.txt"], vm.Tabs.Tabs.Select(t => t.Title));
            Assert.Equal("b.txt", vm.Tabs.SelectedTab!.Title);
        }
    }

    [Fact] public async Task An_unreadable_session_file_is_not_overwritten_either()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var session = new SessionCoordinator(store, new SessionLoadResult(SessionState.Empty, LoadStatus.Unreadable, null),
                c, vm.Explorer, vm.Tabs, game.Log);
            await session.RestoreAsync();
            await vm.OpenEntryAsync(c.Catalog!.Packs[0], "a.txt");
            session.Save();
            Assert.False(File.Exists(store.FilePath));
            Assert.Single(game.Log.Warnings);
        }
    }

    [Fact] public async Task A_session_without_tabs_captures_minus_one_as_the_active_tab()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack"));
        using (game)
        {
            var session = Coordinator(game, c, vm, store);
            await session.RestoreAsync();
            Assert.Equal(-1, session.Capture().ActiveTab);
            Assert.Empty(session.Capture().Tabs);
        }
    }
}
