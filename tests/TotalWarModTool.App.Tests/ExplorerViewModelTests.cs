using TotalWarModTool.PackFiles;
using TotalWarModTool.Session;
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
            var restored = await c.RestoreAsync(new SessionState([], -1, [pack.Path.ToUpperInvariant()]));
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
}
