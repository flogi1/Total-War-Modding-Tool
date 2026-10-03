using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Tests;

public class ControlTemplatesTests
{
    private sealed class FakeSystemTheme : ISystemThemeSource { public bool IsDark() => false; }

    private sealed record Row(string Name, int Size);

    // The test host cannot resolve the pack URI of the compiled dictionary (no Application), so the source file is parsed, as in ProgressBarStyleTests.
    private static string ControlsXamlPath() =>
        Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml");

    // One instance of every control type that Controls.xaml styles, named for the failure message.
    private static List<(string Name, FrameworkElement Control)> CreateControls()
    {
        var comboBox = new ComboBox { SelectedIndex = 0 };
        comboBox.Items.Add("one");

        var listView = new ListView { View = new GridView() };
        ((GridView)listView.View).Columns.Add(new GridViewColumn { Header = "Name", DisplayMemberBinding = new Binding(nameof(Row.Name)) });
        listView.ItemsSource = new[] { new Row("a", 1), new Row("b", 2) };

        var treeView = new TreeView();
        var treeItem = new TreeViewItem { Header = "parent", IsExpanded = true };
        treeItem.Items.Add(new TreeViewItem { Header = "child" });
        treeView.Items.Add(treeItem);

        var menu = new Menu();
        var menuItem = new MenuItem { Header = "File" };
        menuItem.Items.Add(new MenuItem { Header = "Open" });
        menu.Items.Add(menuItem);

        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(new MenuItem { Header = "x" });

        var tabControl = new TabControl();
        tabControl.Items.Add(new TabItem { Header = "tab", Content = "content" });

        var toolBar = new ToolBar();
        toolBar.Items.Add(new Button { Content = "tool" });

        var dataGrid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true };
        dataGrid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(Row.Name)) });
        dataGrid.Columns.Add(new DataGridTextColumn { Header = "Size", Binding = new Binding(nameof(Row.Size)) });
        dataGrid.ItemsSource = new[] { new Row("a", 1), new Row("b", 2) };

        var statusBar = new StatusBar();
        statusBar.Items.Add(new StatusBarItem { Content = "ready" });

        var listBox = new ListBox();
        listBox.Items.Add("item");

        return
        [
            ("Button", new Button { Content = "b" }),
            ("ToggleButton", new ToggleButton { Content = "t" }),
            ("CheckBox", new CheckBox { Content = "c" }),
            ("RadioButton", new RadioButton { Content = "r" }),
            ("TextBox", new TextBox { Text = "text" }),
            ("ScrollBar", new ScrollBar()),
            ("ComboBox", comboBox),
            ("ListBox", listBox),
            ("ListView", listView),
            ("TreeView", treeView),
            ("Menu", menu),
            ("ContextMenu", contextMenu),
            ("ToolBar", toolBar),
            ("TabControl", tabControl),
            ("Expander", new Expander { Header = "e", Content = "body" }),
            ("ProgressBar", new ProgressBar { Value = 5 }),
            ("ToolTip", new ToolTip { Content = "tip" }),
            ("Separator", new Separator()),
            ("DataGrid", dataGrid),
            ("StatusBar", statusBar),
            ("GridSplitter", new GridSplitter { Width = 5, Height = 20 }),
        ];
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in VisualDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static Color ColorOf(object? brush) => ((SolidColorBrush)brush!).Color;

    [Theory]
    [InlineData(ThemeChoice.Light)]
    [InlineData(ThemeChoice.Dark)]
    public void Every_styled_control_builds_its_template(ThemeChoice theme) => Sta.Run(() =>
    {
        var themeResources = new ResourceDictionary();
        new ThemeManager(themeResources, new FakeSystemTheme()).Apply(theme);
        var xaml = File.ReadAllText(ControlsXamlPath());
        var controlResources = (ResourceDictionary)XamlReader.Parse(xaml);

        // Every token the styles reference exists in the applied theme, so no brush stays unresolved.
        var used = Regex.Matches(xaml, @"Theme\.[A-Za-z]+").Select(m => m.Value).Distinct().ToList();
        Assert.NotEmpty(used);
        Assert.All(used, key => Assert.True(themeResources.Contains(key), $"Theme resource {key} is not defined."));

        var window = new Window { Width = 900, Height = 700, ShowActivated = false };
        window.Resources.MergedDictionaries.Add(themeResources);
        window.Resources.MergedDictionaries.Add(controlResources);
        var panel = new StackPanel();
        var controls = CreateControls();
        foreach (var (_, control) in controls)
        {
            // A ToolTip and a ContextMenu cannot be children of a panel (they live in a popup); they get the resources directly.
            if (control is ToolTip or ContextMenu)
            {
                control.Resources.MergedDictionaries.Add(themeResources);
                control.Resources.MergedDictionaries.Add(controlResources);
                continue;
            }

            panel.Children.Add(control);
        }

        window.Content = panel;
        window.Show();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();

        foreach (var (name, control) in controls)
        {
            if (control is Control templated)
            {
                templated.ApplyTemplate();
                Assert.True(VisualTreeHelper.GetChildrenCount(templated) > 0, $"{name} has no visual children after ApplyTemplate.");
            }

            control.Measure(new Size(400, 300));
            control.Arrange(new Rect(0, 0, 400, 300));
        }

        var panelBackground = ColorOf(themeResources["Theme.PanelBackground"]);
        var grid = (DataGrid)controls.Single(c => c.Name == "DataGrid").Control;
        Assert.Equal(panelBackground, ColorOf(grid.Background));
        Assert.Equal(2, VisualDescendants<DataGridRow>(grid).Count());
        Assert.Equal(4, VisualDescendants<DataGridCell>(grid).Count());
        Assert.NotEmpty(VisualDescendants<DataGridColumnHeader>(grid));
        Assert.Equal(DataGridHeadersVisibility.Column, grid.HeadersVisibility);

        var statusBar = (StatusBar)controls.Single(c => c.Name == "StatusBar").Control;
        Assert.NotEmpty(VisualDescendants<StatusBarItem>(statusBar));

        var splitter = (GridSplitter)controls.Single(c => c.Name == "GridSplitter").Control;
        Assert.NotNull(splitter.Template);
    });
}
