using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace TotalWarModTool.App.Tests;

public class MainWindowTests
{
    private sealed record Setup(TestGame Game, WorkspaceController Controller, MainViewModel ViewModel, MainWindow Window, List<string> LogFolders) : IDisposable
    {
        public void Dispose() => Game.Dispose();
    }

    // A MainWindow with a MainViewModel of a loaded TestGame, shown. The test host has no Application, so the styles of Controls.xaml go into
    // the window's resources (the real app has them in its application resources).
    private static async Task<Setup> Create(Action<TestGame>? arrange = null)
    {
        var game = new TestGame(); arrange?.Invoke(game);
        var controller = game.NewController();
        var logFolders = new List<string>();
        var viewModel = new MainViewModel(game.Texts, game.Paths, logFolders.Add, game.Runner, () => { }, controller);
        await controller.LoadAsync();
        var window = new MainWindow(viewModel);
        window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(
            File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml"))));
        window.Show();
        Pump(window);
        return new Setup(game, controller, viewModel, window, logFolders);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
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

    [Fact] public void Window_shows_explorer_tabs_and_status_line() => Sta.RunAsync(async () =>
    {
        using var setup = await Create(g => g.AddPack("a.pack"));
        var window = setup.Window;
        Assert.Same(setup.ViewModel.Explorer, Assert.Single(Descendants<ExplorerView>(window)).DataContext);
        Assert.Same(setup.ViewModel.Tabs, Assert.Single(Descendants<TabsView>(window)).DataContext);

        var bar = Assert.Single(Descendants<StatusBar>(window));
        var first = Descendants<TextBlock>(bar).First();
        Assert.Equal(setup.ViewModel.StatusGame, first.Text);
        Assert.Equal("Game: Test Game", first.Text);
        Assert.Equal(setup.Game.InstallPath, first.ToolTip);
        Assert.Contains(Descendants<TextBlock>(bar), t => t.Text == setup.ViewModel.StatusSchema);

        // Explorer, splitter and tabs side by side in this order, the explorer 320 wide to start with.
        var grid = (Grid)window.FindName("Layout");
        Assert.Equal([320.0, 5.0], [grid.ColumnDefinitions[0].Width.Value, grid.ColumnDefinitions[1].Width.Value]);
        Assert.Equal(150.0, grid.ColumnDefinitions[0].MinWidth);
        Assert.Single(Descendants<GridSplitter>(window));
    });

    [Fact] public void Tools_menu_offers_refresh_settings_and_log_folder() => Sta.RunAsync(async () =>
    {
        using var setup = await Create();
        var tools = Descendants<MenuItem>(setup.Window).First();
        Assert.Equal(setup.Game.Texts.MenuTools, tools.Header);
        var items = tools.Items.OfType<MenuItem>().ToList();
        Assert.Equal([setup.Game.Texts.OperationRefreshPacks, setup.Game.Texts.MenuSettings, setup.Game.Texts.MenuOpenLogFolder], items.Select(i => i.Header));
        Assert.Same(setup.ViewModel.RefreshCommand, items[0].Command);
        Assert.Same(setup.ViewModel.OpenSettingsCommand, items[1].Command);
        Assert.Same(setup.ViewModel.OpenLogFolderCommand, items[2].Command);
    });

    [Fact] public void Status_hint_is_hidden_without_a_hint() => Sta.RunAsync(async () =>
    {
        using var setup = await Create();
        var hint = (FrameworkElement)setup.Window.FindName("StatusHintItem");
        Assert.Null(setup.ViewModel.StatusHint);
        Assert.Equal(Visibility.Collapsed, hint.Visibility);

        setup.Game.Settings.SchemaFolder = setup.Game.Root.File("no-schemas");         // the override fails: the shipped schema is used, with a hint
        Pump(setup.Window);
        Assert.NotNull(setup.ViewModel.StatusHint);
        Assert.Equal(Visibility.Visible, hint.Visibility);
        Assert.Contains(Descendants<TextBlock>(hint), t => t.Text == setup.ViewModel.StatusHint);
    });

    // Review item 3: Ctrl+W and F5 are bound on the window, so they work wherever the focus is.
    [Fact] public void Ctrl_W_and_F5_are_bound_on_the_window() => Sta.RunAsync(async () =>
    {
        using var setup = await Create();
        var bindings = setup.Window.InputBindings.OfType<KeyBinding>().ToList();
        Assert.Same(setup.ViewModel.Tabs.CloseSelectedCommand, Assert.Single(bindings, b => b.Key == Key.W && b.Modifiers == ModifierKeys.Control).Command);
        Assert.Same(setup.ViewModel.RefreshCommand, Assert.Single(bindings, b => b.Key == Key.F5 && b.Modifiers == ModifierKeys.None).Command);
    });
}
