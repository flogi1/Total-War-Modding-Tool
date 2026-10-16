using TotalWarModTool.Objects;
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
    // Waits for the effect, no fixed sleep.
    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 8000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected state was not reached in time.");
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

    // The game is missing at the start and found later: the stored session is restored as soon as a catalog exists, and saved from then on.
    [Fact] public async Task A_game_found_after_the_start_gets_its_session_restored_and_saved()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1]), ("b.txt", [1])));
        using (game)
        {
            var pack = c.Catalog!.Packs[0];
            store.Save(new SessionState([new(pack.Path, "a.txt"), new(pack.Path, "b.txt")], 1, [pack.Path]));
            game.GameSettings.InstallPath = game.Root.File("nowhere");                 // not found at the start
            Assert.Null(c.Catalog);
            var session = Coordinator(game, c, vm, store); var dirty = 0; session.Dirty += (_, _) => dirty++;
            await session.RestoreAsync();
            Assert.Empty(vm.Tabs.Tabs);
            session.Save();
            Assert.Equal(2, store.Load().State.Tabs.Count);                            // nothing written while nothing was restored

            game.GameSettings.InstallPath = game.InstallPath;                          // the user chooses the folder: refreshed at once by the fake runner
            await WaitUntil(() => vm.Tabs.Tabs.Count == 2);                            // no explicit RestoreAsync: only the coordinator's own retry can do this
            Assert.Equal(["a.txt", "b.txt"], vm.Tabs.Tabs.Select(t => t.Title));
            Assert.Equal("b.txt", vm.Tabs.SelectedTab!.Title);
            Assert.Equal([c.Catalog!.Packs[0].Path], vm.Explorer.ExpandedPackPaths);
            Assert.Equal(0, dirty);                                                    // nothing was dropped

            vm.Tabs.Close(vm.Tabs.Tabs[0]);
            Assert.True(dirty > 0);
            session.Save();
            Assert.Equal(["b.txt"], store.Load().State.Tabs.Select(t => t.EntryPath));
            await session.RestoreAsync();                                              // never restores a second time
            Assert.Single(vm.Tabs.Tabs);
        }
    }

    // The controller returns null for a restore that failed: that is "not restored", never an empty session to write.
    [Fact] public async Task A_failed_restore_leaves_the_stored_session_untouched()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var pack = c.Catalog!.Packs[0];
            store.Save(new SessionState([new(pack.Path, "a.txt")], 0, [pack.Path]));
            // A null tab is the way to make the restore throw inside the controller; the store never delivers one.
            var failing = new SessionLoadResult(new SessionState([null!], 0, []), LoadStatus.Loaded, null);
            var session = new SessionCoordinator(store, failing, c, vm.Explorer, vm.Tabs, game.Log);
            var dirty = 0; session.Dirty += (_, _) => dirty++;
            await session.RestoreAsync();
            Assert.Contains(game.Log.Errors, line => line.Contains("Restoring the session failed", StringComparison.Ordinal));
            await vm.OpenEntryAsync(pack, "a.txt");
            vm.Explorer.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single().IsExpanded = true;
            session.Save();
            Assert.Equal(0, dirty);
            Assert.Equal([new SessionTab(pack.Path, "a.txt")], store.Load().State.Tabs);
            Assert.Equal([pack.Path], store.Load().State.ExpandedPacks);
        }
    }

    [Fact] public async Task A_restored_tab_is_marked_stale_by_a_refresh_that_sees_a_changed_pack()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", "one"u8.ToArray())));
        using (game)
        {
            var pack = c.Catalog!.Packs[0];
            store.Save(new SessionState([new(pack.Path, "a.txt")], 0, []));
            await Coordinator(game, c, vm, store).RestoreAsync();
            Assert.False(vm.Tabs.Tabs[0].IsStale);
            new PackBuilder().AddEntry("a.txt", "changed"u8.ToArray()).AddEntry("b.txt", [1]).WriteTo(pack.Path);
            vm.RefreshCommand.Execute(null);
            Assert.True(vm.Tabs.Tabs[0].IsStale);
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
            Assert.Single(game.Log.Warnings, w => !w.Contains("table versions"));
        }
    }

    // Item E: a reload wish that is parked must be carried out before the session is restored, so the restore runs against the new catalog.
    [Fact] public async Task The_restore_waits_for_a_pending_reload_and_runs_against_the_new_catalog()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", [1]));
        var runner = new ManualOperationRunner();
        var c = game.NewController(runner: runner);
        var vm = new MainViewModel(game.Texts, game.Paths, _ => { }, runner, () => { }, c);
        var store = new SessionStore(game.Paths.RoamingRoot, "testgame");
        store.Save(new SessionState([new(path, "a.txt")], 0, []));
        var session = Coordinator(game, c, vm, store);
        var newSpelling = game.InstallPath.ToUpperInvariant();
        var asked = false;
        c.Changed += (_, _) =>
        {
            if (c.IsBusy && !asked)
            {
                asked = true;
                game.GameSettings.InstallPath = newSpelling;                           // parked: the controller is busy with the load
            }
        };

        var load = c.LoadAsync();
        await runner.WaitForRun().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(["Refresh pack list"], runner.Titles);
        Assert.Empty(vm.Tabs.Tabs);                                                    // the end of the load must not restore against the old catalog
        Assert.DoesNotContain(game.Log.Infos, line => line.StartsWith("Session", StringComparison.Ordinal));

        runner.Complete();                                                             // the refresh ends: its Changed starts the restore
        await load.WaitAsync(TimeSpan.FromSeconds(30));
        await WaitUntil(() => vm.Tabs.Tabs.Count == 1);
        Assert.Equal(c.Catalog!.Packs[0].Path, vm.Tabs.Tabs[0].PackPath);              // the spelling of the new catalog
        Assert.StartsWith(newSpelling, vm.Tabs.Tabs[0].PackPath, StringComparison.Ordinal);
    }

    // Item F: what was already open when the late restore ended is not in the stored session yet, so it has to be saved.
    [Fact] public async Task A_restore_that_finds_tabs_already_open_raises_Dirty_once()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1]), ("b.txt", [1])));
        using (game)
        {
            var pack = c.Catalog!.Packs[0];
            store.Save(new SessionState([new(pack.Path, "a.txt")], 0, []));
            var session = Coordinator(game, c, vm, store); var dirty = 0; session.Dirty += (_, _) => dirty++;
            await vm.OpenEntryAsync(pack, "b.txt");                                    // the user was faster than the restore
            await session.RestoreAsync();
            Assert.Equal(["b.txt", "a.txt"], vm.Tabs.Tabs.Select(t => t.Title));
            Assert.Equal(1, dirty);
        }
    }

    [Fact] public async Task A_restore_that_finds_packs_already_expanded_raises_Dirty_once()
    {
        var (game, c, vm, store) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var pack = c.Catalog!.Packs[0];
            store.Save(new SessionState([new(pack.Path, "a.txt")], 0, []));
            var session = Coordinator(game, c, vm, store); var dirty = 0; session.Dirty += (_, _) => dirty++;
            vm.Explorer.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single().IsExpanded = true;   // before the restore: silent
            await session.RestoreAsync();
            Assert.Equal([pack.Path], vm.Explorer.ExpandedPackPaths);
            Assert.Equal(1, dirty);
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

    // ---- projects --------------------------------------------------------------------------------------------------------------

    private static async Task<(TestGame Game, WorkspaceController C, MainSetup Main, SessionStore Store)> CreateWithProjects(Action<TestGame> arrange)
    {
        var game = new TestGame(); arrange(game);
        var c = game.NewController();
        await c.LoadAsync();
        return (game, c, game.NewMain(c), new SessionStore(game.Paths.RoamingRoot, "testgame"));
    }

    private static SessionCoordinator CoordinatorWithProjects(TestGame game, WorkspaceController c, MainSetup main, SessionStore store) =>
        new(store, store.Load(), c, main.Main.Explorer, main.Main.Tabs, game.Log, main.Projects);

    private static IEnumerable<ProjectNode> ProjectNodesOf(ExplorerViewModel explorer) =>
        explorer.Groups.OfType<ProjectsGroupNode>().SelectMany(group => group.Children).OfType<ProjectNode>();

    private static string KeyOf(Guid id) => "project:" + id.ToString("D");

    [Fact] public async Task Project_tabs_and_expanded_projects_are_captured()
    {
        var (game, c, main, store) = await CreateWithProjects(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var session = CoordinatorWithProjects(game, c, main, store);
            await session.RestoreAsync();
            var pack = c.Catalog!.Packs[0];
            var state = game.NewProject(main.Projects, "Alpha", (@"db\units_tables\a.tsv", "x"));
            await main.Main.OpenEntryAsync(pack, "a.txt");
            await main.Main.OpenProjectFileAsync(state.Id, @"db\units_tables\a.tsv");
            main.Main.Explorer.ApplyRestoredProjects([(state.Id, (await main.Projects.ListFilesSilentlyAsync(state.Id))!)]);
            main.Main.Explorer.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single().IsExpanded = true;

            var captured = session.Capture();

            Assert.Equal([new SessionTab(pack.Path, "a.txt"), new SessionTab(KeyOf(state.Id), "db/units_tables/a.tsv")], captured.Tabs);
            Assert.Equal([pack.Path, KeyOf(state.Id)], captured.ExpandedPacks);
        }
    }

    [Fact] public async Task Project_tabs_are_restored_in_their_old_order_and_missing_ones_are_dropped()
    {
        var (game, c, main, store) = await CreateWithProjects(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var packPath = c.Catalog!.Packs[0].Path;
            var state = game.NewProject(main.Projects, "Alpha", ("a.lua", "x"), ("b.lua", "x"));
            File.Delete(Path.Combine(state.Folder, "pack", "b.lua"));                    // the file of the active tab is gone
            store.Save(new SessionState([new(KeyOf(state.Id), "a.lua"), new(packPath, "a.txt"), new(KeyOf(state.Id), "b.lua"),
                new(KeyOf(Guid.NewGuid()), "x.lua")], 2, [KeyOf(state.Id)]));
            var session = CoordinatorWithProjects(game, c, main, store); var dirty = 0; session.Dirty += (_, _) => dirty++;

            await session.RestoreAsync();

            Assert.Equal(["a.lua", "a.txt"], main.Main.Tabs.Tabs.Select(t => t.Title));
            Assert.Equal([KeyOf(state.Id), packPath], main.Main.Tabs.Tabs.Select(t => t.PackPath));
            Assert.Same(main.Main.Tabs.Tabs[0], main.Main.Tabs.SelectedTab);             // the active one is gone: the first
            Assert.Equal([state.Id], main.Main.Explorer.ExpandedProjectIds);
            Assert.Equal(["a.lua"], Assert.Single(ProjectNodesOf(main.Main.Explorer)).Children.Select(n => n.Name));
            Assert.Equal(1, dirty);
        }
    }

    [Fact] public async Task The_active_project_tab_stays_active_after_the_restore()
    {
        var (game, c, main, store) = await CreateWithProjects(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var packPath = c.Catalog!.Packs[0].Path;
            var state = game.NewProject(main.Projects, "Alpha", ("a.lua", "x"));
            store.Save(new SessionState([new(packPath, "a.txt"), new("project:" + state.Id.ToString("D").ToUpperInvariant(), "a.lua")], 1, []));
            var session = CoordinatorWithProjects(game, c, main, store); var dirty = 0; session.Dirty += (_, _) => dirty++;

            await session.RestoreAsync();

            Assert.Equal(["a.txt", "a.lua"], main.Main.Tabs.Tabs.Select(t => t.Title));
            Assert.Equal("a.lua", main.Main.Tabs.SelectedTab!.Title);
            Assert.Equal(0, dirty);
        }
    }

    [Fact] public async Task Project_tabs_restored_with_a_documents_controller_are_editable()
    {
        var (game, c, _, store) = await CreateWithProjects(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var main = game.NewMain(c, withDocuments: true);
            var def = new TotalWarModTool.Schema.TableDefinition(0, [new TotalWarModTool.Schema.FieldDefinition("value", TotalWarModTool.Schema.FieldType.I32, nameof(TotalWarModTool.Schema.FieldType.I32), false, null)]);
            var text = TotalWarModTool.Tables.TableTsv.Write("x_tables", 0, "db/x_tables/a", def, [[42]]).Text!;
            var state = game.NewProject(main.Projects, "Alpha", (@"db\x_tables\a.tsv", text), ("readme.txt", "hello"));
            store.Save(new SessionState([new(KeyOf(state.Id), "db/x_tables/a.tsv"), new(KeyOf(state.Id), "readme.txt")], 0, []));
            var session = CoordinatorWithProjects(game, c, main, store);

            await session.RestoreAsync();

            Assert.Equal(["a.tsv", "readme.txt"], main.Main.Tabs.Tabs.Select(t => t.Title));
            var editable = Assert.IsType<EditableRowsTabViewModel>(main.Main.Tabs.Tabs[0]);
            Assert.Same(editable, Assert.Single(main.Documents!.EditableTabs));
            Assert.IsType<TextTabViewModel>(main.Main.Tabs.Tabs[1]);
        }
    }

    [Fact] public async Task Without_a_projects_controller_project_entries_are_ignored()
    {
        var (game, c, main, store) = await CreateWithProjects(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var packPath = c.Catalog!.Packs[0].Path;
            var state = game.NewProject(main.Projects, "Alpha", ("a.lua", "x"));
            store.Save(new SessionState([new(KeyOf(state.Id), "a.lua"), new(packPath, "a.txt")], 0, [KeyOf(state.Id), packPath]));
            var session = new SessionCoordinator(store, store.Load(), c, main.Main.Explorer, main.Main.Tabs, game.Log);

            await session.RestoreAsync();

            Assert.Equal(["a.txt"], main.Main.Tabs.Tabs.Select(t => t.Title));
            Assert.Equal([packPath], main.Main.Explorer.ExpandedPackPaths);
            Assert.Empty(main.Main.Explorer.ExpandedProjectIds);
        }
    }

    [Fact] public async Task A_project_that_left_the_list_during_the_restore_gets_no_tabs()
    {
        var (game, c, main, store) = await CreateWithProjects(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var packPath = c.Catalog!.Packs[0].Path;
            var state = game.NewProject(main.Projects, "Alpha", ("a.lua", "x"));
            store.Save(new SessionState([new(KeyOf(state.Id), "a.lua"), new(packPath, "a.txt")], 0, [KeyOf(state.Id)]));
            var session = CoordinatorWithProjects(game, c, main, store); var dirty = 0; session.Dirty += (_, _) => dirty++;

            // The continuations of the restore wait in a queue, so that nothing runs on a pool thread while the project leaves the list.
            var queue = new QueueContext();
            var previous = SynchronizationContext.Current;
            Task restore;
            SynchronizationContext.SetSynchronizationContext(queue);
            try
            {
                restore = session.RestoreAsync();                                        // runs up to its first await: the project part has read its snapshot
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            Assert.True(main.Projects.Remove(state.Id).Ok);
            queue.RunUntilCompleted(restore);

            Assert.Equal(["a.txt"], main.Main.Tabs.Tabs.Select(t => t.Title));
            Assert.Empty(main.Main.Explorer.ExpandedProjectIds);
            Assert.Equal(1, dirty);
        }
    }

    private const string XEntry = "db/x_tables/data__";
    private const string YEntry = "db/y_tables/data__";

    // A game with the object schema, a vanilla pack (x_tables k1, k2; y_tables), loaded, and the controllers the composition root has.
    private static async Task<(TestGame Game, WorkspaceController C, MainSetup Main, ObjectsController Objects, SessionStore Store)> CreateWithObjects()
    {
        var game = new TestGame(withSchema: false);
        ObjectsTestSchema.WriteSchema(game);
        ObjectsTestSchema.WriteVanillaPack(game, "data.pack", ObjectsTestSchema.XRows, ObjectsTestSchema.YRows);
        File.WriteAllText(Path.Combine(game.InstallPath, LoadOrderResolver.UsedModsFileName), string.Empty);
        ObjectsTestSchema.WriteLocPack(game);
        var c = game.NewController(profile: ObjectsTestSchema.Profile);
        await c.LoadAsync();
        var main = game.NewMain(c, withDocuments: true);
        var objects = new ObjectsController(c, main.Projects, main.Documents, main.Main.Tabs, game.Runner, game.Paths, game.Log, game.Texts,
            key => key, main.Main.OpenEntryAsync, main.Main.OpenProjectFileAsync);
        main.Main.Tabs.ObjectOpener = objects;
        return (game, c, main, objects, new SessionStore(game.Paths.RoamingRoot, "testgame"));
    }

    private static SessionTab ObjectSessionTab(string table, string key, params SessionObjectRef[] history) =>
        new(TabSource.ObjectPrefix + table, new ObjectId(table, [key]).ToString(), new SessionObjectTab(history, history.Length - 1));

    [Fact] public async Task Restore_brings_back_object_tabs_in_order_without_building_them()
    {
        var (game, c, main, objects, store) = await CreateWithObjects();
        using (game)
        {
            var pack = c.Catalog!.Packs[0].Path;
            var objectTab = new SessionTab(TabSource.ObjectPrefix + "x_tables", new ObjectId("x_tables", ["k1"]).ToString(),
                new SessionObjectTab([new SessionObjectRef("x_tables", ["k1"], null), new SessionObjectRef("x_tables", ["k2"], null)], 0));
            store.Save(new SessionState([new(pack, XEntry), objectTab, new(pack, YEntry)], 0, []));
            var session = new SessionCoordinator(store, store.Load(), c, main.Main.Explorer, main.Main.Tabs, game.Log, main.Projects, objects);
            var dirty = 0; session.Dirty += (_, _) => dirty++;

            await session.RestoreAsync();

            var tabs = main.Main.Tabs.Tabs;
            Assert.Equal(3, tabs.Count);
            Assert.IsType<TableTabViewModel>(tabs[0]);
            var restored = Assert.IsType<ObjectTabViewModel>(tabs[1]);
            Assert.IsType<TableTabViewModel>(tabs[2]);
            Assert.Equal(["k1", "k2"], restored.History.Select(entry => entry.JoinedKey));
            Assert.Equal(0, restored.Position);
            Assert.Equal(ObjectTabState.Idle, restored.State);                         // built when it is activated, not by the restore
            Assert.Empty(game.Runner.Titles);
            Assert.Same(tabs[0], main.Main.Tabs.SelectedTab);
            Assert.Equal(0, dirty);                                                    // nothing was left out
        }
    }

    [Fact] public async Task Save_writes_object_tabs_with_their_history()
    {
        var (game, c, main, objects, store) = await CreateWithObjects();
        using (game)
        {
            var session = new SessionCoordinator(store, store.Load(), c, main.Main.Explorer, main.Main.Tabs, game.Log, main.Projects, objects);
            await session.RestoreAsync();
            var pack = c.Catalog!.Packs[0];
            await main.Main.OpenEntryAsync(pack, XEntry);
            var table = Assert.IsType<TableTabViewModel>(main.Main.Tabs.SelectedTab);
            var index = Enumerable.Range(0, 100).First(i => table.KeyOfRow(i) is { } key && key.KeyValues.SequenceEqual(new[] { "k1" }));
            objects.Show(table, table.KeyOfRow(index)!);
            var tab = Assert.IsType<ObjectTabViewModel>(main.Main.Tabs.SelectedTab);
            tab.NavigateTo(new ObjectId("x_tables", ["k2"]), newTab: false);

            session.Save();

            var saved = store.Load().State;
            Assert.Equal(2, saved.Tabs.Count);
            Assert.Null(saved.Tabs[0].Object);
            var savedObject = saved.Tabs[1];
            Assert.Equal((tab.PackPath, tab.EntryPath), (savedObject.PackPath, savedObject.EntryPath));
            Assert.Equal(TabSource.ObjectPrefix + "x_tables", savedObject.PackPath);
            Assert.Equal(1, savedObject.Object!.Position);
            Assert.Equal(["k1", "k2"], savedObject.Object.History.Select(entry => string.Concat(entry.KeyValues)));
            Assert.All(savedObject.Object.History, entry => Assert.Equal("x_tables", entry.Table));
            Assert.Equal(1, saved.ActiveTab);
        }
    }

    // Only the tab that stays selected is built: the first restored object tab is selected on its way and must not start its own build.
    [Fact] public async Task Restore_builds_only_the_active_object_tab()
    {
        var (game, c, main, objects, store) = await CreateWithObjects();
        using (game)
        {
            store.Save(new SessionState([
                ObjectSessionTab("x_tables", "k1", new SessionObjectRef("x_tables", ["k1"], null)),
                ObjectSessionTab("x_tables", "k2", new SessionObjectRef("x_tables", ["k2"], null))], 1, []));
            var session = new SessionCoordinator(store, store.Load(), c, main.Main.Explorer, main.Main.Tabs, game.Log, main.Projects, objects);

            await session.RestoreAsync();

            var tabs = main.Main.Tabs.Tabs.Cast<ObjectTabViewModel>().ToList();
            Assert.Equal(["k1", "k2"], tabs.Select(tab => tab.Current.JoinedKey));
            Assert.Same(tabs[1], main.Main.Tabs.SelectedTab);
            Assert.Equal(game.Texts.ObjectsBuilding("k2"), Assert.Single(game.Runner.Titles));
            Assert.Equal((ObjectTabState.Idle, ObjectTabState.Ready), (tabs[0].State, tabs[1].State));
        }
    }

    [Fact] public async Task Without_an_objects_controller_object_tabs_are_left_out()
    {
        var (game, c, main, _, store) = await CreateWithObjects();
        using (game)
        {
            var pack = c.Catalog!.Packs[0].Path;
            store.Save(new SessionState([new(pack, XEntry), ObjectSessionTab("x_tables", "k1", new SessionObjectRef("x_tables", ["k1"], null)), new(pack, YEntry)], 2, []));
            var session = new SessionCoordinator(store, store.Load(), c, main.Main.Explorer, main.Main.Tabs, game.Log, main.Projects);
            var dirty = 0; session.Dirty += (_, _) => dirty++;

            await session.RestoreAsync();

            Assert.All(main.Main.Tabs.Tabs, tab => Assert.IsType<TableTabViewModel>(tab));
            Assert.Equal(2, main.Main.Tabs.Tabs.Count);
            Assert.Same(main.Main.Tabs.Tabs[1], main.Main.Tabs.SelectedTab);           // the stored active tab, its index after the dropped one
            Assert.Equal(1, dirty);
        }
    }

    // Runs the continuations that were posted to it on the thread that calls RunUntilCompleted, one at a time.
    private sealed class QueueContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public void RunUntilCompleted(Task task)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                var deadline = Environment.TickCount64 + 30000;
                while (!task.IsCompleted)
                {
                    Assert.True(Environment.TickCount64 < deadline, "The restore did not end in time.");
                    if (_queue.TryTake(out var item, 50))
                    {
                        item.Callback(item.State);
                    }
                }
            }
            finally
            {
                SetSynchronizationContext(previous);
            }

            task.GetAwaiter().GetResult();
        }
    }
}
