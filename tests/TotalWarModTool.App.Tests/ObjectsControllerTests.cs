using TotalWarModTool.Objects;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class ObjectsControllerTests
{
    private const string XEntry = "db/x_tables/data__";

    private sealed record Setup(TestGame Game, WorkspaceController Workspace, MainSetup Main, ObjectsController Controller, string VanillaPath) : IDisposable
    {
        public TabsViewModel Tabs => Main.Main.Tabs;

        public ProjectsController Projects => Main.Projects;

        public FakeOperationRunner Runner => Game.Runner;

        public AppTexts Texts => Game.Texts;

        public void Dispose() => Game.Dispose();

        /// <summary>How many builds of the object with this key went through the runner.</summary>
        public int Builds(string key) => Runner.Titles.Count(title => title == Texts.ObjectsBuilding(key));
    }

    // A game with the object schema, a vanilla pack (x_tables k1, k2; y_tables with two rows on k1), an empty used_mods.txt and an empty loc
    // pack (so the load order has no warnings), loaded; the main view model with documents, and the controller wired as the composition root
    // wires it. arrange runs before the workspace and the controller exist.
    private static async Task<Setup> CreateAsync(Action<TestGame>? arrange = null)
    {
        var game = new TestGame(withSchema: false);
        ObjectsTestSchema.WriteSchema(game);
        var vanilla = ObjectsTestSchema.WriteVanillaPack(game, "data.pack", ObjectsTestSchema.XRows, ObjectsTestSchema.YRows);
        File.WriteAllText(Path.Combine(game.InstallPath, LoadOrderResolver.UsedModsFileName), string.Empty);
        ObjectsTestSchema.WriteLocPack(game);
        arrange?.Invoke(game);
        var workspace = game.NewController(profile: ObjectsTestSchema.Profile);
        await workspace.LoadAsync();
        var main = game.NewMain(workspace, withDocuments: true);
        var controller = new ObjectsController(workspace, main.Projects, main.Documents, main.Main.Tabs, game.Runner, game.Paths, game.Log, game.Texts,
            key => key, main.Main.OpenEntryAsync, main.Main.OpenProjectFileAsync);
        main.Main.Tabs.ObjectOpener = controller;
        return new Setup(game, workspace, main, controller, vanilla);
    }

    // Opens x_tables of the pack at the path the way the explorer does and returns its tab.
    private static async Task<TableTabViewModel> OpenTableAsync(Setup setup, string packPath)
    {
        var pack = setup.Workspace.Catalog!.Packs.Single(p => string.Equals(p.Path, packPath, StringComparison.OrdinalIgnoreCase));
        await setup.Main.Main.OpenEntryAsync(pack, XEntry);
        return Assert.IsType<TableTabViewModel>(setup.Tabs.SelectedTab);
    }

    // Shows the row with the key from the tab and returns the object tab that is selected afterwards.
    private static ObjectTabViewModel Show(Setup setup, RowsTabViewModel from, string key)
    {
        var index = Enumerable.Range(0, 100).First(i => from.KeyOfRow(i) is { } rowKey && rowKey.KeyValues.SequenceEqual(new[] { key }));
        setup.Controller.Show(from, from.KeyOfRow(index)!);
        return Assert.IsType<ObjectTabViewModel>(setup.Tabs.SelectedTab);
    }

    private static ProjectState NewProject(Setup setup, string name, object?[][] xRows)
    {
        var project = setup.Game.NewProject(setup.Projects, name);
        ObjectsTestSchema.WriteProjectTable(project.Folder, xRows);
        return project;
    }

    // Records the rows that the next rows tab added to the list asks its view to select.
    private static List<IReadOnlyList<int>> WatchSelections(TabsViewModel tabs)
    {
        var requested = new List<IReadOnlyList<int>>();
        tabs.Tabs.CollectionChanged += (_, e) =>
        {
            foreach (var tab in e.NewItems?.OfType<RowsTabViewModel>() ?? [])
            {
                tab.SelectionRequested += (_, rows) => requested.Add(rows);
            }
        };
        return requested;
    }

    [Fact] public async Task Show_opens_a_tab_builds_it_and_selects_it()
    {
        using var setup = await CreateAsync();
        var table = await OpenTableAsync(setup, setup.VanillaPath);

        var tab = Show(setup, table, "k1");

        Assert.Contains("k1", setup.Runner.Titles[^1]);
        Assert.Equal(1, setup.Builds("k1"));
        Assert.Equal(ObjectTabState.Ready, tab.State);
        Assert.Same(tab, Assert.Single(setup.Controller.ObjectTabs));
        var section = Assert.Single(tab.Result!.View.Sections);
        Assert.Equal(("y_tables", 2), (section.SourceTable, section.Rows.Count));
        Assert.Contains(tab.Lines, line => line.Spans.Any(span => span.Kind == SpanKind.TableName && span.Section == 0)
            && line.Spans.Any(span => span.Text == "(2)"));
        Assert.Equal("Vanilla + 0 mods", tab.Status);
    }

    [Fact] public async Task Show_for_the_same_object_activates_the_open_tab()
    {
        using var setup = await CreateAsync();
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        var first = Show(setup, table, "k1");
        setup.Tabs.SelectedTab = table;

        var second = Show(setup, table, "k1");

        Assert.Same(first, second);
        Assert.Single(setup.Controller.ObjectTabs);
        Assert.Equal(1, setup.Builds("k1"));
    }

    [Fact] public async Task A_row_of_a_data_folder_mod_adds_its_pack_to_the_load_order()
    {
        string? modPath = null;
        using var setup = await CreateAsync(game => modPath = ObjectsTestSchema.WriteModPack(game, "mod.pack", [["k1", "Mod label"]]));
        var table = await OpenTableAsync(setup, modPath!);

        var tab = Show(setup, table, "k1");

        Assert.Equal(modPath, tab.Current.RootPackPath);
        Assert.Contains(setup.Texts.ObjectsNoticeRootPackAdded, tab.Notice);
        var root = tab.Result!.View.Root!;
        Assert.Equal((SourceKind.Mod, modPath), (root.Provenance.Source.Kind, root.Provenance.Source.Path));
    }

    [Fact] public async Task A_row_of_a_pack_missing_from_the_catalog_adds_no_root_pack()
    {
        string? modPath = null;
        using var setup = await CreateAsync(game => modPath = ObjectsTestSchema.WriteModPack(game, "mod.pack", [["k1", "Mod label"]]));
        var table = await OpenTableAsync(setup, modPath!);
        File.Delete(modPath!);
        await setup.Workspace.RefreshAsync();
        Assert.DoesNotContain(setup.Workspace.Catalog!.Packs, pack => string.Equals(pack.Path, modPath, StringComparison.OrdinalIgnoreCase));

        var tab = Show(setup, table, "k1");

        Assert.Null(tab.Current.RootPackPath);
        Assert.DoesNotContain(tab.Result!.Order.Warnings, warning => warning.Problem == LoadOrderProblem.RootPackAdded);
        Assert.Equal(SourceKind.Vanilla, tab.Result.View.Root!.Provenance.Source.Kind);
    }

    [Fact] public async Task An_unsaved_project_table_shows_the_saved_state_notice()
    {
        using var setup = await CreateAsync();
        var project = NewProject(setup, "Alpha", [["k1", "Project label"]]);
        await setup.Main.Main.OpenProjectFileAsync(project.Id, @"db\x_tables\a.tsv");
        var editable = Assert.IsType<EditableRowsTabViewModel>(setup.Tabs.SelectedTab);
        editable.Rows[0][1] = "Changed";
        Assert.True(editable.IsModified);

        var tab = Show(setup, editable, "k1");

        Assert.Null(tab.Current.RootPackPath);
        Assert.Contains(setup.Texts.ObjectsNoticeUnsavedSource, tab.Notice);
        Assert.Equal(ObjectTabState.Ready, tab.State);
    }

    [Fact] public async Task DisabledReason_names_no_game_no_schema_and_busy()
    {
        using var setup = await CreateAsync();
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        var changed = 0;
        setup.Controller.Changed += (_, _) => changed++;
        Assert.Null(setup.Controller.DisabledReason(table));

        setup.Game.GameSettings.InstallPath = setup.Game.Root.File("nowhere");
        Assert.Equal(setup.Texts.ObjectsNoGame, setup.Controller.DisabledReason(table));
        Assert.True(changed > 0);

        File.Delete(Path.Combine(setup.Game.Paths.SchemasDirectory, ObjectsTestSchema.Profile.SchemaFile));
        setup.Game.GameSettings.InstallPath = setup.Game.InstallPath;
        Assert.Equal(setup.Texts.ObjectsNoSchema, setup.Controller.DisabledReason(table));

        ObjectsTestSchema.WriteSchema(setup.Game);
        await setup.Workspace.ReloadSchemaAsync();
        Assert.Null(setup.Controller.DisabledReason(table));
        setup.Runner.IsRunning = true;
        Assert.Equal(setup.Texts.ProjectBusy, setup.Controller.DisabledReason(table));
    }

    [Fact] public async Task Selecting_an_idle_tab_inside_a_lazy_build_scope_builds_nothing_until_the_scope_ends()
    {
        using var setup = await CreateAsync();
        var tab = setup.Controller.CreateTab([new ObjectRef("x_tables", ["k1"], null)], 0);

        var scope = setup.Controller.SuppressLazyBuilds();
        setup.Tabs.AddTab(tab);

        Assert.Same(tab, setup.Tabs.SelectedTab);
        Assert.Equal((ObjectTabState.Idle, 0), (tab.State, setup.Builds("k1")));

        scope.Dispose();
        scope.Dispose();                                                               // a second dispose changes nothing

        Assert.Equal((ObjectTabState.Ready, 1), (tab.State, setup.Builds("k1")));
    }

    [Fact] public async Task A_link_builds_the_target_in_the_same_tab_and_a_shift_click_opens_a_new_one()
    {
        using var setup = await CreateAsync();
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        var tab = Show(setup, table, "k1");

        tab.NavigateTo(new ObjectId("x_tables", ["k2"]), newTab: false);

        Assert.Same(tab, Assert.Single(setup.Controller.ObjectTabs));
        Assert.Equal(new[] { "k1", "k2" }, tab.History.Select(entry => entry.JoinedKey));
        Assert.Equal(1, tab.Position);
        Assert.Equal(ObjectTabState.Ready, tab.State);
        Assert.Equal(1, setup.Builds("k2"));

        tab.NavigateTo(new ObjectId("x_tables", ["k1"]), newTab: true);

        Assert.Equal(2, setup.Controller.ObjectTabs.Count);
        var opened = Assert.IsType<ObjectTabViewModel>(setup.Tabs.SelectedTab);
        Assert.NotSame(tab, opened);
        Assert.Equal("k1", Assert.Single(opened.History).JoinedKey);
        Assert.Equal(ObjectTabState.Ready, opened.State);
        Assert.Equal(2, setup.Builds("k1"));
        Assert.Equal("k2", tab.Current.JoinedKey);
    }

    [Fact] public async Task A_cancelled_build_leaves_the_tab_with_a_refresh()
    {
        using var setup = await CreateAsync();
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        setup.Runner.CancelNext = true;

        var tab = Show(setup, table, "k1");

        Assert.Equal(ObjectTabState.Cancelled, tab.State);
        Assert.Contains(setup.Texts.ObjectsStateCancelled, tab.Notice);
        Assert.Empty(tab.Lines);
        Assert.True(tab.RefreshCommand.CanExecute(null));

        tab.RefreshCommand.Execute(null);

        Assert.Equal(ObjectTabState.Ready, tab.State);
        Assert.Equal(2, setup.Builds("k1"));
    }

    [Fact] public async Task A_restored_tab_is_built_when_it_is_activated_and_the_runner_is_free()
    {
        using var setup = await CreateAsync();
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        var restored = setup.Controller.CreateTab([new ObjectRef("x_tables", ["k1"], null)], 0);
        setup.Tabs.AddTab(restored, select: false);
        Assert.Equal(ObjectTabState.Idle, restored.State);
        Assert.Same(table, setup.Tabs.SelectedTab);

        setup.Runner.IsRunning = true;
        setup.Tabs.SelectedTab = restored;
        Assert.Equal(ObjectTabState.Idle, restored.State);
        Assert.Equal(0, setup.Builds("k1"));

        setup.Runner.IsRunning = false;
        setup.Tabs.SelectedTab = table;
        setup.Tabs.SelectedTab = restored;

        Assert.Equal(ObjectTabState.Ready, restored.State);
        Assert.Equal(1, setup.Builds("k1"));
    }

    [Fact] public async Task A_schema_change_discards_the_store_and_marks_the_tabs()
    {
        using var setup = await CreateAsync();
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        var tab = Show(setup, table, "k1");
        var folder = setup.Game.Root.File("other-schemas");
        ObjectsTestSchema.WriteSchema(setup.Game, folder);

        setup.Game.Settings.SchemaFolder = folder;

        Assert.Equal(folder, Path.GetDirectoryName(setup.Workspace.Schema!.Path));
        Assert.True(tab.IsStale);
        Assert.Equal(setup.Texts.TabStaleSchema, tab.StaleText);

        tab.RefreshCommand.Execute(null);

        Assert.Equal(2, setup.Builds("k1"));
        Assert.Equal(ObjectTabState.Ready, tab.State);
        Assert.False(tab.IsStale);
    }

    [Fact] public async Task Refresh_without_a_game_says_why()
    {
        using var setup = await CreateAsync();
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        var tab = Show(setup, table, "k1");
        setup.Game.GameSettings.InstallPath = setup.Game.Root.File("nowhere");

        tab.RefreshCommand.Execute(null);

        Assert.Contains(setup.Texts.ObjectsNoGame, tab.Notice);
        Assert.Equal(ObjectTabState.Failed, tab.State);
        Assert.Equal(1, setup.Builds("k1"));
    }

    [Fact] public async Task A_changed_source_of_the_object_marks_the_tab_stale()
    {
        using var setup = await CreateAsync();
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        var tab = Show(setup, table, "k1");
        ObjectsTestSchema.WriteVanillaPack(setup.Game, "data.pack", ObjectsTestSchema.XRows, [["k1", 1], ["k1", 2], ["k1", 3]]);

        await setup.Controller.CheckStaleAsync();

        Assert.True(tab.IsStale);
        Assert.Equal(setup.Texts.ObjectsStale, tab.StaleText);
        Assert.Equal(ObjectTabState.Ready, tab.State);
        Assert.Equal(1, setup.Builds("k1"));

        tab.RefreshCommand.Execute(null);

        Assert.False(tab.IsStale);
        Assert.Equal(ObjectTabState.Ready, tab.State);
        Assert.Equal(2, setup.Builds("k1"));
    }

    [Fact] public async Task A_change_of_another_table_marks_nothing()
    {
        using var setup = await CreateAsync(game => ObjectsTestSchema.WriteZPack(game, "z.pack", [["z1"]]));
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        var tab = Show(setup, table, "k1");
        ObjectsTestSchema.WriteZPack(setup.Game, "z.pack", [["z1"], ["z2"], ["z3"]]);

        await setup.Controller.CheckStaleAsync();

        Assert.False(tab.IsStale);
    }

    [Fact] public async Task A_changed_project_table_of_the_object_marks_the_tab_stale()
    {
        using var setup = await CreateAsync();
        var project = NewProject(setup, "Alpha", [["k1", "Project label"]]);
        Assert.Equal(project.Id, setup.Projects.ActiveId);
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        var tab = Show(setup, table, "k1");
        Assert.Equal(SourceKind.Project, tab.Result!.View.Root!.Provenance.Source.Kind);
        Assert.False(tab.IsStale);

        ObjectsTestSchema.WriteProjectTable(project.Folder, [["k1", "A longer project label than before"]]);
        await setup.Controller.CheckStaleAsync();

        Assert.True(tab.IsStale);
        Assert.Equal(setup.Texts.ObjectsStale, tab.StaleText);
    }

    [Fact] public async Task Switching_the_active_project_marks_every_object_tab()
    {
        using var setup = await CreateAsync();
        NewProject(setup, "Alpha", [["k1", "Alpha label"]]);
        var beta = NewProject(setup, "Beta", [["k1", "Beta label"]]);
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        var first = Show(setup, table, "k1");
        var second = Show(setup, table, "k2");
        Assert.False(first.IsStale || second.IsStale);

        setup.Projects.SetActive(beta.Id);
        await setup.Controller.CheckStaleAsync();

        Assert.True(first.IsStale);
        Assert.True(second.IsStale);
    }

    [Fact] public async Task Jump_to_row_opens_the_table_and_selects_the_row()
    {
        using var setup = await CreateAsync();
        var project = NewProject(setup, "Alpha", [["k0", "Zero"], ["k1", "Project label"]]);
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        var fromVanilla = Show(setup, table, "k2");
        var fromProject = Show(setup, table, "k1");
        setup.Tabs.CloseWithoutAsking(table);
        var requested = WatchSelections(setup.Tabs);

        await setup.Controller.JumpAsync(fromVanilla);

        var opened = Assert.IsType<TableTabViewModel>(setup.Tabs.SelectedTab);
        Assert.Equal((setup.VanillaPath, XEntry), (opened.PackPath, opened.EntryPath));
        Assert.Equal(new[] { 1 }, Assert.Single(requested));
        Assert.DoesNotContain(setup.Texts.ObjectsNoticeRowNotFound, fromVanilla.Notice ?? string.Empty);

        await setup.Controller.JumpAsync(fromProject);

        var editable = Assert.IsType<EditableRowsTabViewModel>(setup.Tabs.SelectedTab);
        Assert.Equal((project.Id, "db/x_tables/a.tsv"), (editable.ProjectId, editable.EntryPath));
        Assert.Equal(new[] { 1 }, requested[^1]);
        Assert.Equal(2, requested.Count);
    }

    [Fact] public async Task Jump_to_a_row_whose_pack_is_gone_says_so()
    {
        using var setup = await CreateAsync();
        var table = await OpenTableAsync(setup, setup.VanillaPath);
        var tab = Show(setup, table, "k1");
        File.Delete(setup.VanillaPath);
        await setup.Workspace.RefreshAsync();
        Assert.DoesNotContain(setup.Workspace.Catalog!.Packs, pack => pack.Path == setup.VanillaPath);

        await setup.Controller.JumpAsync(tab);

        Assert.Contains(setup.Texts.ObjectsNoticeRowSourceGone, tab.Notice);
        Assert.Same(tab, setup.Tabs.SelectedTab);
    }

    [Fact] public async Task A_broken_category_file_is_logged_and_noted_on_its_tab()
    {
        using var setup = await CreateAsync(game =>
        {
            var folder = Path.Combine(game.Paths.ProfilesDirectory, ObjectsTestSchema.Profile.Id, "objects");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "x_tables.json"), "{ not json");
        });
        var table = await OpenTableAsync(setup, setup.VanillaPath);

        var tab = Show(setup, table, "k1");

        Assert.Contains("x_tables.json", Assert.Single(setup.Game.Log.Warnings));
        Assert.Contains(setup.Texts.ObjectsNoticeCategoryUnreadable, tab.Notice);
        Assert.Equal(ObjectTabState.Ready, tab.State);
    }

    [Fact] public async Task DisplayNameOf_comes_from_the_loc_pack_of_the_game()
    {
        using var setup = await CreateAsync(game => ObjectsTestSchema.WriteLocPack(game, new LocRow("x_name_k1", "One", false)));
        var table = await OpenTableAsync(setup, setup.VanillaPath);

        var tab = Show(setup, table, "k1");

        Assert.Equal("One", tab.DisplayNameOf(new ObjectId("x_tables", ["k1"])));
    }
}
