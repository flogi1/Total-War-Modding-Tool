using TotalWarModTool.Localization;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Session;
using TotalWarModTool.Storage;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class ExplorerViewModelTests
{
    private static async Task<(TestGame Game, WorkspaceController C, ExplorerViewModel Vm, List<(string Pack, string Entry)> Opened)> Create(Action<TestGame>? arrange = null, bool load = true)
    {
        var game = new TestGame(); arrange?.Invoke(game);
        var c = game.NewController(); var opened = new List<(string, string)>();
        var vm = new ExplorerViewModel(c, game.Texts, (p, e) => { opened.Add((p.Name, e)); return Task.CompletedTask; }, () => opened.Add(("settings", "")));
        if (load) await c.LoadAsync();
        return (game, c, vm, opened);
    }
    private static PackNode PackNodeOf(ExplorerViewModel vm, string name) => vm.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single(p => p.Pack.Name == name);

    [Fact] public async Task Messages_follow_the_state()
    {
        var (game, c, vm, opened) = await Create(load: false);
        using (game)
        {
            Assert.Equal(("Loading the pack list", false, false), (vm.Message, vm.ShowOpenSettings, vm.ShowTree));
            Assert.Equal("No packs were found in the game folder.", await Loaded());
            game.GameSettings.InstallPath = game.Root.File("nowhere");
            Assert.Equal(("The game folder from the settings has no data folder. Choose the game folder in the settings.", true), (vm.Message, vm.ShowOpenSettings));
            vm.OpenSettingsCommand.Execute(null);
            Assert.Equal(("settings", ""), Assert.Single(opened));
            async Task<string?> Loaded() { await c.LoadAsync(); return vm.Message; }
        }
    }

    // Review item 1: the explorer follows a language switch; what the user expanded stays as it is.
    [Fact] public async Task Group_names_and_the_message_follow_a_language_switch()
    {
        var locales = Path.Combine(RepoRoot.Find(), "locales");
        using var game = new TestGame(); game.AddPack("a.pack", ("a.txt", [1]));
        var ui = new UiText(locales); var texts = new AppTexts(ui);
        var c = new WorkspaceController(TestGame.Profile, game.GameSettings, game.Settings, game.Paths, new NoSteam(), game.Runner, game.Log, texts);
        var vm = new ExplorerViewModel(c, texts, (_, _) => Task.CompletedTask, () => { });
        await c.LoadAsync();
        var group = Assert.IsType<GroupNode>(Assert.Single(vm.Groups));
        var pack = PackNodeOf(vm, "a.pack");
        pack.IsExpanded = true; group.IsExpanded = true;
        var changed = new List<string?>(); group.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var raised = 0; vm.ExpandedPacksChanged += (_, _) => raised++;

        ui.Load("de");
        Assert.Equal("Mods im Datenordner (1)", group.Name);
        Assert.Contains(nameof(ExplorerNode.Name), changed);
        Assert.Same(group, Assert.Single(vm.Groups));                              // the same nodes, nothing was rebuilt
        Assert.Equal((true, true), (group.IsExpanded, pack.IsExpanded));
        Assert.Equal([pack.Pack.Path], vm.ExpandedPackPaths);
        Assert.Equal(0, raised);

        // The message of a game that is not found, in the other direction.
        game.GameSettings.InstallPath = game.Root.File("nowhere");
        Assert.Equal("Der Spielordner aus den Einstellungen hat keinen Datenordner. Wähle den Spielordner in den Einstellungen.", vm.Message);
        ui.Load("en");
        Assert.Equal("The game folder from the settings has no data folder. Choose the game folder in the settings.", vm.Message);
        Assert.True(vm.ShowOpenSettings);
    }

    [Fact] public async Task The_messages_for_a_missing_game_and_a_missing_profile()
    {
        using var game = new TestGame(inSteamLibrary: true);
        var noGame = game.NewController();                                       // Steam is not found: no stored path, no Steam folder
        var vm = new ExplorerViewModel(noGame, game.Texts, (_, _) => Task.CompletedTask, () => { });
        Assert.Equal(("The game was not found. Choose the game folder in the settings.", true, false), (vm.Message, vm.ShowOpenSettings, vm.ShowTree));

        var noProfile = new WorkspaceController(null, game.GameSettings, game.Settings, game.Paths, new NoSteam(), game.Runner, game.Log, game.Texts);
        var vm2 = new ExplorerViewModel(noProfile, game.Texts, (_, _) => Task.CompletedTask, () => { });
        Assert.Equal(("No game profile was found. The program folder is incomplete.", false), (vm2.Message, vm2.ShowOpenSettings));
        await noProfile.LoadAsync();
        Assert.Equal("No game profile was found. The program folder is incomplete.", vm2.Message);
    }

    [Fact] public async Task Groups_list_the_packs_by_origin_with_counts()
    {
        var (game, _, vm, _) = await Create(g => { g.AddPack("b.pack"); g.AddPack("a.pack"); });
        using (game)
        {
            Assert.Null(vm.Message);
            var group = Assert.IsType<GroupNode>(Assert.Single(vm.Groups));
            Assert.Equal(("Mods in the data folder (2)", PackOrigin.DataFolderMod, true), (group.Name, group.Origin, group.IsExpanded));
            Assert.Equal(["a.pack", "b.pack"], group.Children.Select(n => n.Name));
            Assert.All(group.Children, n => Assert.IsType<PlaceholderNode>(Assert.Single(n.Children)));
        }
    }

    [Fact] public async Task Expanding_a_pack_shows_its_tree_and_folders_fill_on_demand()
    {
        var (game, _, vm, opened) = await Create(g => g.AddPack("a.pack", ("script/main.lua", [1]), ("readme.txt", [1])));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack"); var raised = 0; vm.ExpandedPacksChanged += (_, _) => raised++;
            pack.IsExpanded = true;
            Assert.Equal(["script", "readme.txt"], pack.Children.Select(n => n.Name));
            var folder = Assert.IsType<FolderNode>(pack.Children[0]);
            Assert.IsType<PlaceholderNode>(Assert.Single(folder.Children));
            folder.IsExpanded = true;
            var entry = Assert.IsType<EntryNode>(Assert.Single(folder.Children));
            entry.OpenCommand.Execute(null);
            Assert.Equal(("a.pack", "script/main.lua"), Assert.Single(opened));
            Assert.Equal([pack.Pack.Path], vm.ExpandedPackPaths);
            Assert.Equal(1, raised);
        }
    }

    [Fact] public async Task A_folder_creates_its_children_only_when_it_is_expanded()
    {
        var entries = Enumerable.Range(0, 3000).Select(i => ($"big/file{i:D4}.txt", new byte[] { 1 })).ToArray();
        var (game, _, vm, _) = await Create(g => g.AddPack("a.pack", entries));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack");
            pack.IsExpanded = true;
            var folder = Assert.IsType<FolderNode>(Assert.Single(pack.Children));
            Assert.IsType<PlaceholderNode>(Assert.Single(folder.Children));
            folder.IsExpanded = true;
            Assert.Equal(3000, folder.Children.OfType<EntryNode>().Count());
            Assert.Equal("big/file0000.txt", ((EntryNode)folder.Children[0]).EntryPath);
            folder.IsExpanded = false; folder.IsExpanded = true;                      // not built a second time
            Assert.Equal(3000, folder.Children.Count);
        }
    }

    [Fact] public async Task Collapsing_a_pack_releases_its_index()
    {
        var (game, c, vm, _) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack");
            pack.IsExpanded = true; pack.IsExpanded = false;
            Assert.IsType<PlaceholderNode>(Assert.Single(pack.Children));
            Assert.Empty(vm.ExpandedPackPaths);
            Assert.Null(c.OpenContent(pack.Pack));
            pack.IsExpanded = true;
            Assert.Equal(["Open a.pack", "Open a.pack"], game.Runner.Titles);      // read again: the index was not kept
        }
    }

    [Fact] public async Task A_pack_that_fails_to_open_collapses_and_is_marked()
    {
        var (game, _, vm, _) = await Create(g => g.AddPack("a.pack", ("x", [1, 2, 3])));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack");
            File.WriteAllBytes(pack.Pack.Path, File.ReadAllBytes(pack.Pack.Path)[..^1]);
            pack.IsExpanded = true;
            Assert.Equal((false, true), (pack.IsExpanded, pack.HasProblem));
            Assert.StartsWith("The file list of the pack is damaged.", pack.ToolTip);
            Assert.Equal(["Open a.pack"], game.Runner.Titles);                     // the reset of IsExpanded did not start another read
            Assert.Empty(vm.ExpandedPackPaths);
            Assert.IsType<PlaceholderNode>(Assert.Single(pack.Children));
        }
    }

    [Fact] public async Task A_pack_that_opens_after_a_failure_loses_its_mark()
    {
        var (game, _, vm, _) = await Create(g => g.AddPack("a.pack", ("x", [1, 2, 3])));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack");
            var good = File.ReadAllBytes(pack.Pack.Path);
            File.WriteAllBytes(pack.Pack.Path, good[..^1]);
            pack.IsExpanded = true;
            Assert.True(pack.HasProblem);
            File.WriteAllBytes(pack.Pack.Path, good);
            pack.IsExpanded = true;
            Assert.Equal((true, false, pack.Pack.Path), (pack.IsExpanded, pack.HasProblem, pack.ToolTip));
            Assert.Equal(["x"], pack.Children.Select(n => n.Name));
        }
    }

    [Fact] public async Task A_cancelled_open_collapses_without_a_mark()
    {
        var (game, _, vm, _) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack");
            game.Runner.CancelNext = true;
            pack.IsExpanded = true;
            Assert.Equal((false, false), (pack.IsExpanded, pack.HasProblem));
        }
    }

    [Fact] public async Task Collapsing_while_the_pack_is_still_loading_leaves_it_collapsed_and_uncached()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("a.txt", [1]));
        var runner = new ManualOperationRunner();
        var c = game.NewController(runner: runner);
        var vm = new ExplorerViewModel(c, game.Texts, (_, _) => Task.CompletedTask, () => { });
        await c.LoadAsync();
        var pack = PackNodeOf(vm, "a.pack"); var raised = 0; vm.ExpandedPacksChanged += (_, _) => raised++;
        pack.IsExpanded = true;
        pack.IsExpanded = true;                                                      // already expanded: nothing happens
        pack.IsExpanded = false;                                                     // collapsed while the read runs
        Assert.Single(runner.Titles);
        runner.Complete();
        Assert.Equal((false, 0), (pack.IsExpanded, raised));
        Assert.IsType<PlaceholderNode>(Assert.Single(pack.Children));
        Assert.Null(c.OpenContent(pack.Pack));
        Assert.Empty(vm.ExpandedPackPaths);
    }

    [Fact] public async Task Expanding_again_while_the_pack_is_loading_starts_no_second_read()
    {
        using var game = new TestGame();
        game.AddPack("a.pack", ("a.txt", [1]));
        var runner = new ManualOperationRunner();
        var c = game.NewController(runner: runner);
        var vm = new ExplorerViewModel(c, game.Texts, (_, _) => Task.CompletedTask, () => { });
        await c.LoadAsync();
        var pack = PackNodeOf(vm, "a.pack");
        pack.IsExpanded = true; pack.IsExpanded = false; pack.IsExpanded = true;
        runner.Complete();
        Assert.Single(runner.Titles);
        Assert.True(pack.IsExpanded);
        Assert.Equal(["a.txt"], pack.Children.Select(n => n.Name));
        Assert.Equal([pack.Pack.Path], vm.ExpandedPackPaths);
    }

    [Fact] public async Task Broken_and_encrypted_packs_are_marked()
    {
        var (game, _, vm, _) = await Create(g =>
        {
            File.WriteAllBytes(Path.Combine(g.DataPath, "broken.pack"), new byte[10]);
            new PackBuilder().WithFlags(0x80).WriteTo(Path.Combine(g.DataPath, "encindex.pack"));
            new PackBuilder().WithFlags(0x10).AddEntry("a", [1]).WriteTo(Path.Combine(g.DataPath, "encdata.pack"));
        });
        using (game)
        {
            var broken = PackNodeOf(vm, "broken.pack");
            Assert.Equal((true, false), (broken.HasProblem, broken.CanExpand));
            Assert.StartsWith("The pack file is incomplete.", broken.ToolTip);
            Assert.Empty(broken.Children);
            var encIndex = PackNodeOf(vm, "encindex.pack");
            Assert.Equal((true, false, "Encrypted pack, not supported."), (encIndex.HasProblem, encIndex.CanExpand, encIndex.ToolTip));
            Assert.Empty(encIndex.Children);
            var encData = PackNodeOf(vm, "encdata.pack");
            Assert.Equal((true, true), (encData.HasProblem, encData.CanExpand));
            Assert.Equal("Encrypted pack, not supported.", encData.ToolTip);
            Assert.IsType<PlaceholderNode>(Assert.Single(encData.Children));
        }
    }

    [Fact] public async Task A_pack_that_cannot_expand_stays_collapsed_without_a_read()
    {
        var (game, _, vm, _) = await Create(g => File.WriteAllBytes(Path.Combine(g.DataPath, "broken.pack"), new byte[10]));
        using (game)
        {
            var broken = PackNodeOf(vm, "broken.pack");
            broken.IsExpanded = true;
            Assert.False(broken.IsExpanded);
            Assert.Empty(game.Runner.Titles);
        }
    }

    [Fact] public async Task Refresh_keeps_unchanged_packs_expanded_and_collapses_changed_ones()
    {
        var (game, c, vm, _) = await Create(g => { g.AddPack("a.pack", ("a.txt", [1])); g.AddPack("b.pack", ("b.txt", [1])); });
        using (game)
        {
            PackNodeOf(vm, "a.pack").IsExpanded = true; PackNodeOf(vm, "b.pack").IsExpanded = true;
            new PackBuilder().AddEntry("a.txt", [1, 2, 3]).WriteTo(PackNodeOf(vm, "a.pack").Pack.Path);
            await c.RefreshAsync();
            Assert.Equal((false, true), (PackNodeOf(vm, "a.pack").IsExpanded, PackNodeOf(vm, "b.pack").IsExpanded));
            Assert.Equal(["b.txt"], PackNodeOf(vm, "b.pack").Children.Select(n => n.Name));
        }
    }

    [Fact] public async Task A_refresh_never_reads_a_pack_again_and_reports_the_changed_expansion()
    {
        var (game, c, vm, _) = await Create(g => { g.AddPack("a.pack", ("a.txt", [1])); g.AddPack("b.pack", ("b.txt", [1])); });
        using (game)
        {
            PackNodeOf(vm, "a.pack").IsExpanded = true; PackNodeOf(vm, "b.pack").IsExpanded = true;
            var before = vm.ExpandedPackPaths.ToList(); var raised = 0; vm.ExpandedPacksChanged += (_, _) => raised++;
            new PackBuilder().AddEntry("a.txt", [1, 2, 3]).WriteTo(PackNodeOf(vm, "a.pack").Pack.Path);
            await c.RefreshAsync();
            Assert.Equal(["Open a.pack", "Open b.pack", "Refresh pack list"], game.Runner.Titles);   // no read of a.pack inside the refresh
            Assert.Equal([PackNodeOf(vm, "b.pack").Pack.Path], vm.ExpandedPackPaths);
            Assert.Equal(2, before.Count);
            Assert.True(raised >= 1);
        }
    }

    [Fact] public async Task A_refresh_keeps_the_state_of_the_groups()
    {
        var (game, c, vm, _) = await Create(g => g.AddPack("a.pack"));
        using (game)
        {
            vm.Groups.Single().IsExpanded = false;
            await c.RefreshAsync();
            Assert.False(vm.Groups.Single().IsExpanded);
            Assert.All(vm.Groups.SelectMany(g => g.Children), n => Assert.IsType<PackNode>(n));
        }
    }

    [Fact] public async Task A_pack_that_changed_on_disk_under_an_open_entry_is_collapsed_and_released()
    {
        var (game, c, vm, _) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack");
            pack.IsExpanded = true;
            new PackBuilder().AddEntry("a.txt", [1, 2, 3]).AddEntry("b.txt", [1]).WriteTo(pack.Pack.Path);
            await c.OpenEntryAsync(pack.Pack, "a.txt");                              // finds the pack changed and raises PackChanged
            Assert.Equal((false, 0), (pack.IsExpanded, vm.ExpandedPackPaths.Count));
            Assert.IsType<PlaceholderNode>(Assert.Single(pack.Children));
            Assert.Null(c.OpenContent(pack.Pack));
        }
    }

    [Fact] public async Task ApplyRestored_expands_without_a_runner_run()
    {
        var (game, c, vm, _) = await Create(g => g.AddPack("a.pack", ("a.txt", [1])));
        using (game)
        {
            var pack = PackNodeOf(vm, "a.pack").Pack;
            var restored = (await c.RestoreAsync(new SessionState([], -1, [pack.Path.ToUpperInvariant()])))!;
            vm.ApplyRestored(restored.Packs);
            Assert.True(PackNodeOf(vm, "a.pack").IsExpanded);
            Assert.Equal(["a.txt"], PackNodeOf(vm, "a.pack").Children.Select(n => n.Name));
            Assert.Empty(game.Runner.Titles);
        }
    }

    [Fact] public async Task Workshop_packs_show_their_id()
    {
        using var game = new TestGame(inSteamLibrary: true);
        game.AddWorkshopPack("777", "mod.pack", ("a.txt", [1]));
        var c = game.NewController(new FixedSteam(game.SteamPath));
        var vm = new ExplorerViewModel(c, game.Texts, (_, _) => Task.CompletedTask, () => { });
        await c.LoadAsync();
        var group = Assert.Single(vm.Groups);
        Assert.Equal(("Workshop (1)", PackOrigin.Workshop, false), (group.Name, ((GroupNode)group).Origin, group.IsExpanded));
        var pack = Assert.IsType<PackNode>(Assert.Single(group.Children));
        Assert.Equal(("mod.pack (777)", pack.Pack.Path), (pack.Name, pack.ToolTip));
    }

    // ---- projects --------------------------------------------------------------------------------------------------------------

    private sealed record ProjectSetup(TestGame Game, WorkspaceController C, ProjectsController Projects, ProjectsViewModel Commands,
        ExplorerViewModel Vm, List<(Guid Id, string File)> OpenedFiles) : IDisposable
    {
        public AppTexts Texts => Game.Texts;

        public void Dispose() => Game.Dispose();

        public ProjectsGroupNode Group => Assert.IsType<ProjectsGroupNode>(Vm.Groups[0]);

        public ProjectNode NodeOf(string name) => Group.Children.OfType<ProjectNode>().Single(n => n.Name == name);
    }

    // An explorer with projects over a TestGame; the projects are created before the explorer, as the list is loaded at start.
    private static async Task<ProjectSetup> CreateWithProjects(Action<TestGame>? arrange = null, Action<TestGame, ProjectsController>? projects = null,
        bool inSteamLibrary = false)
    {
        var game = new TestGame(inSteamLibrary: inSteamLibrary); arrange?.Invoke(game);
        var c = game.NewController();
        var p = game.NewProjects(c);
        projects?.Invoke(game, p);
        var commands = new ProjectsViewModel(p, new FakeProjectDialogs(), game.Texts);
        var opened = new List<(Guid, string)>();
        var vm = new ExplorerViewModel(c, game.Texts, (_, _) => Task.CompletedTask, () => { }, p, commands, (id, file) => { opened.Add((id, file)); return Task.CompletedTask; });
        await c.LoadAsync();
        return new ProjectSetup(game, c, p, commands, vm, opened);
    }

    [Fact] public async Task The_projects_group_comes_first_and_lists_the_projects()
    {
        using var setup = await CreateWithProjects(g => g.AddPack("a.pack"), projects: (g, p) =>
        {
            g.NewProject(p, "Alpha");
            var beta = g.NewProject(p, "Beta");
            p.SetActive(beta.Id);
        });
        var group = setup.Group;
        Assert.Equal(setup.Texts.ExplorerProjectsGroup(2), group.Name);
        Assert.Equal(["Alpha", "Beta"], group.Children.Select(n => n.Name));
        Assert.Equal([false, true], group.Children.Select(n => n.IsEmphasized));
        Assert.IsType<GroupNode>(setup.Vm.Groups[1]);
        Assert.All(group.Children, n => Assert.IsType<PlaceholderNode>(Assert.Single(n.Children)));
        Assert.Equal(setup.Projects.Projects[0].Folder, group.Children[0].ToolTip);
    }

    [Fact] public async Task The_group_is_there_without_projects_and_follows_the_list()
    {
        using var setup = await CreateWithProjects();
        Assert.Equal(setup.Texts.ExplorerProjectsGroup(0), setup.Group.Name);
        setup.Game.NewProject(setup.Projects, "Alpha");
        Assert.Equal((setup.Texts.ExplorerProjectsGroup(1), "Alpha"), (setup.Group.Name, Assert.Single(setup.Group.Children).Name));
        setup.Commands.Remove(setup.Projects.Projects[0].Id);
        Assert.Equal((setup.Texts.ExplorerProjectsGroup(0), 0), (setup.Group.Name, setup.Group.Children.Count));
    }

    [Fact] public async Task Projects_are_shown_while_the_game_is_not_found()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha"), inSteamLibrary: true);
        var vm = setup.Vm;
        Assert.Equal((setup.Texts.ExplorerGameNotFound, true, true), (vm.Message, vm.ShowOpenSettings, vm.ShowTree));
        Assert.Single(vm.Groups);
        Assert.Equal(["Alpha"], setup.Group.Children.Select(n => n.Name));
    }

    [Fact] public async Task Without_a_game_profile_there_is_no_projects_group()
    {
        using var game = new TestGame();
        var noProfile = new WorkspaceController(null, game.GameSettings, game.Settings, game.Paths, new NoSteam(), game.Runner, game.Log, game.Texts);
        var projects = new ProjectsController(null, new ProjectListLoadResult(ProjectList.Empty, LoadStatus.Missing, null),
            noProfile, game.Runner, game.Log, game.Texts, TimeProvider.System);
        var vm = new ExplorerViewModel(noProfile, game.Texts, (_, _) => Task.CompletedTask, () => { }, projects,
            new ProjectsViewModel(projects, new FakeProjectDialogs(), game.Texts), (_, _) => Task.CompletedTask);
        await noProfile.LoadAsync();
        Assert.Empty(vm.Groups);
        Assert.False(vm.ShowTree);
    }

    [Fact] public async Task A_broken_project_node_shows_its_problem_and_disables_its_actions()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha"));
        var state = setup.Projects.Projects[0];
        File.WriteAllText(Path.Combine(state.Folder, "twmt-project.json"), "{ not json");
        setup.Projects.Reload();
        var node = setup.Group.Children.OfType<ProjectNode>().Single();
        var reason = setup.Texts.ProjectProblemText(ProjectProblem.Corrupt);
        Assert.True(node.HasProblem);
        Assert.Contains(reason, node.ToolTip);
        Assert.Equal("alpha", node.Name);                                                // the name of the folder: the project file cannot be read
        node.IsExpanded = true;
        Assert.False(node.IsExpanded);                                                   // not expandable, no read
        Assert.Empty(node.Children);

        var build = Assert.Single(node.Actions, a => a.Header == setup.Texts.BuildMessageTitle);
        Assert.Equal(reason, build.DisabledReason);
        Assert.False(build.Command.CanExecute(null));
        var remove = Assert.Single(node.Actions, a => a.Header == setup.Texts.ProjectActionRemove);
        Assert.Null(remove.DisabledReason);
        Assert.True(remove.Command.CanExecute(null));
    }

    [Fact] public async Task Expanding_a_project_shows_folders_before_files()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha", (@"db\units_tables\a.tsv", "x"), ("b.lua", "x"), ("A.lua", "x")));
        var node = setup.NodeOf("Alpha"); var raised = 0; setup.Vm.ExpandedPacksChanged += (_, _) => raised++;
        node.IsExpanded = true;
        Assert.Equal(["db", "A.lua", "b.lua"], node.Children.Select(n => n.Name));
        Assert.IsType<ProjectFolderNode>(node.Children[0]);
        Assert.IsType<ProjectFileNode>(node.Children[1]);
        Assert.Equal(setup.Texts.OperationOpenProject("Alpha"), setup.Game.Runner.Titles[^1]);
        Assert.Equal([node.ProjectId], setup.Vm.ExpandedProjectIds);
        Assert.Equal(1, raised);

        var folder = (ProjectFolderNode)node.Children[0];
        Assert.IsType<PlaceholderNode>(Assert.Single(folder.Children));
        folder.IsExpanded = true;
        var inner = Assert.IsType<ProjectFolderNode>(Assert.Single(folder.Children));
        inner.IsExpanded = true;
        Assert.Equal(["a.tsv"], inner.Children.Select(n => n.Name));

        node.IsExpanded = false;                                                         // collapsed: the tree is released, the next expand reads again
        Assert.IsType<PlaceholderNode>(Assert.Single(node.Children));
        Assert.Empty(setup.Vm.ExpandedProjectIds);
    }

    [Fact] public async Task An_empty_project_stays_expanded_without_children()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha"));
        var node = setup.NodeOf("Alpha");
        node.IsExpanded = true;                                                          // the pack folder is empty
        Assert.Equal((true, 0), (node.IsExpanded, node.Children.Count));
    }

    [Fact] public async Task A_problem_of_the_list_hangs_below_the_project_as_a_marked_node()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha", ("a.lua", "x")));
        var state = setup.Projects.Projects[0];
        var list = new ProjectFileList(true, [new ProjectFileItem("a.lua", "a.lua", ProjectFileKind.Raw)], [],
            [new ProjectFileProblem("sub", "links to folders are not supported")]);
        setup.Vm.ApplyRestoredProjects([(state.Id, list)]);
        var node = setup.NodeOf("Alpha");
        Assert.Equal(["a.lua", "sub"], node.Children.Select(n => n.Name));
        var problem = node.Children[1];
        Assert.True(problem.HasProblem);
        Assert.Equal("links to folders are not supported", problem.ToolTip);
    }

    [Fact] public async Task A_cancelled_listing_collapses_the_project_again()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha", ("a.lua", "x")));
        var node = setup.NodeOf("Alpha");
        setup.Game.Runner.CancelNext = true;
        node.IsExpanded = true;
        Assert.False(node.IsExpanded);
        Assert.IsType<PlaceholderNode>(Assert.Single(node.Children));
        Assert.Empty(setup.Vm.ExpandedProjectIds);
    }

    [Fact] public async Task Opening_a_project_file_calls_the_delegate_with_id_and_relative_file()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha", (@"db\units_tables\a.tsv", "x")));
        var node = setup.NodeOf("Alpha");
        node.IsExpanded = true;
        var folder = (ProjectFolderNode)node.Children[0]; folder.IsExpanded = true;
        var inner = (ProjectFolderNode)folder.Children[0]; inner.IsExpanded = true;
        var file = Assert.IsType<ProjectFileNode>(Assert.Single(inner.Children));
        file.OpenCommand.Execute(null);
        Assert.Equal((node.ProjectId, @"db\units_tables\a.tsv"), Assert.Single(setup.OpenedFiles));
        Assert.Equal((node.ProjectId, @"db\units_tables\a.tsv"), (file.ProjectId, file.RelativeFile));
    }

    [Fact] public async Task A_change_of_the_list_keeps_expanded_projects_and_updates_names()
    {
        using var setup = await CreateWithProjects(projects: (g, p) =>
        {
            g.NewProject(p, "Alpha", ("a.lua", "x"));
            g.NewProject(p, "Beta");
        });
        var a = setup.NodeOf("Alpha"); var b = setup.NodeOf("Beta");
        a.IsExpanded = true;
        var children = a.Children.ToList();
        setup.Commands.SetActive(b.ProjectId);
        var file = Path.Combine(setup.Projects.Projects[0].Folder, "twmt-project.json");
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"Alpha\"", "\"Alpha renamed\""));
        setup.Projects.Reload();

        Assert.Same(a, setup.Group.Children[0]);
        Assert.Equal(("Alpha renamed", true, false, true), (a.Name, a.IsExpanded, a.IsEmphasized, b.IsEmphasized));
        Assert.Equal(children, a.Children);
        Assert.Equal([a.ProjectId], setup.Vm.ExpandedProjectIds);

        setup.Commands.Remove(a.ProjectId);                                              // a node that is gone from the list goes from the tree
        Assert.Equal([b], setup.Group.Children);
        Assert.Empty(setup.Vm.ExpandedProjectIds);
    }

    [Fact] public async Task A_silent_refresh_replaces_the_children_and_keeps_expanded_folders()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha", (@"db\a.tsv", "x"), (@"other\o.lua", "x")));
        var node = setup.NodeOf("Alpha");
        node.IsExpanded = true;
        var db = node.Children.OfType<ProjectFolderNode>().Single(f => f.Name == "db"); db.IsExpanded = true;
        var titles = setup.Game.Runner.Titles.Count;
        var state = setup.Projects.Projects[0];
        File.WriteAllText(Path.Combine(state.Folder, "pack", "db", "b.tsv"), "x");
        File.WriteAllText(Path.Combine(state.Folder, "pack", "new.lua"), "x");

        await setup.Vm.RefreshProjectFilesAsync();

        Assert.Equal(["db", "other", "new.lua"], node.Children.Select(n => n.Name));
        var newDb = node.Children.OfType<ProjectFolderNode>().Single(f => f.Name == "db");
        Assert.True(newDb.IsExpanded);
        Assert.Equal(["a.tsv", "b.tsv"], newDb.Children.Select(n => n.Name));
        Assert.False(node.Children.OfType<ProjectFolderNode>().Single(f => f.Name == "other").IsExpanded);
        Assert.Equal(titles, setup.Game.Runner.Titles.Count);                            // silent: no runner run
        Assert.True(node.IsExpanded);
    }

    [Fact] public async Task A_pack_refresh_does_not_collapse_the_projects()
    {
        using var setup = await CreateWithProjects(g => g.AddPack("a.pack"), projects: (g, p) => g.NewProject(p, "Alpha", ("a.lua", "x")));
        var node = setup.NodeOf("Alpha"); var group = setup.Group;
        node.IsExpanded = true; group.IsExpanded = true;
        setup.Game.AddPack("b.pack");
        await setup.C.RefreshAsync();
        Assert.Same(group, setup.Vm.Groups[0]);
        Assert.Equal(2, setup.Vm.Groups.Count);
        Assert.Same(node, Assert.Single(group.Children));
        Assert.Equal((true, true, 1), (group.IsExpanded, node.IsExpanded, node.Children.Count));
    }

    [Fact] public async Task A_pack_node_offers_import_and_a_project_node_its_five_actions()
    {
        using var setup = await CreateWithProjects(g => g.AddPack("a.pack"), projects: (g, p) => g.NewProject(p, "Alpha"));
        var texts = setup.Texts;
        var pack = PackNodeOf(setup.Vm, "a.pack");
        var import = Assert.Single(pack.Actions);
        Assert.Equal((texts.ProjectActionImport, (string?)null), (import.Header, import.DisabledReason));
        Assert.True(pack.HasActions);
        Assert.False(setup.Group.HasActions);

        var actions = setup.NodeOf("Alpha").Actions;
        Assert.Equal([texts.ProjectActionSetActive, texts.BuildMessageTitle, texts.ProjectActionBuildAndInstall, texts.UninstallMessageTitle, texts.ProjectActionRemove],
            actions.Select(a => a.Header));
        Assert.Equal(texts.UninstallNothing, actions[3].DisabledReason);
        Assert.False(actions[3].Command.CanExecute(null));
        Assert.All(new[] { 0, 1, 2, 4 }, i => Assert.Null(actions[i].DisabledReason));

        // The pack action starts the import: the pack is read first, then the (fake) dialog is cancelled.
        import.Command.Execute(null);
        Assert.Contains(texts.OperationReadPack("a.pack"), setup.Game.Runner.Titles);
    }

    [Fact] public async Task Install_actions_say_why_they_are_disabled_without_a_game_folder()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha"), inSteamLibrary: true);
        var actions = setup.NodeOf("Alpha").Actions;
        Assert.Equal(setup.Texts.InstallNoGameFolder, actions[2].DisabledReason);
        Assert.Null(actions[1].DisabledReason);
        Assert.Equal(setup.Texts.InstallNoGameFolder, actions[3].DisabledReason);
    }

    // The start-up load is a silent run: while it runs the workspace is busy. List edits stay possible, the entries that need a free runner say why not.
    [Fact] public async Task During_the_load_set_active_and_remove_work_and_the_runner_entries_say_busy()
    {
        using var game = new TestGame();
        var c = game.NewController();
        var p = game.NewProjects(c);
        game.NewProject(p, "Alpha");
        game.AddPack("a.pack");
        var commands = new ProjectsViewModel(p, new FakeProjectDialogs(), game.Texts);
        var vm = new ExplorerViewModel(c, game.Texts, (_, _) => Task.CompletedTask, () => { }, p, commands, (_, _) => Task.CompletedTask);
        var texts = game.Texts;

        var load = c.LoadAsync();                                                    // returns at its first await: the workspace is busy now
        Assert.True(c.IsBusy);
        var node = Assert.IsType<ProjectNode>(Assert.Single(vm.Groups[0].Children));
        var actions = node.Actions;
        Assert.Equal(((string?)null, true), (actions[0].DisabledReason, actions[0].Command.CanExecute(null)));        // set as active
        Assert.Equal(((string?)null, true), (actions[4].DisabledReason, actions[4].Command.CanExecute(null)));        // remove from list
        Assert.Equal((texts.ProjectBusy, false), (actions[1].DisabledReason, actions[1].Command.CanExecute(null)));   // build
        Assert.Equal(texts.ProjectBusy, actions[2].DisabledReason);                  // build and install
        Assert.Equal((texts.UninstallNothing, false), (actions[3].DisabledReason, actions[3].Command.CanExecute(null)));   // the reason of the project comes before "busy"

        await load;
        node = Assert.IsType<ProjectNode>(Assert.Single(vm.Groups[0].Children));
        Assert.Equal(((string?)null, true), (node.Actions[1].DisabledReason, node.Actions[1].Command.CanExecute(null)));
        var import = Assert.Single(PackNodeOf(vm, "a.pack").Actions);
        Assert.Equal(((string?)null, true), (import.DisabledReason, import.Command.CanExecute(null)));
    }

    [Fact] public async Task While_a_run_is_going_the_runner_entries_are_disabled_and_list_edits_are_not()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => { g.NewProject(p, "Alpha"); g.NewProject(p, "Beta"); });
        var actions = setup.NodeOf("Alpha").Actions;
        setup.Game.Runner.IsRunning = true;                                          // a user run: the commands ask live, the window is locked anyway
        Assert.Equal([true, false, false, false, true], actions.Select(a => a.Command.CanExecute(null)));
        setup.NodeOf("Beta").Actions[0].Command.Execute(null);                       // set Beta as active
        Assert.Equal([false, true], setup.Group.Children.Select(n => n.IsEmphasized));
    }

    [Fact] public async Task A_project_without_its_pack_folder_expands_to_one_problem_node()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha"));
        Directory.Delete(Path.Combine(setup.Projects.Projects[0].Folder, "pack"));
        var node = setup.NodeOf("Alpha");
        node.IsExpanded = true;
        var child = Assert.Single(node.Children);
        Assert.Equal(("pack", true, setup.Texts.ProjectPackFolderMissing), (child.Name, child.HasProblem, child.ToolTip));
        Assert.True(node.IsExpanded);
    }

    // The runner listing of an expand and the silent listings after F5 can overlap: only the newest state of the node may show its result.
    [Fact] public async Task A_held_silent_listing_never_replaces_the_result_of_a_newer_listing()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha", ("a.lua", "x")));
        var node = setup.NodeOf("Alpha"); var state = setup.Projects.Projects[0];
        node.IsExpanded = true;
        var old = ProjectFiles.List(state.Folder, CancellationToken.None);          // what the held listing has read
        var gate = new TaskCompletionSource<ProjectFileList?>();
        setup.Vm.SilentListing = _ => gate.Task;
        var refresh = setup.Vm.RefreshProjectFilesAsync();                           // held

        node.IsExpanded = false;
        File.WriteAllText(Path.Combine(state.Folder, "pack", "fresh.lua"), "x");
        node.IsExpanded = true;                                                      // the fresh listing of the runner lands at once
        Assert.Equal(["a.lua", "fresh.lua"], node.Children.Select(n => n.Name));

        gate.SetResult(old);
        await refresh;
        Assert.Equal(["a.lua", "fresh.lua"], node.Children.Select(n => n.Name));
        Assert.True(node.IsExpanded);
    }

    [Fact] public async Task Of_two_silent_listings_the_one_started_last_stays()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha", ("a.lua", "x")));
        var node = setup.NodeOf("Alpha"); var state = setup.Projects.Projects[0];
        node.IsExpanded = true;
        var older = ProjectFiles.List(state.Folder, CancellationToken.None);
        File.WriteAllText(Path.Combine(state.Folder, "pack", "newer.lua"), "x");
        var newer = ProjectFiles.List(state.Folder, CancellationToken.None);
        var first = new TaskCompletionSource<ProjectFileList?>();
        var second = new TaskCompletionSource<ProjectFileList?>();
        var gates = new Queue<TaskCompletionSource<ProjectFileList?>>();
        gates.Enqueue(first);
        gates.Enqueue(second);
        setup.Vm.SilentListing = _ => gates.Dequeue().Task;
        var one = setup.Vm.RefreshProjectFilesAsync();
        var two = setup.Vm.RefreshProjectFilesAsync();

        second.SetResult(newer);                                                     // they finish in reverse order
        first.SetResult(older);
        await Task.WhenAll(one, two);
        Assert.Equal(["a.lua", "newer.lua"], node.Children.Select(n => n.Name));
    }

    // The silent loop takes its nodes first and awaits each listing: a node that the user collapsed and expands again meanwhile has a runner
    // listing in flight, which the loop must not invalidate.
    [Fact] public async Task The_silent_loop_leaves_a_node_alone_whose_expand_started_meanwhile()
    {
        using var game = new TestGame();
        var manual = new ManualOperationRunner();
        var c = game.NewController();
        var p = game.NewProjects(c, manual);                                         // the runner listings of the explorer wait in the manual runner
        game.NewProject(p, "Alpha", ("a.lua", "x"));
        game.NewProject(p, "Beta", ("b.lua", "x"));
        var commands = new ProjectsViewModel(p, new FakeProjectDialogs(), game.Texts);
        var vm = new ExplorerViewModel(c, game.Texts, (_, _) => Task.CompletedTask, () => { }, p, commands, (_, _) => Task.CompletedTask);
        await c.LoadAsync();
        var group = Assert.IsType<ProjectsGroupNode>(vm.Groups[0]);
        var alpha = group.Children.OfType<ProjectNode>().Single(n => n.Name == "Alpha");
        var beta = group.Children.OfType<ProjectNode>().Single(n => n.Name == "Beta");
        alpha.IsExpanded = true; manual.Complete();
        beta.IsExpanded = true; manual.Complete();
        Assert.Equal([alpha.ProjectId, beta.ProjectId], vm.ExpandedProjectIds);

        var alphaList = ProjectFiles.List(p.Projects[0].Folder, CancellationToken.None);
        var betaList = ProjectFiles.List(p.Projects[1].Folder, CancellationToken.None);
        var gate = new TaskCompletionSource<ProjectFileList?>();
        vm.SilentListing = id => id == alpha.ProjectId ? gate.Task : Task.FromResult<ProjectFileList?>(betaList);
        var refresh = vm.RefreshProjectFilesAsync();                                 // held on the listing of Alpha

        beta.IsExpanded = false;
        beta.IsExpanded = true;                                                      // a runner expand of Beta is in flight now
        Assert.True(beta.IsLoading);
        gate.SetResult(alphaList);
        await refresh;                                                               // the loop reaches Beta and must skip it
        manual.Complete();                                                           // the expand delivers

        Assert.Equal((true, false), (beta.IsExpanded, beta.IsLoading));
        Assert.Equal(["b.lua"], beta.Children.Select(n => n.Name));
        Assert.Equal([alpha.ProjectId, beta.ProjectId], vm.ExpandedProjectIds);
    }

    [Fact] public async Task A_project_that_was_open_with_no_files_and_breaks_is_no_longer_open()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha"));
        var node = setup.NodeOf("Alpha");
        node.IsExpanded = true;                                                      // the pack folder is empty: open, without children
        Assert.Equal([node.ProjectId], setup.Vm.ExpandedProjectIds);

        File.WriteAllText(Path.Combine(setup.Projects.Projects[0].Folder, "twmt-project.json"), "{ not json");
        setup.Projects.Reload();

        Assert.Equal((true, false), (node.HasProblem, node.IsExpanded));
        Assert.Empty(setup.Vm.ExpandedProjectIds);
        Assert.Empty(node.Children);
    }

    [Fact] public async Task A_listing_that_finishes_after_its_project_was_removed_changes_nothing()
    {
        using var game = new TestGame();
        var manual = new ManualOperationRunner();
        var c = game.NewController();
        var p = game.NewProjects(c, manual);                                         // the listings of the explorer wait in the manual runner
        game.NewProject(p, "Alpha", ("a.lua", "x"));
        var commands = new ProjectsViewModel(p, new FakeProjectDialogs(), game.Texts);
        var vm = new ExplorerViewModel(c, game.Texts, (_, _) => Task.CompletedTask, () => { }, p, commands, (_, _) => Task.CompletedTask);
        await c.LoadAsync();
        var group = Assert.IsType<ProjectsGroupNode>(vm.Groups[0]);
        var node = Assert.IsType<ProjectNode>(Assert.Single(group.Children));

        node.IsExpanded = true;                                                      // held
        commands.Remove(node.ProjectId);
        manual.Complete();                                                           // the continuation runs inline: it must not throw

        Assert.Empty(group.Children);
        Assert.Empty(vm.ExpandedProjectIds);
        Assert.IsType<PlaceholderNode>(Assert.Single(node.Children));
    }

    [Fact] public async Task A_silent_listing_that_finishes_after_its_project_was_removed_changes_nothing()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha", ("a.lua", "x")));
        var node = setup.NodeOf("Alpha"); var state = setup.Projects.Projects[0];
        node.IsExpanded = true;
        var list = ProjectFiles.List(state.Folder, CancellationToken.None);
        var gate = new TaskCompletionSource<ProjectFileList?>();
        setup.Vm.SilentListing = _ => gate.Task;
        var refresh = setup.Vm.RefreshProjectFilesAsync();

        setup.Commands.Remove(state.Id);
        gate.SetResult(list);
        await refresh;

        Assert.Empty(setup.Group.Children);
        Assert.Equal(["a.lua"], node.Children.Select(n => n.Name));
        Assert.Empty(setup.Vm.ExpandedProjectIds);
    }

    [Fact] public async Task ApplyRestoredProjects_expands_without_a_runner_run()
    {
        using var setup = await CreateWithProjects(projects: (g, p) => g.NewProject(p, "Alpha", ("a.lua", "x")));
        var state = setup.Projects.Projects[0];
        var list = ProjectFiles.List(state.Folder, CancellationToken.None);
        var titles = setup.Game.Runner.Titles.Count; var raised = 0; setup.Vm.ExpandedPacksChanged += (_, _) => raised++;
        setup.Vm.ApplyRestoredProjects([(state.Id, list), (Guid.NewGuid(), list)]);          // an unknown project is skipped
        var node = setup.NodeOf("Alpha");
        Assert.Equal((true, 1, 1), (node.IsExpanded, node.Children.Count, raised));
        Assert.Equal([state.Id], setup.Vm.ExpandedProjectIds);
        Assert.Equal(titles, setup.Game.Runner.Titles.Count);
    }

    [Fact] public async Task The_group_name_and_the_actions_follow_a_language_switch()
    {
        var locales = Path.Combine(RepoRoot.Find(), "locales");
        using var game = new TestGame(); game.AddPack("a.pack");
        var ui = new UiText(locales); var texts = new AppTexts(ui);
        var c = new WorkspaceController(TestGame.Profile, game.GameSettings, game.Settings, game.Paths, new NoSteam(), game.Runner, game.Log, texts);
        var store = new ProjectListStore(game.Paths.RoamingRoot, TestGame.Profile.Id);
        var p = new ProjectsController(store, store.Load(), c, game.Runner, game.Log, texts, TimeProvider.System);
        game.NewProject(p, "Alpha");
        var vm = new ExplorerViewModel(c, texts, (_, _) => Task.CompletedTask, () => { }, p, new ProjectsViewModel(p, new FakeProjectDialogs(), texts),
            (_, _) => Task.CompletedTask);
        await c.LoadAsync();
        var group = Assert.IsType<ProjectsGroupNode>(vm.Groups[0]);
        var node = group.Children.OfType<ProjectNode>().Single();
        ui.Load("de");
        Assert.Equal("Projekte (1)", group.Name);
        Assert.Equal(["Als aktiv setzen", "Bauen", "Bauen und installieren", "Deinstallieren", "Aus der Liste entfernen"], node.Actions.Select(a => a.Header));
        Assert.Same(group, vm.Groups[0]);
        Assert.Equal("Als Projekt importieren", Assert.Single(PackNodeOf(vm, "a.pack").Actions).Header);
        Assert.Equal("Mods im Datenordner (1)", vm.Groups[1].Name);                          // the pack groups follow too, behind the projects
    }
}
