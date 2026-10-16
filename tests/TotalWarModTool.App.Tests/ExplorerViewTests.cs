using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace TotalWarModTool.App.Tests;

public class ExplorerViewTests
{
    private sealed record Setup(TestGame Game, WorkspaceController Controller, ExplorerViewModel ViewModel, ExplorerView View, Window Window,
        List<(string Pack, string Entry)> Opened, List<string> SettingsCalls) : IDisposable
    {
        public void Dispose() => Game.Dispose();

        public TreeView Tree => (TreeView)View.FindName("Tree");

        public TextBlock MessageBlock => (TextBlock)View.FindName("MessageBlock");

        public Button OpenSettingsButton => (Button)View.FindName("OpenSettingsButton");
    }

    // A view model of a loaded TestGame inside a shown window, with the styles of Controls.xaml as in the app.
    private static async Task<Setup> Create(Action<TestGame>? arrange = null, double height = 400)
    {
        var game = new TestGame(); arrange?.Invoke(game);
        var controller = game.NewController();
        var opened = new List<(string, string)>(); var settingsCalls = new List<string>();
        var viewModel = new ExplorerViewModel(controller, game.Texts, (p, e) => { opened.Add((p.Name, e)); return Task.CompletedTask; }, () => settingsCalls.Add("settings"));
        await controller.LoadAsync();

        // The test host has no Application, so the styles of Controls.xaml go into the view's own resources, and the base of the container style is taken again.
        var view = new ExplorerView(viewModel);
        view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(
            File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml"))));
        view.ApplyThemedItemStyle();
        var window = new Window { Width = 400, Height = height, Content = view };
        window.Show();
        Pump(window);
        return new Setup(game, controller, viewModel, view, window, opened, settingsCalls);
    }

    // Bindings and the generation of containers are delivered by the dispatcher; a window nobody pumps needs this nudge.
    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private static TreeViewItem ContainerOf(Window window, ItemsControl parent, object item)
    {
        Pump(window);
        return Assert.IsType<TreeViewItem>(parent.ItemContainerGenerator.ContainerFromItem(item));
    }

    private static int RealizedContainers(DependencyObject root)
    {
        var count = root is TreeViewItem ? 1 : 0;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            count += RealizedContainers(VisualTreeHelper.GetChild(root, i));
        }

        return count;
    }

    [Fact] public void Tree_shows_groups_and_message_is_hidden() => Sta.RunAsync(async () =>
    {
        using var setup = await Create(g => { g.AddPack("a.pack"); g.AddPack("b.pack"); });
        Assert.Equal((1, Visibility.Visible), (setup.Tree.Items.Count, setup.Tree.Visibility));
        Assert.Same(setup.ViewModel.Groups[0], setup.Tree.Items[0]);
        Assert.Equal((Visibility.Collapsed, Visibility.Collapsed), (setup.MessageBlock.Visibility, setup.OpenSettingsButton.Visibility));
    });

    [Fact] public void Message_replaces_the_tree_when_the_game_is_not_found() => Sta.RunAsync(async () =>
    {
        using var setup = await Create(g => g.GameSettings.InstallPath = g.Root.File("nowhere"));
        Assert.Equal(Visibility.Collapsed, setup.Tree.Visibility);
        Assert.Equal((Visibility.Visible, Visibility.Visible), (setup.MessageBlock.Visibility, setup.OpenSettingsButton.Visibility));
        Assert.Equal("The game folder from the settings has no data folder. Choose the game folder in the settings.", setup.MessageBlock.Text);
        Assert.Equal(TextWrapping.Wrap, setup.MessageBlock.TextWrapping);
        Assert.Equal("Open settings", setup.OpenSettingsButton.Content);

        // Pressing the button, as the user does.
        var invoke = (IInvokeProvider)new ButtonAutomationPeer(setup.OpenSettingsButton).GetPattern(PatternInterface.Invoke)!;
        invoke.Invoke();
        Pump(setup.Window);                                                          // the peer clicks through the dispatcher
        Assert.Single(setup.SettingsCalls);
    });

    [Fact] public void Enter_on_an_entry_opens_it() => Sta.RunAsync(async () =>
    {
        using var setup = await Create(g => g.AddPack("a.pack", ("script/main.lua", [1]), ("readme.txt", [1])));
        var vm = setup.ViewModel;
        var pack = vm.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single();
        pack.IsExpanded = true;
        var group = ContainerOf(setup.Window, setup.Tree, vm.Groups[0]);
        var packContainer = ContainerOf(setup.Window, group, pack);
        var entry = pack.Children.OfType<EntryNode>().Single();
        var entryContainer = ContainerOf(setup.Window, packContainer, entry);

        // Enter on a node that is no entry does nothing.
        packContainer.IsSelected = true;
        RaiseEnter(setup.Tree);
        Assert.Empty(setup.Opened);

        entryContainer.IsSelected = true;
        RaiseEnter(setup.Tree);
        Assert.Equal(("a.pack", "readme.txt"), Assert.Single(setup.Opened));
    });

    [Fact] public void A_double_click_on_an_entry_opens_it_and_on_a_folder_does_not() => Sta.RunAsync(async () =>
    {
        using var setup = await Create(g => g.AddPack("a.pack", ("script/main.lua", [1]), ("readme.txt", [1])));
        var vm = setup.ViewModel;
        var pack = vm.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single();
        pack.IsExpanded = true;
        var packContainer = ContainerOf(setup.Window, ContainerOf(setup.Window, setup.Tree, vm.Groups[0]), pack);

        RaiseDoubleClick(ContainerOf(setup.Window, packContainer, pack.Children.OfType<FolderNode>().Single()));
        Assert.Empty(setup.Opened);
        var entryContainer = ContainerOf(setup.Window, packContainer, pack.Children.OfType<EntryNode>().Single());
        RaiseDoubleClick(entryContainer);
        Assert.Equal(("a.pack", "readme.txt"), Assert.Single(setup.Opened));
    });

    [Fact] public void Expansion_is_bound_in_both_directions() => Sta.RunAsync(async () =>
    {
        using var setup = await Create(g => { g.AddPack("a.pack", ("a.txt", [1])); g.AddPack("b.pack", ("b.txt", [1])); });
        var vm = setup.ViewModel;
        var a = vm.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single(p => p.Name == "a.pack");
        var b = vm.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single(p => p.Name == "b.pack");
        var groupContainer = ContainerOf(setup.Window, setup.Tree, vm.Groups[0]);

        // The tree expands a container: the view model reads the pack.
        ContainerOf(setup.Window, groupContainer, a).IsExpanded = true;
        Assert.True(a.IsExpanded);
        Assert.Equal(["a.txt"], a.Children.Select(n => n.Name));

        // The view model collapses a node that failed: the container follows.
        var bContainer = ContainerOf(setup.Window, groupContainer, b);
        File.WriteAllBytes(b.Pack.Path, File.ReadAllBytes(b.Pack.Path)[..^1]);
        bContainer.IsExpanded = true;
        Assert.Equal((false, true), (bContainer.IsExpanded, b.HasProblem));
        Assert.Equal(["Open a.pack", "Open b.pack"], setup.Game.Runner.Titles);
    });

    [Fact] public void The_containers_use_the_themed_item_style_with_the_expansion_binding() => Sta.RunAsync(async () =>
    {
        using var setup = await Create(g => g.AddPack("a.pack"));
        var group = ContainerOf(setup.Window, setup.Tree, setup.ViewModel.Groups[0]);
        Assert.NotNull(group.Template.FindName("Expander", group));                  // the template of Controls.xaml, not the default one
        Assert.True(group.IsExpanded);                                               // the binding to the node (the data folder group starts open)
        Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(group));
    });

    [Fact] public void A_folder_with_thousands_of_entries_creates_only_the_visible_containers() => Sta.RunAsync(async () =>
    {
        var entries = Enumerable.Range(0, 5000).Select(i => ($"big/file{i:D4}.txt", new byte[] { 1 })).ToArray();
        using var setup = await Create(g => g.AddPack("a.pack", entries), height: 300);
        var vm = setup.ViewModel;
        var pack = vm.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single();
        pack.IsExpanded = true;
        var folder = pack.Children.OfType<FolderNode>().Single();
        folder.IsExpanded = true;
        Pump(setup.Window);
        Assert.Equal(5000, folder.Children.Count);
        var realized = RealizedContainers(setup.Tree);
        Assert.InRange(realized, 5, 100);
    });

    // Recycled containers must not write their reset state back into the nodes: scrolling the pack out of view and back keeps it open.
    [Fact] public void Scrolling_far_down_and_back_up_keeps_an_expanded_pack_open() => Sta.RunAsync(async () =>
    {
        var entries = Enumerable.Range(0, 3000).Select(i => ($"file{i:D4}.txt", new byte[] { 1 })).ToArray();
        using var setup = await Create(g => g.AddPack("a.pack", entries), height: 300);
        var vm = setup.ViewModel;
        var pack = vm.Groups.SelectMany(g => g.Children).OfType<PackNode>().Single();
        pack.IsExpanded = true;
        Pump(setup.Window);
        var scroll = FindScrollViewer(setup.Tree)!;
        Assert.True(scroll.ScrollableHeight > 0);
        scroll.ScrollToEnd(); Pump(setup.Window);
        Assert.True(scroll.VerticalOffset > 0);
        scroll.ScrollToHome(); Pump(setup.Window);
        Assert.Equal(0, scroll.VerticalOffset);
        Assert.True(pack.IsExpanded);
        Assert.Equal([pack.Pack.Path], vm.ExpandedPackPaths);
        Assert.Equal(3000, pack.Children.Count);
        Assert.Equal(["Open a.pack"], setup.Game.Runner.Titles);                       // never read again
    });

    // ---- projects --------------------------------------------------------------------------------------------------------------

    // Like Create, with a projects controller and its commands; the game can be left unfound (a Steam library the controller does not find).
    private static async Task<Setup> CreateWithProjects(Action<TestGame, ProjectsController> projects, bool gameFound = true)
    {
        var game = new TestGame(inSteamLibrary: !gameFound);
        var controller = game.NewController();
        var p = game.NewProjects(controller);
        projects(game, p);
        var viewModel = new ExplorerViewModel(controller, game.Texts, (_, _) => Task.CompletedTask, () => { }, p,
            new ProjectsViewModel(p, new FakeProjectDialogs(), game.Texts), (_, _) => Task.CompletedTask);
        await controller.LoadAsync();
        var view = new ExplorerView(viewModel);
        view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(
            File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml"))));
        view.ApplyThemedItemStyle();
        var window = new Window { Width = 400, Height = 400, Content = view };
        window.Show();
        Pump(window);
        return new Setup(game, controller, viewModel, view, window, [], []);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    // The row panel of a node: the element of the template that carries the tool tip and the context menu.
    private static StackPanel RowOf(TreeViewItem container, ExplorerNode node) =>
        Descendants<StackPanel>(container).First(panel => ReferenceEquals(panel.DataContext, node));

    [Fact] public void The_message_is_shown_above_the_tree() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects((g, p) => g.NewProject(p, "Alpha"), gameFound: false);
        Assert.Equal((Visibility.Visible, Visibility.Visible, Visibility.Visible), (setup.Tree.Visibility, setup.MessageBlock.Visibility, setup.OpenSettingsButton.Visibility));
        Assert.Equal(setup.Game.Texts.ExplorerGameNotFound, setup.MessageBlock.Text);
        Assert.Single(setup.Tree.Items);
        var message = setup.MessageBlock.TranslatePoint(new Point(0, 0), setup.View);
        var tree = setup.Tree.TranslatePoint(new Point(0, 0), setup.View);
        Assert.True(message.Y < tree.Y, $"The message ({message.Y}) must be above the tree ({tree.Y}).");
        Assert.True(setup.Tree.ActualHeight > 50);                                       // the tree gets the rest of the space
    });

    [Fact] public void The_context_menu_lists_the_actions_and_marks_the_active_project() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects((g, p) => { g.NewProject(p, "Alpha"); g.NewProject(p, "Beta"); });
        var vm = setup.ViewModel;
        var group = (ProjectsGroupNode)vm.Groups[0];
        var groupContainer = ContainerOf(setup.Window, setup.Tree, group);
        var alpha = group.Children.OfType<ProjectNode>().Single(n => n.Name == "Alpha");
        var beta = group.Children.OfType<ProjectNode>().Single(n => n.Name == "Beta");
        var alphaRow = RowOf(ContainerOf(setup.Window, groupContainer, alpha), alpha);
        var betaRow = RowOf(ContainerOf(setup.Window, groupContainer, beta), beta);

        // The active project is bold, the other is not; the group has no menu at all.
        Assert.Equal(FontWeights.Bold, Descendants<TextBlock>(alphaRow).Single().FontWeight);
        Assert.NotEqual(FontWeights.Bold, Descendants<TextBlock>(betaRow).Single().FontWeight);
        Assert.Null(RowOf(groupContainer, group).ContextMenu);

        var menu = alphaRow.ContextMenu;
        Assert.NotNull(menu);
        menu.PlacementTarget = alphaRow;
        menu.IsOpen = true;
        try
        {
            Pump(setup.Window);
            var texts = setup.Game.Texts;
            var items = Enumerable.Range(0, menu.Items.Count).Select(i => Assert.IsType<MenuItem>(menu.ItemContainerGenerator.ContainerFromIndex(i))).ToList();
            Assert.Equal([texts.ProjectActionSetActive, texts.BuildMessageTitle, texts.ProjectActionBuildAndInstall, texts.UninstallMessageTitle, texts.ProjectActionRemove],
                items.Select(i => i.Header));
            for (var i = 0; i < items.Count; i++)
            {
                Assert.Same(alpha.Actions[i].Command, items[i].Command);
                Assert.Equal(alpha.Actions[i].DisabledReason, items[i].ToolTip);
                Assert.Equal(alpha.Actions[i].DisabledReason is null, items[i].IsEnabled);
                Assert.True(ToolTipService.GetShowOnDisabled(items[i]));
            }

            Assert.Equal(texts.UninstallNothing, items[3].ToolTip);                      // the disabled item says why
        }
        finally
        {
            menu.IsOpen = false;
        }
    });

    [Fact] public void A_pack_row_has_a_menu_and_its_group_has_none() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithProjects((g, p) => g.AddPack("a.pack"));
        var pack = setup.ViewModel.Groups.OfType<GroupNode>().Single().Children.OfType<PackNode>().Single();
        var groupContainer = ContainerOf(setup.Window, setup.Tree, setup.ViewModel.Groups[1]);
        var row = RowOf(ContainerOf(setup.Window, groupContainer, pack), pack);
        Assert.NotNull(row.ContextMenu);
        Assert.Null(RowOf(ContainerOf(setup.Window, setup.Tree, setup.ViewModel.Groups[1]), setup.ViewModel.Groups[1]).ContextMenu);
    });

    // ---- file operations -------------------------------------------------------------------------------------------------------

    private sealed record FilesSetup(Setup Setup, ProjectsController Projects, FakeFileDialogs Dialogs) : IDisposable
    {
        public void Dispose() => Setup.Dispose();
    }

    // Like CreateWithProjects, with the file operations wired as the app wires them (one undo history, the trash below the temp folder).
    private static async Task<FilesSetup> CreateWithFiles(Action<TestGame> arrange, Action<TestGame, ProjectsController>? projects = null)
    {
        var game = new TestGame(); arrange(game);
        game.EnsureTableVersions();
        var controller = game.NewController();
        var p = game.NewProjects(controller);
        projects?.Invoke(game, p);
        var history = new TotalWarModTool.UndoRedo.ActionHistory();
        var documents = new DocumentsController(new TabsViewModel(game.Texts), p, history, game.Runner, new FakeEditDialogs(), game.Log, game.Texts);
        var filesController = new ProjectFilesController(p, controller, documents, history, game.Runner, game.Root.File("trash"), game.Log, game.Texts, TimeProvider.System);
        var dialogs = new FakeFileDialogs();
        var files = new ProjectFilesViewModel(filesController, p, dialogs, game.Texts);
        var viewModel = new ExplorerViewModel(controller, game.Texts, (_, _) => Task.CompletedTask, () => { }, p,
            new ProjectsViewModel(p, new FakeProjectDialogs(), game.Texts), (_, _) => Task.CompletedTask, files);
        await controller.LoadAsync();
        var view = new ExplorerView(viewModel);
        view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(
            File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml"))));
        view.ApplyThemedItemStyle();
        var window = new Window { Width = 400, Height = 400, Content = view };
        window.Show();
        Pump(window);
        return new FilesSetup(new Setup(game, controller, viewModel, view, window, [], []), p, dialogs);
    }

    // Opens the context menu of a row the way the view does (the row is the placement target) and returns its realized items.
    private static List<MenuItem> OpenMenu(Window window, StackPanel row)
    {
        var menu = row.ContextMenu;
        Assert.NotNull(menu);
        menu.PlacementTarget = row;
        menu.IsOpen = true;
        Pump(window);
        return Enumerable.Range(0, menu.Items.Count).Select(i => Assert.IsType<MenuItem>(menu.ItemContainerGenerator.ContainerFromIndex(i))).ToList();
    }

    // The group that holds a pack; the test game has a vanilla pack of its own (EnsureTableVersions) in another group.
    private static GroupNode GroupOf(ExplorerViewModel vm, string pack) =>
        vm.Groups.OfType<GroupNode>().Single(group => group.Children.OfType<PackNode>().Any(node => node.Name == pack));

    [Fact] public void The_menu_of_an_entry_reads_its_actions_each_time_it_opens() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithFiles(g => g.AddPack("a.pack", ("readme.txt", "hello"u8.ToArray())));
        var vm = setup.Setup.ViewModel; var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts;
        var pack = GroupOf(vm, "a.pack").Children.OfType<PackNode>().Single(p => p.Name == "a.pack");
        pack.IsExpanded = true;
        var groupContainer = ContainerOf(window, setup.Setup.Tree, GroupOf(vm, "a.pack"));
        var packContainer = ContainerOf(window, groupContainer, pack);
        var entry = pack.Children.OfType<EntryNode>().Single();
        var row = RowOf(ContainerOf(window, packContainer, entry), entry);
        Assert.NotNull(row.ContextMenu);                                             // an entry has a menu ...
        Assert.Equal(0, vm.EntryActionBuilds);                                       // ... without a list having been built for its row

        // No project is active yet: the entry is there, disabled, and says why.
        var items = OpenMenu(window, row);
        try
        {
            var item = Assert.Single(items);
            Assert.Equal(texts.FileAddTitle, item.Header);
            Assert.Equal(texts.ProjectNoneActive, item.ToolTip);
            Assert.False(item.IsEnabled);
            Assert.True(ToolTipService.GetShowOnDisabled(item));
            Assert.Equal(1, vm.EntryActionBuilds);                                   // one list for the opening
        }
        finally
        {
            row.ContextMenu!.IsOpen = false;
        }

        Assert.Equal(1, vm.EntryActionBuilds);                                       // none for the closing

        // A project became the active one meanwhile: the same menu of the same row shows the state of now.
        setup.Setup.Game.NewProject(setup.Projects, "Alpha");
        items = OpenMenu(window, row);
        try
        {
            var item = Assert.Single(items);
            Assert.Equal(texts.FileAddTitle, item.Header);
            Assert.Null(item.ToolTip);
            Assert.True(item.IsEnabled);
            Assert.Equal(2, vm.EntryActionBuilds);
        }
        finally
        {
            row.ContextMenu!.IsOpen = false;
        }

        Assert.Equal(2, vm.EntryActionBuilds);
    });

    // Invokes an item of an open menu the way a click does (the automation peer calls OnClick, which runs the command), and lets the dispatcher work.
    private static void Click(Window window, MenuItem item)
    {
        ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke();
        Pump(window);
    }

    [Fact] public void A_click_on_an_item_of_an_entry_menu_runs_its_command_once() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithFiles(g => g.AddPack("a.pack", ("a.loc", new byte[] { 1 })), (g, p) => g.NewProject(p, "Alpha"));
        var vm = setup.Setup.ViewModel; var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts;
        var pack = GroupOf(vm, "a.pack").Children.OfType<PackNode>().Single(p => p.Name == "a.pack");
        pack.IsExpanded = true;
        var entry = pack.Children.OfType<EntryNode>().Single();
        var row = RowOf(ContainerOf(window, ContainerOf(window, ContainerOf(window, setup.Setup.Tree, GroupOf(vm, "a.pack")), pack), entry), entry);

        var items = OpenMenu(window, row);
        try
        {
            Assert.Equal(1, vm.EntryActionBuilds);
            Click(window, Assert.Single(items));
            Assert.Equal(1, vm.EntryActionBuilds);                                   // the click did not replace the list under itself
        }
        finally
        {
            row.ContextMenu!.IsOpen = false;
        }

        Assert.Equal(1, vm.EntryActionBuilds);
        Assert.Equal(texts.FileAddTitle, Assert.Single(setup.Dialogs.NameAsks).Title);   // the command asked for the name, once
    });

    [Fact] public void A_click_on_an_item_of_a_file_menu_acts_on_its_own_file_also_when_another_one_is_selected() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithFiles(_ => { }, (g, p) => g.NewProject(p, "Alpha", ("a.lua", "x"), ("b.lua", "y")));
        var vm = setup.Setup.ViewModel; var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts;
        var group = (ProjectsGroupNode)vm.Groups[0];
        var alpha = group.Children.OfType<ProjectNode>().Single();
        var alphaContainer = ContainerOf(window, ContainerOf(window, setup.Setup.Tree, group), alpha);
        alpha.IsExpanded = true;
        var a = alpha.Children.OfType<ProjectFileNode>().Single(file => file.Name == "a.lua");
        var b = alpha.Children.OfType<ProjectFileNode>().Single(file => file.Name == "b.lua");
        var aContainer = ContainerOf(window, alphaContainer, a);
        var bRow = RowOf(ContainerOf(window, alphaContainer, b), b);
        aContainer.IsSelected = true;                                                // the selected node is a.lua; the menu opens on b.lua

        var items = OpenMenu(window, bRow);
        try
        {
            Assert.Equal([texts.FileRenameTitle, texts.FileActionDelete], items.Select(i => i.Header));
            Assert.Equal(1, vm.FileActionBuilds);
            Click(window, items[0]);
            Assert.Equal(1, vm.FileActionBuilds);
        }
        finally
        {
            bRow.ContextMenu!.IsOpen = false;
        }

        var ask = Assert.Single(setup.Dialogs.NameAsks);                             // "Rename" asked for the name of b.lua, once
        Assert.Equal((texts.FileRenameTitle, "b"), (ask.Title, ask.Suggestion));

        items = OpenMenu(window, bRow);
        try
        {
            Click(window, items[1]);                                                 // "Delete" removes b.lua, not the selected a.lua
        }
        finally
        {
            bRow.ContextMenu!.IsOpen = false;
        }

        var pack = Path.Combine(setup.Projects.Projects[0].Folder, "pack");
        Assert.True(File.Exists(Path.Combine(pack, "a.lua")));
        Assert.False(File.Exists(Path.Combine(pack, "b.lua")));
    });

    [Fact] public void A_click_on_an_item_of_a_project_menu_runs_its_command_once() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithFiles(_ => { }, (g, p) => g.NewProject(p, "Alpha"));
        var vm = setup.Setup.ViewModel; var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts;
        var group = (ProjectsGroupNode)vm.Groups[0];
        var alpha = group.Children.OfType<ProjectNode>().Single();
        var row = RowOf(ContainerOf(window, ContainerOf(window, setup.Setup.Tree, group), alpha), alpha);

        var items = OpenMenu(window, row);
        try
        {
            Click(window, items[1]);                                                 // "New loc file"
        }
        finally
        {
            row.ContextMenu!.IsOpen = false;
        }

        Assert.Equal(texts.FileNewLocTitle, Assert.Single(setup.Dialogs.NameAsks).Title);
    });

    [Fact] public void The_menu_of_a_project_has_a_separator_and_the_menu_of_a_file_has_rename_and_delete() => Sta.RunAsync(async () =>
    {
        using var setup = await CreateWithFiles(_ => { }, (g, p) => g.NewProject(p, "Alpha", ("a.lua", "x")));
        var vm = setup.Setup.ViewModel; var window = setup.Setup.Window; var texts = setup.Setup.Game.Texts;
        var group = (ProjectsGroupNode)vm.Groups[0];
        var alpha = group.Children.OfType<ProjectNode>().Single();
        var groupContainer = ContainerOf(window, setup.Setup.Tree, group);
        var alphaContainer = ContainerOf(window, groupContainer, alpha);

        var alphaRow = RowOf(alphaContainer, alpha);
        var items = OpenMenu(window, alphaRow);
        try
        {
            Assert.Equal(8, items.Count);
            Assert.Equal([texts.FileNewTableTitle, texts.FileNewLocTitle], items.Take(2).Select(i => i.Header));
            Assert.Equal(texts.ProjectActionSetActive, items[3].Header);

            // The separator is a line, not an entry: nothing to read, nothing to click, no focus.
            var separator = items[2];
            Assert.Single(Descendants<Separator>(separator));
            Assert.DoesNotContain(Descendants<TextBlock>(separator), block => block.Text.Length > 0);
            Assert.Equal((false, false, false), (separator.IsEnabled, separator.IsHitTestVisible, separator.Focusable));
            Assert.All(items.Where(i => !ReferenceEquals(i, separator)), i => Assert.Empty(Descendants<Separator>(i)));
        }
        finally
        {
            alphaRow.ContextMenu!.IsOpen = false;
        }

        alpha.IsExpanded = true;
        var file = Assert.IsType<ProjectFileNode>(Assert.Single(alpha.Children));
        var fileRow = RowOf(ContainerOf(window, alphaContainer, file), file);
        items = OpenMenu(window, fileRow);
        try
        {
            Assert.Equal([texts.FileRenameTitle, texts.FileActionDelete], items.Select(i => i.Header));
            Assert.All(items, i => Assert.Equal((true, true), (i.IsEnabled, ToolTipService.GetShowOnDisabled(i))));
        }
        finally
        {
            fileRow.ContextMenu!.IsOpen = false;
        }
    });

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer || (viewer = FindScrollViewer(child)!) is not null)
            {
                return viewer;
            }
        }

        return null;
    }

    private static void RaiseEnter(TreeView tree) =>
        tree.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(tree)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });

    private static void RaiseDoubleClick(TreeViewItem container) =>
        container.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = container });
}
