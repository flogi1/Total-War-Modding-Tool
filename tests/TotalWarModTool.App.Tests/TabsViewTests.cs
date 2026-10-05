using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;
using System.Windows.Threading;
using TotalWarModTool.Localization;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class TabsViewTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly PackSource Pack = new(@"D:\data\a.pack", "a.pack", PackOrigin.DataFolderMod, null, null, null, PackProblem.None, null);

    private sealed record Setup(TabsViewModel ViewModel, TabsView View, Window Window)
    {
        public TabControl TabControl => Descendants<TabControl>(View).Single();

        public TableGrid Grid => Descendants<TableGrid>(View).Single();
    }

    // A TabsView with its view model inside a shown window. The test host has no Application, so the styles of Controls.xaml go into the view's resources.
    private static Setup Create()
    {
        var viewModel = new TabsViewModel(Texts);
        var view = new TabsView(viewModel);
        view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(ControlsXamlPath())));
        var window = new Window { Width = 600, Height = 400, Content = view };
        window.Show();
        Pump(window);
        return new Setup(viewModel, view, window);
    }

    private static string ControlsXamlPath() => System.IO.Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml");

    // Bindings and the generation of containers are delivered by the dispatcher; a window nobody pumps needs this nudge.
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

    private static OpenedEntry TableEntry(string[] columns, object?[][] rows)
    {
        var definition = new TableDefinition(0, columns.Select(c => new FieldDefinition(c, FieldType.StringU8, "StringU8", false, null)).ToList());
        var table = new DecodedTable(TableDecodeStatus.Ok, "units_tables", 0, false, definition, rows, null);
        return new OpenedEntry("db/units_tables/data__", EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, table, null, null);
    }

    private static OpenedEntry TextEntry(string path) =>
        new(path, EntryKind.Text, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, null, new TextContent("x", "UTF-8", false, 1));

    private static void RaiseMouseDown(UIElement target, MouseButton button) =>
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button) { RoutedEvent = UIElement.MouseDownEvent, Source = target });

    private static IEnumerable<string> HeaderNames(DataGrid grid) => grid.Columns.Select(c => Assert.IsType<TextBlock>(c.Header).Text);

    [Fact] public void Table_grid_gets_one_column_per_field_and_keeps_underscores() => Sta.Run(() =>
    {
        var setup = Create();
        setup.ViewModel.Add(Pack, TableEntry(["unit_key", "cost"], [["a", "1"], ["b", "2"]]), null);
        Pump(setup.Window);

        Assert.Equal(2, setup.Grid.Columns.Count);
        Assert.Equal(["unit_key", "cost"], HeaderNames(setup.Grid));
        var row = Assert.IsType<DataGridRow>(setup.Grid.ItemContainerGenerator.ContainerFromIndex(0));
        var firstCell = Descendants<DataGridCell>(row).First();
        Assert.Equal("a", Assert.IsType<TextBlock>(firstCell.Content).Text);
    });

    // The columns read "[i]" of an array row; the default sorting of the grid has to cope with that path.
    [Fact] public void Table_grid_sorts_by_the_cell_of_a_column() => Sta.Run(() =>
    {
        var setup = Create();
        setup.ViewModel.Add(Pack, TableEntry(["unit_key", "cost"], [["a", "1"], ["c", "3"], ["b", "2"]]), null);
        Pump(setup.Window);

        setup.Grid.Items.SortDescriptions.Add(new SortDescription("[0]", ListSortDirection.Descending));
        Pump(setup.Window);
        Assert.Equal(["c", "b", "a"], setup.Grid.Items.Cast<object?[]>().Select(r => (string)r[0]!));
    });

    [Fact] public void Switching_to_another_table_tab_rebuilds_the_columns() => Sta.Run(() =>
    {
        var setup = Create();
        setup.ViewModel.Add(Pack, TableEntry(["unit_key", "cost"], [["a", "1"]]), null);
        setup.ViewModel.Add(Pack, TableEntry(["x", "y", "z"], [["p", "q", "r"]]), null);
        Pump(setup.Window);
        Assert.Equal(["x", "y", "z"], HeaderNames(setup.Grid));

        setup.ViewModel.SelectedTab = setup.ViewModel.Tabs[0];
        Pump(setup.Window);
        Assert.Equal(["unit_key", "cost"], HeaderNames(setup.Grid));
    });

    [Fact] public void The_table_template_switches_between_grid_and_text() => Sta.Run(() =>
    {
        var setup = Create();
        setup.ViewModel.Add(Pack, TableEntry(["unit_key", "cost"], [["a", "1"]]), null);
        Pump(setup.Window);
        var tab = Assert.IsType<TableTabViewModel>(setup.ViewModel.SelectedTab);
        var box = Descendants<TextBox>(setup.View).Single();
        var buttons = Descendants<RadioButton>(setup.View).ToList();
        Assert.Equal((Visibility.Visible, Visibility.Collapsed), (setup.Grid.Visibility, box.Visibility));
        Assert.Equal([true, false], buttons.Select(b => b.IsChecked == true));

        tab.ShowText = true;
        Pump(setup.Window);

        Assert.Equal((Visibility.Collapsed, Visibility.Visible), (setup.Grid.Visibility, box.Visibility));
        Assert.Equal(tab.Text, box.Text);
        Assert.Equal(("unit_key\tcost\n#units_tables;0;db/units_tables/data__\t\na\t1\n", true, "Consolas"), (box.Text, box.IsReadOnly, box.FontFamily.Source));
        Assert.Equal([false, true], buttons.Select(b => b.IsChecked == true));

        // Selecting the grid switch (what a click does) brings the grid back.
        ((ISelectionItemProvider)new RadioButtonAutomationPeer(buttons[0]).GetPattern(PatternInterface.SelectionItem)!).Select();
        Pump(setup.Window);
        Assert.False(tab.ShowText);
        Assert.Equal((Visibility.Visible, Visibility.Collapsed), (setup.Grid.Visibility, box.Visibility));
        Assert.Equal([true, false], buttons.Select(b => b.IsChecked == true));
    });

    [Fact] public void The_text_notice_of_a_table_is_visible_only_when_there_is_one() => Sta.Run(() =>
    {
        var setup = Create();
        setup.ViewModel.Add(Pack, TableEntry(["unit_key"], [["a"]]), null);
        var tab = Assert.IsType<TableTabViewModel>(setup.ViewModel.SelectedTab);
        tab.MaxChars = 10;
        tab.ShowText = true;
        Pump(setup.Window);

        var notice = Descendants<TextBlock>(setup.View).Single(t => t.Text == Texts.TabTextViewTruncated);
        Assert.Equal(Visibility.Visible, notice.Visibility);

        // It describes the text view: collapsed again while the grid is shown, although the notice is still set.
        tab.ShowText = false;
        Pump(setup.Window);
        Assert.NotNull(tab.TextNotice);
        Assert.Equal(Visibility.Collapsed, notice.Visibility);

        // In the text view of a text that fits there is no notice and nothing is shown for it.
        setup.ViewModel.Add(Pack, TableEntry(["unit_key"], [["a"]]), null);
        var fitting = Assert.IsType<TableTabViewModel>(setup.ViewModel.SelectedTab);
        fitting.ShowText = true;
        Pump(setup.Window);
        Assert.Null(fitting.TextNotice);
        Assert.Equal(Visibility.Collapsed, NoticeBlock(setup.View).Visibility);
    });

    // The notice line of a table or loc tab is the only wrapping text block with this margin.
    private static TextBlock NoticeBlock(DependencyObject root) =>
        Descendants<TextBlock>(root).Single(t => t.Margin == new Thickness(6, 0, 6, 4) && t.TextWrapping == TextWrapping.Wrap);

    [Fact] public void The_loc_template_switches_between_grid_and_text() => Sta.Run(() =>
    {
        var setup = Create();
        var loc = new DecodedLoc(LocDecodeStatus.Ok, [new("k1", "t1", true)], null);
        setup.ViewModel.Add(Pack, new OpenedEntry("text/names.loc", EntryKind.Loc, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, loc, null), null);
        Pump(setup.Window);
        var tab = Assert.IsType<LocTabViewModel>(setup.ViewModel.SelectedTab);
        var box = Descendants<TextBox>(setup.View).Single();
        Assert.Equal((Visibility.Visible, Visibility.Collapsed), (setup.Grid.Visibility, box.Visibility));

        tab.ShowText = true;
        Pump(setup.Window);

        Assert.Equal((Visibility.Collapsed, Visibility.Visible), (setup.Grid.Visibility, box.Visibility));
        Assert.StartsWith("key\ttext\ttooltip\n#Loc;1;", box.Text);
    });

    [Fact] public void Middle_click_on_a_tab_header_closes_the_tab() => Sta.Run(() =>
    {
        var setup = Create();
        setup.ViewModel.Add(Pack, TextEntry("a.txt"), null);
        setup.ViewModel.Add(Pack, TextEntry("b.txt"), null);
        Pump(setup.Window);
        var item = Assert.IsType<TabItem>(setup.TabControl.ItemContainerGenerator.ContainerFromIndex(0));
        var header = Descendants<TextBlock>(item).First(t => t.Text == "a.txt");

        RaiseMouseDown(header, MouseButton.Left);
        Assert.Equal(2, setup.ViewModel.Tabs.Count);

        RaiseMouseDown(header, MouseButton.Middle);
        Assert.Equal(["b.txt"], setup.ViewModel.Tabs.Select(t => t.Title));
    });

    [Fact] public void Info_tab_shows_heading_reason_and_lines() => Sta.Run(() =>
    {
        var setup = Create();
        var entry = new OpenedEntry("movies/Intro.CA_VP8", EntryKind.Other, 2048, 4096, CompressionFormat.Lz4, EntryProblem.None, null, false, null, null, null);
        setup.ViewModel.Add(Pack, entry, null);
        Pump(setup.Window);
        var tab = Assert.IsType<InfoTabViewModel>(setup.ViewModel.SelectedTab);

        Assert.Contains(Descendants<TextBlock>(setup.View), t => t.Text == tab.Heading);
        var list = Descendants<ItemsControl>(setup.View).Single(i => ReferenceEquals(i.ItemsSource, tab.Lines));
        Assert.Equal(4, list.Items.Count);
        Assert.Equal(tab.Lines.Select(l => l.Label), Descendants<TextBlock>(list).Select(t => t.Text));
        Assert.Equal(tab.Lines.Select(l => l.Value), Descendants<TextBox>(list).Select(t => t.Text));
        Assert.All(Descendants<TextBox>(list), t => Assert.True(t.IsReadOnly));
    });

    [Fact] public void Close_selected_is_bound_to_Ctrl_W() => Sta.Run(() =>
    {
        var setup = Create();
        var binding = Assert.Single(setup.View.InputBindings.OfType<KeyBinding>());
        Assert.Equal((Key.W, ModifierKeys.Control), (binding.Key, binding.Modifiers));
        Assert.Same(setup.ViewModel.CloseSelectedCommand, binding.Command);
    });

    [Fact] public void Stale_tab_shows_its_mark_with_the_tooltip() => Sta.Run(() =>
    {
        var setup = Create();
        setup.ViewModel.Add(Pack, TextEntry("a.txt"), null);
        Pump(setup.Window);
        var item = Assert.IsType<TabItem>(setup.TabControl.ItemContainerGenerator.ContainerFromIndex(0));
        var mark = Descendants<Ellipse>(item).Single(ellipse => ellipse.Name == "StaleMark");
        Assert.Equal(Visibility.Collapsed, mark.Visibility);

        setup.ViewModel.MarkStale(Pack.Path);
        Pump(setup.Window);
        Assert.Equal((Visibility.Visible, Texts.TabStale), (mark.Visibility, mark.ToolTip));
    });

    [Fact] public void A_modified_tab_shows_its_own_mark_apart_from_the_stale_mark() => Sta.Run(() =>
    {
        var setup = Create();
        var document = TotalWarModTool.Editing.RowsDocument.ForLoc("text/db/a.loc", [new LocRow("k", "Text", false)]);
        var tab = new EditableRowsTabViewModel(TabSource.ForProject(Guid.NewGuid(), "Project"), "text/db/a.loc.tsv", Texts, setup.ViewModel.Close, document,
            new TotalWarModTool.UndoRedo.ActionHistory());
        setup.ViewModel.Tabs.Add(tab);
        setup.ViewModel.Add(Pack, TextEntry("b.txt"), null);                      // a tab type that has no such property of its own
        Pump(setup.Window);
        var item = Assert.IsType<TabItem>(setup.TabControl.ItemContainerGenerator.ContainerFromIndex(0));
        var other = Assert.IsType<TabItem>(setup.TabControl.ItemContainerGenerator.ContainerFromIndex(1));
        var modified = Descendants<Ellipse>(item).Single(ellipse => ellipse.Name == "ModifiedMark");
        var stale = Descendants<Ellipse>(item).Single(ellipse => ellipse.Name == "StaleMark");
        var otherModified = Descendants<Ellipse>(other).Single(ellipse => ellipse.Name == "ModifiedMark");
        Assert.Equal((Visibility.Collapsed, Visibility.Collapsed, Visibility.Collapsed), (modified.Visibility, stale.Visibility, otherModified.Visibility));

        tab.Rows[0][0] = "other";
        Pump(setup.Window);
        Assert.Equal((Visibility.Visible, Texts.TabModified), (modified.Visibility, modified.ToolTip));
        Assert.Equal(Visibility.Collapsed, stale.Visibility);
        Assert.Equal(Visibility.Collapsed, otherModified.Visibility);

        setup.ViewModel.MarkStale(tab.PackPath);                                   // both marks can show at the same time
        Pump(setup.Window);
        Assert.Equal((Visibility.Visible, Visibility.Visible), (modified.Visibility, stale.Visibility));

        tab.Document.MarkSaved();
        Pump(setup.Window);
        Assert.Equal((Visibility.Collapsed, Visibility.Visible), (modified.Visibility, stale.Visibility));
    });

    [Fact] public void Text_tab_is_a_read_only_monospace_box_with_a_notice_only_when_truncated() => Sta.Run(() =>
    {
        var setup = Create();
        var text = new TextContent("hello", "UTF-8", true, 8 * 1024 * 1024);
        setup.ViewModel.Add(Pack, new OpenedEntry("a.txt", EntryKind.Text, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, null, text), null);
        Pump(setup.Window);

        var box = Descendants<TextBox>(setup.View).Single();
        Assert.Equal(("hello", true, "Consolas"), (box.Text, box.IsReadOnly, box.FontFamily.Source));
        Assert.Equal((TextWrapping.NoWrap, ScrollBarVisibility.Auto, ScrollBarVisibility.Auto),
            (box.TextWrapping, box.HorizontalScrollBarVisibility, box.VerticalScrollBarVisibility));
        var notice = Descendants<TextBlock>(setup.View).Single(t => t.Text.StartsWith("Only the first"));
        Assert.Equal(Visibility.Visible, notice.Visibility);

        setup.ViewModel.Add(Pack, TextEntry("b.txt"), null);
        Pump(setup.Window);
        Assert.DoesNotContain(Descendants<TextBlock>(setup.View).Where(t => t.Text.StartsWith("Only the first")), t => t.Visibility == Visibility.Visible);
    });

    // The themed TextBox template once hard-coded hidden scroll bars, so the bars of the text tab never showed.
    [Fact] public void Text_tab_shows_both_scroll_bars_when_the_text_is_longer_and_wider_than_the_view() => Sta.Run(() =>
    {
        var setup = Create();
        var longText = string.Join(NewLine, Enumerable.Repeat(new string('x', 600), 200));
        var text = new TextContent(longText, "UTF-8", false, longText.Length);
        setup.ViewModel.Add(Pack, new OpenedEntry("a.txt", EntryKind.Text, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, null, text), null);
        Pump(setup.Window);

        var host = ContentHostOf(Descendants<TextBox>(setup.View).Single());
        Assert.Equal((Visibility.Visible, Visibility.Visible), (host.ComputedVerticalScrollBarVisibility, host.ComputedHorizontalScrollBarVisibility));
    });

    [Fact] public void A_plain_text_box_keeps_hidden_scroll_bars() => Sta.Run(() =>
    {
        var box = new TextBox { Text = string.Join(NewLine, Enumerable.Repeat(new string('x', 600), 200)), AcceptsReturn = true };
        var window = new Window { Width = 300, Height = 200, Content = box };
        window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(ControlsXamlPath())));
        window.Show();
        Pump(window);

        var host = ContentHostOf(box);
        Assert.Equal((Visibility.Collapsed, Visibility.Collapsed), (host.ComputedVerticalScrollBarVisibility, host.ComputedHorizontalScrollBarVisibility));
    });

    private static readonly string NewLine = Environment.NewLine;

    private static ScrollViewer ContentHostOf(TextBox box) => Assert.IsType<ScrollViewer>(box.Template.FindName("PART_ContentHost", box));

    [Fact] public void Loc_tab_shows_three_columns_with_texts_and_rows() => Sta.Run(() =>
    {
        var setup = Create();
        var loc = new DecodedLoc(LocDecodeStatus.Ok, [new("k1", "t1", true), new("k2", "t2", false)], null);
        setup.ViewModel.Add(Pack, new OpenedEntry("text/names.loc", EntryKind.Loc, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, loc, null), null);
        Pump(setup.Window);

        var grid = setup.Grid;
        Assert.Equal(["Key", "Text", "Tooltip"], Descendants<DataGridColumnHeader>(grid).Select(h => h.Content).OfType<TextBlock>().Select(t => t.Text));
        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Equal("k1", Assert.IsType<TextBlock>(Descendants<DataGridCell>(row).First().Content).Text);
    });

    // The themed header style once drew the arrow the wrong way round: ascending must point up, descending down (the convention of WPF and the Explorer).
    [Theory]
    [InlineData(ListSortDirection.Ascending, true)]
    [InlineData(ListSortDirection.Descending, false)]
    public void Sort_arrow_points_up_for_ascending_and_down_for_descending(ListSortDirection direction, bool pointsUp) => Sta.Run(() =>
    {
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, ItemsSource = new[] { new LocRow("a", "1", false), new LocRow("b", "2", false) } };
        grid.Columns.Add(new DataGridTextColumn { Header = "Key", Binding = new System.Windows.Data.Binding(nameof(LocRow.Key)) });
        var window = new Window { Width = 300, Height = 200, Content = grid };
        window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(ControlsXamlPath())));
        window.Show();
        Pump(window);

        grid.Columns[0].SortDirection = direction;
        Pump(window);
        var header = Descendants<DataGridColumnHeader>(grid).First(h => h.Column is not null);
        Assert.Equal(direction, header.SortDirection);
        var arrow = Assert.IsType<ShapePath>(header.Template.FindName("SortArrow", header));
        Assert.Equal(Visibility.Visible, arrow.Visibility);

        // The drawn shape is a triangle: two corners on the base line, one tip. The render transform decides where the tip ends up.
        var figure = Assert.Single(PathGeometry.CreateFromGeometry(arrow.Data).Figures);
        var corners = new[] { figure.StartPoint }
            .Concat(figure.Segments.OfType<LineSegment>().Select(s => s.Point))
            .Concat(figure.Segments.OfType<PolyLineSegment>().SelectMany(s => s.Points))
            .Distinct().ToList();
        Assert.Equal(3, corners.Count);
        var tip = corners.Single(c => corners.Count(o => o.Y == c.Y) == 1);
        var baseLine = corners.Single(c => c.Y != tip.Y && c.X == corners.Where(o => o.Y != tip.Y).Min(o => o.X));
        var transform = arrow.RenderTransform ?? Transform.Identity;
        Assert.Equal(pointsUp, transform.Transform(tip).Y < transform.Transform(baseLine).Y);
    });
}
