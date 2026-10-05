using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Session;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class WorkspaceControllerTests
{
    private static PackSource Pack(WorkspaceController c, string name) => c.Catalog!.Packs.Single(p => p.Name == name);

    [Fact] public async Task Load_finds_the_game_scans_the_packs_and_resolves_the_schema()
    {
        using var game = new TestGame();
        game.AddPack("b.pack"); game.AddPack("a.pack");
        var c = game.NewController(); var changes = 0; c.Changed += (_, _) => changes++;
        await c.LoadAsync();
        Assert.Equal((GameStatus.Found, false, false), (c.Status, c.IsBusy, c.ManualPathInvalid));
        Assert.Equal(game.InstallPath, c.Location!.InstallPath);
        Assert.Equal(["a.pack", "b.pack"], c.Catalog!.Packs.Select(p => p.Name));
        Assert.Equal(SchemaOrigin.Shipped, c.Schema!.Origin);
        Assert.True(changes >= 2);
        Assert.Empty(game.Runner.Titles);                                   // silent: never through the runner
    }

    [Fact] public async Task Without_a_profile_nothing_is_loaded()
    {
        using var game = new TestGame();
        var c = new WorkspaceController(null, game.GameSettings, game.Settings, game.Paths, new NoSteam(), game.Runner, game.Log, game.Texts);
        await c.LoadAsync();
        Assert.Equal((GameStatus.NoProfile, (PackCatalogResult?)null), (c.Status, c.Catalog));
    }

    // Review Focus 4
    [Fact] public async Task Stored_install_path_without_data_folder_is_not_found()
    {
        using var game = new TestGame();
        game.GameSettings.InstallPath = game.Root.File("elsewhere");
        var c = game.NewController();
        await c.LoadAsync();
        Assert.Equal((GameStatus.NotFound, true, (PackCatalogResult?)null), (c.Status, c.ManualPathInvalid, c.Catalog));
        Assert.NotNull(c.Schema);                                           // the schema does not depend on the game folder
    }

    [Fact] public async Task Without_a_manual_path_the_game_is_found_through_steam()
    {
        using var game = new TestGame();
        game.GameSettings.InstallPath = null;
        // steam library: <Root>\steam\steamapps\common\Test Game\packs
        var steamRoot = game.Root.File("steam");
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps", "common", "Test Game", "packs"));
        var c = game.NewController(new FixedSteam(steamRoot));
        await c.LoadAsync();
        Assert.Equal(Path.Combine(steamRoot, "steamapps", "common", "Test Game"), c.Location!.InstallPath);
    }

    [Fact] public async Task Override_schema_wins_and_a_broken_override_falls_back()
    {
        using var game = new TestGame();
        var overrideFolder = game.Root.File("override"); Directory.CreateDirectory(overrideFolder);
        File.Copy(Path.Combine(game.Paths.SchemasDirectory, "schema_test.ron"), Path.Combine(overrideFolder, "schema_test.ron"));
        game.Settings.SchemaFolder = overrideFolder;
        var c = game.NewController();
        await c.LoadAsync();
        Assert.Equal(SchemaOrigin.Override, c.Schema!.Origin);

        File.WriteAllText(Path.Combine(overrideFolder, "schema_test.ron"), "( broken");
        await c.ReloadSchemaAsync();
        Assert.Equal((SchemaOrigin.Shipped, SchemaProblem.InvalidSyntax), (c.Schema!.Origin, c.Schema.OverrideAttempt!.Problem));
        Assert.Equal(["Load schema"], game.Runner.Titles);
    }

    [Fact] public async Task Refresh_runs_through_the_runner_and_a_cancel_keeps_the_old_catalog()
    {
        using var game = new TestGame();
        game.AddPack("a.pack");
        var c = game.NewController();
        await c.LoadAsync();
        game.AddPack("b.pack");
        game.Runner.CancelNext = true;
        await c.RefreshAsync();
        Assert.Single(c.Catalog!.Packs);
        await c.RefreshAsync();
        Assert.Equal(2, c.Catalog!.Packs.Count);
        Assert.Equal(["Refresh pack list", "Refresh pack list"], game.Runner.Titles);
        Assert.Contains(game.Runner.Reports, r => r.Total == 2);
    }

    [Fact] public async Task OpenPack_returns_index_and_tree_and_caches_them()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("script/main.lua", [1]), ("db/x_tables/data__", TestGame.TableBytes));
        var c = game.NewController();
        await c.LoadAsync();
        var content = await c.OpenPackAsync(Pack(c, "a.pack"));
        Assert.Equal(2, content!.Index.Entries.Count);
        Assert.Equal(["db", "script"], content.Tree.Children.Select(n => n.Name));
        Assert.Same(content, await c.OpenPackAsync(Pack(c, "a.pack")));
        Assert.Equal(["Open a.pack"], game.Runner.Titles);                  // the second call came from the cache
        c.ClosePack(Pack(c, "a.pack"));
        Assert.NotSame(content, await c.OpenPackAsync(Pack(c, "a.pack")));
    }

    [Fact] public async Task OpenContent_only_looks_into_the_cache_and_never_starts_a_run()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        Assert.Null(c.OpenContent(Pack(c, "a.pack")));
        var content = await c.OpenPackAsync(Pack(c, "a.pack"));
        Assert.Same(content, c.OpenContent(Pack(c, "a.pack")));
        c.ClosePack(Pack(c, "a.pack"));
        Assert.Null(c.OpenContent(Pack(c, "a.pack")));
        Assert.Equal(["Open a.pack"], game.Runner.Titles);
    }

    [Fact] public async Task OpenPack_with_a_broken_index_returns_null_and_keeps_the_problem()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("x", [1, 2, 3]));
        var c = game.NewController();
        await c.LoadAsync();
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..^1]);            // data no longer ends at the file end
        Assert.Null(await c.OpenPackAsync(Pack(c, "a.pack")));
        Assert.Equal(PackProblem.InconsistentIndex, c.LastIndexProblem(Pack(c, "a.pack"))!.Problem);
        Assert.NotEmpty(game.Log.Warnings);
    }

    [Fact] public async Task OpenEntry_decodes_a_table_with_the_schema()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("db/x_tables/data__", TestGame.TableBytes));
        var c = game.NewController();
        await c.LoadAsync();
        var opened = await c.OpenEntryAsync(Pack(c, "a.pack"), "db/x_tables/data__");
        Assert.Equal((EntryKind.Table, TableDecodeStatus.Ok), (opened!.Kind, opened.Table!.Status));
        Assert.Equal(["Open data__"], game.Runner.Titles);
    }

    // Review Focus 1
    [Fact] public async Task OpenEntry_after_a_pack_change_rereads_the_index_and_reports_it()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", "old"u8.ToArray()));
        var c = game.NewController();
        await c.LoadAsync();
        var pack = Pack(c, "a.pack");
        var before = await c.OpenPackAsync(pack);
        var changed = new List<PackSource>(); c.PackChanged += (_, p) => changed.Add(p);
        new PackBuilder().AddEntry("a.txt", "newer text"u8.ToArray()).WriteTo(path);
        var opened = await c.OpenEntryAsync(pack, "a.txt");
        Assert.Equal((EntryProblem.None, "newer text"), (opened!.ReadProblem, opened.Text!.Text));
        Assert.Equal(pack, Assert.Single(changed));
        Assert.NotSame(before, await c.OpenPackAsync(pack));                // the cached index was replaced
    }

    // Review Focus 1
    [Fact] public async Task OpenEntry_of_a_deleted_pack_reports_the_problem()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        var pack = Pack(c, "a.pack");
        await c.OpenPackAsync(pack);
        var changed = new List<PackSource>(); c.PackChanged += (_, p) => changed.Add(p);
        File.Delete(path);
        var opened = await c.OpenEntryAsync(pack, "a.txt");
        Assert.Equal(EntryProblem.PackChanged, opened!.ReadProblem);
        Assert.Null(opened.Text);
        Assert.Equal(pack, Assert.Single(changed));
        Assert.Null(await c.OpenPackAsync(pack));                           // the cached index is gone: this goes through the runner again
        Assert.Equal(["Open a.pack", "Open a.txt", "Open a.pack"], game.Runner.Titles);
    }

    [Fact] public async Task OpenEntry_of_a_missing_entry_is_a_problem_not_an_exception()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        var opened = await c.OpenEntryAsync(Pack(c, "a.pack"), "gone.txt");
        Assert.Equal(EntryProblem.Corrupt, opened!.ReadProblem);
    }

    // Review Focus 5
    [Fact] public async Task A_request_while_the_runner_is_busy_does_nothing()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        game.Runner.IsRunning = true;
        Assert.False(c.CanStartOperation);
        Assert.Null(await c.OpenPackAsync(Pack(c, "a.pack")));
        Assert.Null(await c.OpenEntryAsync(Pack(c, "a.pack"), "a.txt"));
        await c.RefreshAsync(); await c.ReloadSchemaAsync();
        Assert.Empty(game.Runner.Titles);
    }

    [Fact] public async Task Changing_the_install_path_or_the_schema_folder_reloads()
    {
        using var game = new TestGame();
        game.AddPack("a.pack");
        var c = game.NewController();
        await c.LoadAsync();
        game.GameSettings.InstallPath = game.Root.File("nowhere");
        Assert.Equal(GameStatus.NotFound, c.Status);                        // FakeOperationRunner runs synchronously
        game.GameSettings.InstallPath = game.InstallPath;
        Assert.Equal(GameStatus.Found, c.Status);
        game.Settings.SchemaFolder = game.Root.File("no-schemas");
        Assert.Equal(["Refresh pack list", "Refresh pack list", "Load schema"], game.Runner.Titles);
        Assert.Equal(SchemaProblem.NotFound, c.Schema!.OverrideAttempt!.Problem);
    }

    [Fact] public async Task Refresh_drops_cached_indexes_of_changed_packs()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", [1]));
        game.AddPack("b.pack", ("b.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        var a = await c.OpenPackAsync(Pack(c, "a.pack")); var b = await c.OpenPackAsync(Pack(c, "b.pack"));
        var changed = new List<string>(); c.PackChanged += (_, p) => changed.Add(p.Name);
        new PackBuilder().AddEntry("a.txt", [1, 2, 3]).WriteTo(path);
        await c.RefreshAsync();
        Assert.Equal(["a.pack"], changed);
        Assert.Same(b, await c.OpenPackAsync(Pack(c, "b.pack")));
        Assert.NotSame(a, await c.OpenPackAsync(Pack(c, "a.pack")));
    }

    // The tests below are not in the brief: they cover the silent session restore and the remembered settings change.

    [Fact] public async Task Restore_brings_back_packs_and_tabs_with_the_spelling_of_the_catalog()
    {
        using var game = new TestGame();
        var pathA = game.AddPack("a.pack", ("db/x_tables/data__", TestGame.TableBytes), ("b.txt", "hi"u8.ToArray()));
        game.AddPack("b.pack", ("c.txt", "yo"u8.ToArray()));
        var c = game.NewController();
        await c.LoadAsync();
        var state = new SessionState(
            [new SessionTab(pathA.ToUpperInvariant(), "DB/x_tables/data__"), new SessionTab(Pack(c, "b.pack").Path, "c.txt")],
            1, [pathA.ToUpperInvariant()]);

        var restored = (await c.RestoreAsync(state))!;

        var pack = Assert.Single(restored.Packs);
        Assert.Equal(Pack(c, "a.pack"), pack.Pack);                          // the catalog's spelling, not the stored one
        Assert.Equal(["db", "b.txt"], pack.Content.Tree.Children.Select(n => n.Name));
        Assert.Equal(["db/x_tables/data__", "c.txt"], restored.Tabs.Select(t => t.Entry.EntryPath));
        Assert.Equal(TableDecodeStatus.Ok, restored.Tabs[0].Entry.Table!.Status);
        Assert.Equal((1, false), (restored.ActiveTab, c.IsBusy));
        Assert.Same(pack.Content, await c.OpenPackAsync(Pack(c, "a.pack")));  // cached by the restore
        Assert.Empty(game.Runner.Titles);                                      // silent: never through the runner
        // b.pack only has a tab: its index is not kept open
        Assert.NotNull(await c.OpenPackAsync(Pack(c, "b.pack")));
        Assert.Equal(["Open b.pack"], game.Runner.Titles);
    }

    [Fact] public async Task Restore_drops_what_no_longer_exists_and_logs_it()
    {
        using var game = new TestGame();
        var pathA = game.AddPack("a.pack", ("a.txt", [1]), ("b.txt", [2]));
        var c = game.NewController();
        await c.LoadAsync();
        var state = new SessionState(
            [new SessionTab(game.Root.File("gone.pack"), "x.txt"), new SessionTab(pathA, "missing.txt"), new SessionTab(pathA, "b.txt")],
            0, [game.Root.File("gone.pack"), pathA]);

        var restored = (await c.RestoreAsync(state))!;

        Assert.Equal(["b.txt"], restored.Tabs.Select(t => t.Entry.EntryPath));
        Assert.Equal(0, restored.ActiveTab);                                 // the active tab was dropped: the first remaining one
        Assert.Equal(pathA, Assert.Single(restored.Packs).Pack.Path);
        Assert.Equal(3, game.Log.Infos.Count(line => line.StartsWith("Session:", StringComparison.Ordinal)));   // expanded gone pack, tab of the gone pack, tab of the missing entry
    }

    [Fact] public async Task Restore_drops_a_pack_with_a_broken_index_and_its_tabs()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", [1, 2, 3]));
        var c = game.NewController();
        await c.LoadAsync();
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..^1]);
        var restored = (await c.RestoreAsync(new SessionState([new SessionTab(path, "a.txt")], 0, [path])))!;
        Assert.Equal((0, 0, -1), (restored.Packs.Count, restored.Tabs.Count, restored.ActiveTab));
        Assert.Single(game.Log.Warnings, line => line.Contains("a.pack", StringComparison.Ordinal));   // the broken index is logged once
    }

    [Fact] public async Task Restore_keeps_a_tab_whose_entry_has_a_decode_problem()
    {
        using var game = new TestGame(withSchema: false);
        var path = game.AddPack("a.pack", ("db/x_tables/data__", TestGame.TableBytes));
        var c = game.NewController();
        await c.LoadAsync();
        var restored = (await c.RestoreAsync(new SessionState([new SessionTab(path, "db/x_tables/data__")], 0, [])))!;
        Assert.True(Assert.Single(restored.Tabs).Entry.SchemaMissing);
        Assert.Empty(restored.Packs);
        Assert.Single(game.Log.Warnings, line => line.Contains("schema missing", StringComparison.Ordinal));   // the hint of the tab is logged
    }

    [Fact] public async Task Restore_of_an_empty_session_is_empty()
    {
        using var game = new TestGame();
        var c = game.NewController();
        await c.LoadAsync();
        var restored = (await c.RestoreAsync(SessionState.Empty))!;
        Assert.Equal((0, 0, -1), (restored.Packs.Count, restored.Tabs.Count, restored.ActiveTab));
    }

    [Fact] public async Task A_schema_folder_change_during_the_silent_load_is_carried_out_afterwards()
    {
        using var game = new TestGame();
        var c = game.NewController();
        var asked = false;
        c.Changed += (_, _) =>
        {
            if (c.IsBusy && !asked)
            {
                asked = true;
                game.Settings.SchemaFolder = game.Root.File("no-schemas");   // the controller cannot start a run now
            }
        };
        await c.LoadAsync();
        Assert.Equal(["Load schema"], game.Runner.Titles);
        Assert.Equal(SchemaProblem.NotFound, c.Schema!.OverrideAttempt!.Problem);
    }

    [Fact] public async Task An_install_path_change_during_the_silent_load_refreshes_once_for_both_wishes()
    {
        using var game = new TestGame();
        game.AddPack("a.pack");
        var c = game.NewController();
        var asked = false;
        c.Changed += (_, _) =>
        {
            if (c.IsBusy && !asked)
            {
                asked = true;
                game.Settings.SchemaFolder = game.Root.File("no-schemas");
                game.GameSettings.InstallPath = game.Root.File("nowhere");
            }
        };
        await c.LoadAsync();
        Assert.Equal(["Refresh pack list"], game.Runner.Titles);              // a refresh includes the schema
        Assert.Equal((GameStatus.NotFound, SchemaProblem.NotFound), (c.Status, c.Schema!.OverrideAttempt!.Problem));
    }

    // Review of task 4. These use ManualOperationRunner: a user run finishes when the test says so. A silent run cannot be held open
    // without a seam in the product, so the Changed event that BeginSilentRun raises (after the state was read, before the work
    // starts) is the point where the test finishes the other run.

    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    [Fact] public async Task A_refresh_that_finishes_during_the_restore_discards_the_restore()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", [1]));
        var runner = new ManualOperationRunner();
        var c = game.NewController(runner: runner);
        await c.LoadAsync();
        var pack = Pack(c, "a.pack");
        game.GameSettings.InstallPath = game.Root.File("nowhere");           // starts a refresh that waits in the runner
        Assert.Equal(["Refresh pack list"], runner.Titles);
        var finished = false;
        c.Changed += (_, _) => { if (c.IsBusy && !finished) { finished = true; runner.Complete(); } };

        var restored = await c.RestoreAsync(new SessionState([new SessionTab(path, "a.txt")], 0, [path]));

        Assert.Null(restored);                                                // null: not restored
        Assert.Equal(GameStatus.NotFound, c.Status);                          // the refresh result stands
        Assert.Single(game.Log.Infos, line => line.Contains("Session restore skipped", StringComparison.Ordinal));
        var open = c.OpenPackAsync(pack);                                     // nothing was cached: this needs the runner
        runner.Complete();
        Assert.NotNull(await open.WaitAsync(Guard));
        Assert.Equal(["Refresh pack list", "Open a.pack"], runner.Titles);
    }

    [Fact] public async Task LoadAsync_completes_only_after_its_pending_reload()
    {
        using var game = new TestGame();
        var runner = new ManualOperationRunner();
        var c = game.NewController(runner: runner);
        var asked = false;
        c.Changed += (_, _) =>
        {
            if (c.IsBusy && !asked)
            {
                asked = true;
                game.Settings.SchemaFolder = game.Root.File("no-schemas");   // the controller is busy: the wish is remembered
            }
        };

        var load = c.LoadAsync();
        await runner.WaitForRun().WaitAsync(Guard);
        Assert.Equal(["Load schema"], runner.Titles);
        Assert.False(load.IsCompleted);                                       // it waits for the reload it started
        runner.Complete();
        await load.WaitAsync(Guard);
        Assert.Equal(SchemaProblem.NotFound, c.Schema!.OverrideAttempt!.Problem);
    }

    [Fact] public async Task A_load_that_overlaps_a_refresh_does_not_overwrite_the_newer_state()
    {
        using var game = new TestGame();
        game.AddPack("a.pack");
        var runner = new ManualOperationRunner();
        var c = game.NewController(runner: runner);
        game.GameSettings.InstallPath = game.Root.File("nowhere");           // starts a refresh before the load, waits in the runner
        Assert.Equal(["Refresh pack list"], runner.Titles);
        var finished = false;
        c.Changed += (_, _) => { if (c.IsBusy && !finished) { finished = true; runner.Complete(); } };

        await c.LoadAsync();

        Assert.Equal((GameStatus.NotFound, (PackCatalogResult?)null), (c.Status, c.Catalog));   // the older load did not win
        Assert.NotNull(c.Schema);
        Assert.Single(game.Log.Infos, line => line.Contains("first load was skipped", StringComparison.Ordinal));
    }

    [Fact] public async Task A_settings_change_during_a_user_run_is_carried_out_when_that_run_ends()
    {
        using var game = new TestGame();
        game.AddPack("a.pack");
        var runner = new ManualOperationRunner();
        var c = game.NewController(runner: runner);
        await c.LoadAsync();
        var open = c.OpenPackAsync(Pack(c, "a.pack"));
        Assert.False(c.CanStartOperation);
        game.Settings.SchemaFolder = game.Root.File("no-schemas");           // cannot start now: remembered
        Assert.Equal(["Open a.pack"], runner.Titles);

        runner.Complete();
        await runner.WaitForRun().WaitAsync(Guard);
        Assert.Equal(["Open a.pack", "Load schema"], runner.Titles);
        Assert.False(open.IsCompleted);
        runner.Complete();
        Assert.NotNull(await open.WaitAsync(Guard));
        Assert.Equal(SchemaProblem.NotFound, c.Schema!.OverrideAttempt!.Problem);
    }

    [Fact] public async Task A_read_problem_of_an_entry_is_logged_once()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        await c.OpenEntryAsync(Pack(c, "a.pack"), "gone.txt");
        var warning = Assert.Single(game.Log.Warnings);
        Assert.All(new[] { "a.pack", "gone.txt", "Corrupt", "no longer exists" }, part => Assert.Contains(part, warning));
    }

    [Fact] public async Task A_table_that_does_not_decode_is_logged_once()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("db/y_tables/data__", TestGame.TableBytes));      // the schema only knows x_tables
        var c = game.NewController();
        await c.LoadAsync();
        var opened = await c.OpenEntryAsync(Pack(c, "a.pack"), "db/y_tables/data__");
        Assert.Equal(TableDecodeStatus.TableNotInSchema, opened!.Table!.Status);
        var warning = Assert.Single(game.Log.Warnings);
        Assert.All(new[] { "a.pack", "db/y_tables/data__", "TableNotInSchema" }, part => Assert.Contains(part, warning));
    }

    [Fact] public async Task An_index_problem_of_a_pack_that_is_not_open_is_logged_when_an_entry_is_opened()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", [1, 2, 3]));
        var c = game.NewController();
        await c.LoadAsync();
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..^1]);
        var opened = await c.OpenEntryAsync(Pack(c, "a.pack"), "a.txt");
        Assert.Equal(EntryProblem.Corrupt, opened!.ReadProblem);
        var warning = Assert.Single(game.Log.Warnings);
        Assert.All(new[] { "a.pack", "a.txt", "InconsistentIndex" }, part => Assert.Contains(part, warning));
    }

    // ---- changes of packs that are not open --------------------------------------------------------------------------------------

    private static string Rewrite(string path, int size)
    {
        new PackBuilder().AddEntry("a.txt", new byte[size]).AddEntry("b.txt", [1]).WriteTo(path);
        return path;
    }

    [Fact] public async Task A_pack_that_is_not_open_and_changed_raises_PackChanged_on_the_next_entry_open_and_refresh_once_each()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", [1]), ("b.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        var pack = Pack(c, "a.pack");
        var changed = new List<string>(); c.PackChanged += (_, p) => changed.Add(p.Name);

        await c.OpenEntryAsync(pack, "a.txt");
        Assert.Empty(changed);                                              // nothing changed since the scan

        Rewrite(path, 10);
        await c.OpenEntryAsync(pack, "a.txt");
        Assert.Equal(["a.pack"], changed);
        await c.OpenEntryAsync(pack, "b.txt");
        Assert.Equal(["a.pack"], changed);                                  // the same state is not reported again

        Rewrite(path, 20);
        await c.RefreshAsync();
        Assert.Equal(["a.pack", "a.pack"], changed);                        // a second change, noticed by the refresh
        await c.RefreshAsync();
        await c.OpenEntryAsync(Pack(c, "a.pack"), "a.txt");
        Assert.Equal(["a.pack", "a.pack"], changed);
        Assert.Null(c.OpenContent(pack));                                   // never open: no index was cached on the way
    }

    [Fact] public async Task A_refresh_reports_a_changed_pack_that_was_never_read_and_forgets_a_vanished_one()
    {
        using var game = new TestGame();
        var a = game.AddPack("a.pack", ("a.txt", [1]));
        var b = game.AddPack("b.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        var changed = new List<string>(); c.PackChanged += (_, p) => changed.Add(p.Name);
        Rewrite(a, 5); File.Delete(b);
        await c.RefreshAsync();
        Assert.Equal(["a.pack"], changed);                                  // the vanished pack raises nothing
        await c.RefreshAsync();
        Assert.Equal(["a.pack"], changed);
        Rewrite(b, 5);                                                      // it comes back as a new pack: nothing known about it
        await c.RefreshAsync();
        Assert.Equal(["a.pack"], changed);
    }

    [Fact] public async Task Reading_the_index_of_a_changed_pack_reports_it_when_the_pack_is_opened_or_restored()
    {
        using var game = new TestGame();
        var a = game.AddPack("a.pack", ("a.txt", [1]));
        var b = game.AddPack("b.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        var changed = new List<string>(); c.PackChanged += (_, p) => changed.Add(p.Name);
        Rewrite(a, 5); Rewrite(b, 5);
        Assert.NotNull(await c.OpenPackAsync(Pack(c, "a.pack")));
        Assert.Equal(["a.pack"], changed);
        Assert.NotNull(await c.RestoreAsync(new SessionState([new SessionTab(b.ToUpperInvariant(), "a.txt")], 0, [])));
        Assert.Equal(["a.pack", "b.pack"], changed);                        // in the spelling of the catalog
        await c.RestoreAsync(new SessionState([new SessionTab(b, "a.txt")], 0, []));
        Assert.Equal(["a.pack", "b.pack"], changed);
    }

    // ---- final fix wave ------------------------------------------------------------------------------------------------------------

    // Item D.1: the same file is the same file, however the path of the install folder is spelled.
    [Fact] public async Task A_refresh_with_a_differently_spelled_install_path_keeps_the_indexes_and_reports_no_change()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        var spelledBefore = Pack(c, "a.pack").Path;
        var content = await c.OpenPackAsync(Pack(c, "a.pack"));
        var changed = new List<string>(); c.PackChanged += (_, p) => changed.Add(p.Name);

        game.GameSettings.InstallPath = game.InstallPath.ToUpperInvariant();       // refreshed at once by the fake runner

        Assert.Equal(["Open a.pack", "Refresh pack list"], game.Runner.Titles);
        Assert.NotEqual(spelledBefore, Pack(c, "a.pack").Path);                    // the catalog has the other spelling now
        Assert.Same(content, c.OpenContent(Pack(c, "a.pack")));
        Assert.Empty(changed);
    }

    // Item E
    [Fact] public async Task A_reload_wish_during_the_silent_load_is_pending_until_the_reload_ran()
    {
        using var game = new TestGame();
        var c = game.NewController();
        var asked = false; var pendingDuringLoad = false;
        c.Changed += (_, _) =>
        {
            if (c.IsBusy && !asked)
            {
                asked = true;
                game.Settings.SchemaFolder = game.Root.File("no-schemas");   // the controller is busy: the wish is parked
                pendingDuringLoad = c.HasPendingReload;
            }
        };
        Assert.False(c.HasPendingReload);
        await c.LoadAsync();
        Assert.True(pendingDuringLoad);
        Assert.False(c.HasPendingReload);
    }

    // Item I.2: a pack that changed under an open index no longer has the entry: that is a pack change, not a damaged entry.
    [Fact] public async Task An_entry_that_is_gone_from_a_pack_that_changed_is_reported_as_a_pack_change()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", "old"u8.ToArray()), ("b.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        var pack = Pack(c, "a.pack");
        await c.OpenPackAsync(pack);
        var changed = new List<PackSource>(); c.PackChanged += (_, p) => changed.Add(p);
        new PackBuilder().AddEntry("b.txt", [1, 2]).WriteTo(path);

        var opened = await c.OpenEntryAsync(pack, "a.txt");

        Assert.Equal(EntryProblem.PackChanged, opened!.ReadProblem);
        Assert.Equal(pack, Assert.Single(changed));
    }

    // ---- the bytes of an entry ---------------------------------------------------------------------------------------------------

    [Fact] public async Task ReadEntryBytesAsync_returns_the_uncompressed_bytes_and_reports_a_read_problem()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("a.txt", "hello"u8.ToArray()), ("db/x_tables/data__", TestGame.TableBytes));
        var c = game.NewController();
        await c.LoadAsync();
        var pack = Pack(c, "a.pack");

        var text = await c.ReadEntryBytesAsync(pack, "a.txt");
        Assert.Equal(EntryProblem.None, text!.Problem);
        Assert.Equal("hello"u8.ToArray(), text.Bytes);

        // The raw bytes, also of a table: nothing is decoded.
        var table = await c.ReadEntryBytesAsync(pack, "db/x_tables/data__");
        Assert.Equal(TestGame.TableBytes, table!.Bytes);

        var missing = await c.ReadEntryBytesAsync(pack, "gone.txt");
        Assert.Equal((EntryProblem.Corrupt, (byte[]?)null), (missing!.Problem, missing.Bytes));
        Assert.Equal(["Open a.txt", "Open data__", "Open gone.txt"], game.Runner.Titles);
        Assert.Contains(game.Log.Warnings, line => line.Contains("gone.txt", StringComparison.Ordinal));
    }

    [Fact] public async Task ReadEntryBytesAsync_logs_a_read_problem_with_escaped_names()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();

        await c.ReadEntryBytesAsync(Pack(c, "a.pack"), "gone" + (char)7 + ".txt");

        var line = Assert.Single(game.Log.Warnings, text => text.Contains("gone", StringComparison.Ordinal));
        Assert.False(line.Contains((char)7));
        Assert.Contains("gone\\u0007.txt", line, StringComparison.Ordinal);
    }

    [Fact] public async Task ReadEntryBytesAsync_uses_the_cached_index_and_handles_a_changed_pack_like_OpenEntryAsync()
    {
        using var game = new TestGame();
        var path = game.AddPack("a.pack", ("a.txt", "old"u8.ToArray()));
        var c = game.NewController();
        await c.LoadAsync();
        var pack = Pack(c, "a.pack");
        var before = await c.OpenPackAsync(pack);
        var changed = new List<PackSource>(); c.PackChanged += (_, p) => changed.Add(p);
        new PackBuilder().AddEntry("a.txt", "newer text"u8.ToArray()).WriteTo(path);

        var content = await c.ReadEntryBytesAsync(pack, "a.txt");

        Assert.Equal("newer text"u8.ToArray(), content!.Bytes);             // the pack changed under the cached index: read again, second try
        Assert.Equal(pack, Assert.Single(changed));
        Assert.NotSame(before, await c.OpenPackAsync(pack));                // the cached index was replaced

        // A pack that is gone: a problem inside the result, and the cache is dropped.
        File.Delete(path);
        var gone = await c.ReadEntryBytesAsync(pack, "a.txt");
        Assert.Equal(EntryProblem.PackChanged, gone!.Problem);
        Assert.Null(gone.Bytes);
    }

    [Fact] public async Task ReadEntryBytesAsync_does_nothing_while_the_runner_is_busy()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("a.txt", [1]));
        var c = game.NewController();
        await c.LoadAsync();
        game.Runner.IsRunning = true;

        Assert.Null(await c.ReadEntryBytesAsync(Pack(c, "a.pack"), "a.txt"));
        Assert.Empty(game.Runner.Titles);

        game.Runner.IsRunning = false;
        game.Runner.CancelNext = true;
        Assert.Null(await c.ReadEntryBytesAsync(Pack(c, "a.pack"), "a.txt"));   // a cancelled run has no result
    }

    // ---- a restore that did not happen -------------------------------------------------------------------------------------------

    [Fact] public async Task A_restore_that_fails_returns_null_and_logs_the_error()
    {
        using var game = new TestGame();
        game.AddPack("a.pack");
        var c = game.NewController();
        await c.LoadAsync();
        // A null tab cannot come from the session store; it is the one way to make the work on the pool thread throw.
        var restored = await c.RestoreAsync(new SessionState([null!], 0, []));
        Assert.Null(restored);
        Assert.Single(game.Log.Errors, line => line.Contains("Restoring the session failed", StringComparison.Ordinal));
        Assert.False(c.IsBusy);
    }
}
