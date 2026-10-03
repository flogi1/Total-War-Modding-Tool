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
