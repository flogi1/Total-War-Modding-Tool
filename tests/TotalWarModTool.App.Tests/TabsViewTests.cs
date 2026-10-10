using TotalWarModTool.Objects;
using TotalWarModTool.Editing;
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
    private static Setup Create(TabsViewModel? tabs = null)
    {
        var viewModel = tabs ?? new TabsViewModel(Texts);
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

    private static OpenedEntry TableEntry(string[] columns, object?[][] rows, bool firstIsKey = false)
    {
        var definition = new TableDefinition(0, columns.Select((c, i) => new FieldDefinition(c, FieldType.StringU8, "StringU8", firstIsKey && i == 0, null)).ToList());
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

    [Fact] public void A_stale_editable_tab_shows_the_reload_button() => Sta.Run(() =>
    {
        var setup = Create();
        var document = TotalWarModTool.Editing.RowsDocument.ForLoc("text/db/a.loc", [new LocRow("k", "Text", false)]);
        var tab = new EditableRowsTabViewModel(TabSource.ForProject(Guid.NewGuid(), "Project"), "text/db/a.loc.tsv", Texts, setup.ViewModel.Close, document,
            new TotalWarModTool.UndoRedo.ActionHistory());
        var reloads = 0;
        tab.ReloadHandler = _ => reloads++;
        setup.ViewModel.Tabs.Add(tab);
        setup.ViewModel.SelectedTab = tab;
        Pump(setup.Window);
        Button? ReloadButton() => Descendants<Button>(setup.View).SingleOrDefault(button => button.Name == "ReloadButton");
        TextBlock? Line() => Descendants<TextBlock>(setup.View).FirstOrDefault(text => text.Text == Texts.TabStaleChanged);

        // Nothing happened outside: no line, no button.
        Assert.True(ReloadButton() is null || !ReloadButton()!.IsVisible);
        Assert.Null(Line());

        // The file was changed outside: the line says so and the button reads the file again.
        tab.SetOutsideChange(OutsideChange.Changed);
        Pump(setup.Window);
        Assert.NotNull(Line());
        var button = ReloadButton();
        Assert.NotNull(button);
        Assert.True(button.IsVisible);
        Assert.Equal(Texts.TabReload, button.Content);
        Assert.True(button.IsEnabled);
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
        Pump(setup.Window);                                                        // the automation peer posts the click
        Assert.Equal(1, reloads);

        // Own changes: the line stays, the button goes (a reload would throw them away).
        tab.Rows[0][0] = "other";
        Pump(setup.Window);
        Assert.NotNull(Line());
        Assert.False(ReloadButton()!.IsVisible);

        tab.Document.MarkSaved();
        Pump(setup.Window);
        Assert.True(ReloadButton()!.IsVisible);

        // The mark is gone (a reload or a save): the line is gone too.
        tab.SetOutsideChange(OutsideChange.None);
        Pump(setup.Window);
        Assert.False(ReloadButton()!.IsVisible);
        Assert.False(Line()?.IsVisible ?? false);
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

    // ---- the editable tab and the copy of the read-only tabs ------------------------------------------------------------------------

    private static EditableRowsTabViewModel EditableLoc(Setup setup, FakeClipboard clipboard)
    {
        var document = TotalWarModTool.Editing.RowsDocument.ForLoc("text/db/a.loc", [new LocRow("k", "Text", false), new LocRow("k2", "More", true)]);
        var tab = new EditableRowsTabViewModel(TabSource.ForProject(Guid.NewGuid(), "Project"), "text/db/a.loc.tsv", Texts, setup.ViewModel.Close, document,
            new TotalWarModTool.UndoRedo.ActionHistory(), clipboard);
        setup.ViewModel.Tabs.Add(tab);
        setup.ViewModel.SelectedTab = tab;
        Pump(setup.Window);
        return tab;
    }

    // The menu is not in the visual tree: it is opened (with its popup) to let its bindings resolve, and closed again by the caller in a finally.
    private static List<MenuItem> OpenedItems(ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target;
        menu.IsOpen = true;
        target.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        return menu.Items.OfType<MenuItem>().ToList();
    }

    [Fact] public void An_editable_tab_shows_the_editable_grid_the_message_lines_and_the_text_switch() => Sta.Run(() =>
    {
        var setup = Create();
        try
        {
            var clipboard = new FakeClipboard();
            var tab = EditableLoc(setup, clipboard);
            var grid = setup.Grid;
            TextBlock Line(string name) => Descendants<TextBlock>(setup.View).Single(text => text.Name == name);

            Assert.False(grid.IsReadOnly);
            Assert.Equal(["key", "text", "tooltip"], grid.Columns.Select(c => Assert.IsType<TextBlock>(c.Header).Text));
            Assert.IsAssignableFrom<DataGridCheckBoxColumn>(grid.Columns[2]);
            Assert.Equal(Visibility.Collapsed, Line("ValidationLine").Visibility);
            Assert.Equal(Visibility.Collapsed, Line("NoticeLine").Visibility);

            // The message of a cell with an invalid value and the notice of a paste show under the header, in the colour of a warning.
            tab.SetValidationMessage("not a valid value");
            tab.PasteRowsCommand.Execute(null);                                    // no text on the clipboard: a notice
            Pump(setup.Window);
            Assert.Equal((Visibility.Visible, "not a valid value"), (Line("ValidationLine").Visibility, Line("ValidationLine").Text));
            Assert.Equal((Visibility.Visible, Texts.GridPasteNoText), (Line("NoticeLine").Visibility, Line("NoticeLine").Text));
            tab.SetValidationMessage(null);
            Pump(setup.Window);
            Assert.Equal(Visibility.Collapsed, Line("ValidationLine").Visibility);

            // The switch to the text view shows the text of the document instead of the grid.
            var box = Descendants<TextBox>(setup.View).Single();
            Assert.Equal((Visibility.Visible, Visibility.Collapsed), (grid.Visibility, box.Visibility));
            tab.ShowText = true;
            Pump(setup.Window);
            Assert.Equal((Visibility.Collapsed, Visibility.Visible), (grid.Visibility, box.Visibility));
            Assert.StartsWith("key\ttext\ttooltip\n#Loc;1;", box.Text);
        }
        finally
        {
            setup.Window.Close();
        }
    });

    [Fact] public void The_context_menu_of_an_editable_grid_holds_the_row_commands_with_their_texts() => Sta.Run(() =>
    {
        var setup = Create();
        ContextMenu? menu = null;
        try
        {
            var tab = EditableLoc(setup, new FakeClipboard());
            menu = Assert.IsType<ContextMenu>(setup.Grid.ContextMenu);

            var items = OpenedItems(menu, setup.Grid);

            Assert.Equal([Texts.GridInsertRow, Texts.GridDuplicateRows, Texts.GridDeleteRows, Texts.GridCopy, Texts.GridPaste, Texts.GridShowObject],
                items.Select(item => item.Header));
            Assert.Equal([tab.InsertRowCommand, tab.DuplicateRowsCommand, tab.DeleteRowsCommand, tab.CopyRowsCommand, tab.PasteRowsCommand, tab.ShowObjectCommand],
                items.Select(item => item.Command));
            Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace((string)item.Header)));
            Assert.Equal([true, false, false, false, true, false], items.Select(item => item.IsEnabled));  // nothing selected; insert and paste need none
        }
        finally
        {
            if (menu is not null)
            {
                menu.IsOpen = false;
            }

            setup.Window.Close();
        }
    });

    [Fact] public void The_context_menu_of_a_read_only_table_and_loc_grid_copies() => Sta.Run(() =>
    {
        var setup = Create();
        ContextMenu? menu = null;
        try
        {
            setup.ViewModel.Clipboard = new FakeClipboard();
            setup.ViewModel.Add(Pack, TableEntry(["unit_key", "cost"], [["a", "1"], ["b", "2"]]), null);
            Pump(setup.Window);
            var table = Assert.IsType<TableTabViewModel>(setup.ViewModel.SelectedTab);
            menu = Assert.IsType<ContextMenu>(setup.Grid.ContextMenu);

            var items = OpenedItems(menu, setup.Grid);
            Assert.Equal([Texts.GridCopy, Texts.GridShowObject], items.Select(entry => entry.Header));
            var item = items[0];

            Assert.Equal(Texts.GridCopy, item.Header);
            Assert.Same(table.CopyRowsCommand, item.Command);
            Assert.False(item.IsEnabled);                                           // nothing is selected
            menu.IsOpen = false;

            var loc = new DecodedLoc(LocDecodeStatus.Ok, [new("k1", "t1", true)], null);
            setup.ViewModel.Add(Pack, new OpenedEntry("text/names.loc", EntryKind.Loc, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, loc, null), null);
            Pump(setup.Window);
            var locTab = Assert.IsType<LocTabViewModel>(setup.ViewModel.SelectedTab);
            menu = Assert.IsType<ContextMenu>(setup.Grid.ContextMenu);
            var locItem = Assert.Single(OpenedItems(menu, setup.Grid));
            Assert.Equal(Texts.GridCopy, locItem.Header);
            Assert.Same(locTab.CopyRowsCommand, locItem.Command);
        }
        finally
        {
            if (menu is not null)
            {
                menu.IsOpen = false;
            }

            setup.Window.Close();
        }
    });

    [Fact] public void The_object_tab_template_shows_header_buttons_and_the_block() => Sta.Run(() =>
    {
        var setup = Create();
        ContextMenu? menu = null;
        try
        {
            var tab = new ObjectTabViewModel(Texts, setup.ViewModel.Close, [new ObjectRef("units_tables", ["a"], null)], 0, key => key);
            setup.ViewModel.Tabs.Add(tab);
            setup.ViewModel.SelectedTab = tab;
            Pump(setup.Window);

            string[] tips = [Texts.ObjectsBack, Texts.ObjectsForward, Texts.ObjectsRefresh, Texts.ObjectsJumpToRow];
            var buttons = Descendants<Button>(setup.View).Where(button => button.ToolTip is string tip && tips.Contains(tip)).ToList();
            Assert.Equal(tips, buttons.Select(button => (string)button.ToolTip));
            Assert.Equal([tab.BackCommand, tab.ForwardCommand, tab.RefreshCommand, tab.JumpToRowCommand], buttons.Select(button => button.Command));
            Assert.Same(tab, Descendants<ObjectEditorView>(setup.View).Single().DataContext);

            // The notice of the idle tab shows under the header; Alt+Left and Alt+Right move through the history.
            Assert.Equal(Visibility.Visible, Descendants<TextBlock>(setup.View).Single(text => text.Text == Texts.ObjectsStateIdle).Visibility);
            var keys = Descendants<FrameworkElement>(setup.View).SelectMany(element => element.InputBindings.OfType<KeyBinding>())
                .Where(binding => binding.Modifiers == ModifierKeys.Alt).ToList();
            Assert.Equal([(Key.Left, tab.BackCommand), (Key.Right, tab.ForwardCommand)], keys.Select(binding => (binding.Key, binding.Command)));

            // The read-only table offers the object of its row in its context menu.
            setup.ViewModel.Add(Pack, TableEntry(["unit_key"], [["a"]]), null);
            Pump(setup.Window);
            var table = Assert.IsType<TableTabViewModel>(setup.ViewModel.SelectedTab);
            menu = Assert.IsType<ContextMenu>(setup.Grid.ContextMenu);
            var item = OpenedItems(menu, setup.Grid).Last();
            Assert.Equal("Show as object", item.Header);                           // Texts.GridShowObject without a language file
            Assert.Same(table.ShowObjectCommand, item.Command);
            Assert.Equal(table.ShowObjectReason, item.ToolTip);
            Assert.True(ToolTipService.GetShowOnDisabled(item));
        }
        finally
        {
            if (menu is not null)
            {
                menu.IsOpen = false;
            }

            setup.Window.Close();
        }
    });

    [Fact] public void Selecting_rows_in_a_sorted_read_only_grid_reports_the_rows_of_the_table() => Sta.Run(() =>
    {
        var setup = Create();
        try
        {
            var clipboard = new FakeClipboard();
            setup.ViewModel.Clipboard = clipboard;
            setup.ViewModel.Add(Pack, TableEntry(["unit_key", "cost"], [["a", "1"], ["b", "2"], ["c", "3"]]), null);
            Pump(setup.Window);
            var table = Assert.IsType<TableTabViewModel>(setup.ViewModel.SelectedTab);

            // Sorted descending: the grid reports the rows of the table, not the positions in the view.
            setup.Grid.Items.SortDescriptions.Add(new SortDescription("[0]", ListSortDirection.Descending));
            Pump(setup.Window);
            setup.Grid.SelectedItems.Add(setup.Grid.Items[0]);                      // the row "c": index 2 in the table

            Assert.Equal([2], table.SelectedRows);
            Assert.True(table.CopyRowsCommand.CanExecute(null));
            Assert.True(setup.Grid.HandleKey(Key.C, ModifierKeys.Control, false));
            Assert.Equal("unit_key\tcost\nc\t3\n", clipboard.Text);
        }
        finally
        {
            setup.Window.Close();
        }
    });

    // The jump to a row: the table tab is selected and asked for the row before the tab control made its grid (another template was shown).
    [Fact] public void A_row_selected_by_key_while_the_tab_is_not_shown_is_selected_when_its_grid_comes() => Sta.Run(() =>
    {
        var setup = Create();
        try
        {
            setup.ViewModel.Add(Pack, TableEntry(["unit_key", "cost"], [["a", "1"], ["b", "2"], ["c", "3"]], firstIsKey: true), null);
            var table = Assert.IsType<TableTabViewModel>(setup.ViewModel.SelectedTab);
            setup.ViewModel.Add(Pack, TextEntry("a.txt"), null);
            Pump(setup.Window);
            Assert.Empty(Descendants<TableGrid>(setup.View));

            setup.ViewModel.SelectedTab = table;
            Assert.True(table.SelectRowByKey(["b"]));
            Pump(setup.Window);

            Assert.Same(table.Rows[1], Assert.Single(setup.Grid.SelectedItems.Cast<object>()));
            Assert.Equal([1], table.SelectedRows);
            Assert.Null(table.PendingSelection);                                            // applied once
        }
        finally
        {
            setup.Window.Close();
        }
    });

    // One grid shows both table tabs (the same template): the data context changes before the rows of the new tab reach the grid.
    [Fact] public void A_row_selected_by_key_on_a_hidden_table_tab_is_selected_when_the_grid_switches_to_it() => Sta.Run(() =>
    {
        var setup = Create();
        try
        {
            setup.ViewModel.Add(Pack, TableEntry(["unit_key", "cost"], [["a", "1"], ["b", "2"], ["c", "3"]], firstIsKey: true), null);
            var hidden = Assert.IsType<TableTabViewModel>(setup.ViewModel.SelectedTab);
            setup.ViewModel.Add(Pack, TableEntry(["unit_key", "cost"], [["x", "1"]], firstIsKey: true), null);
            Pump(setup.Window);
            var grid = setup.Grid;
            Assert.True(hidden.SelectRowByKey(["c"]));

            setup.ViewModel.SelectedTab = hidden;
            Pump(setup.Window);

            Assert.Same(grid, setup.Grid);                                                  // the same grid, another data context
            Assert.Same(hidden.Rows, setup.Grid.ItemsSource);
            Assert.Same(hidden.Rows[2], Assert.Single(setup.Grid.SelectedItems.Cast<object>()));
            Assert.Equal([2], hidden.SelectedRows);
            Assert.Null(hidden.PendingSelection);
        }
        finally
        {
            setup.Window.Close();
        }
    });

    // ---- leaving a tab with a cell in edit mode -----------------------------------------------------------------------------------------

    // The grid of the selected tab with a cell of the text column in edit mode and the given text typed into it.
    private static TextBox TypeIntoFirstRow(Setup setup, EditableRowsTabViewModel tab, string text)
    {
        var grid = setup.Grid;
        grid.CurrentCell = new DataGridCellInfo(tab.Rows[0], grid.Columns[1]);
        Assert.True(grid.BeginEdit());
        Pump(setup.Window);
        var box = Assert.IsType<TextBox>(grid.Columns[1].GetCellContent(tab.Rows[0]));
        box.Text = text;
        return box;
    }

    // The tab list as the controller sets it up: a tab with a cell in edit mode commits it before it is left.
    private static void CommitBeforeLeaving(TabsViewModel viewModel) =>
        viewModel.CanLeave = tab => tab is not EditableRowsTabViewModel editable || editable.TryCommitPendingEdit();

    [Fact] public void A_header_selection_that_the_tab_list_refuses_goes_back_to_the_old_tab() => Sta.Run(() =>
    {
        var setup = Create();
        try
        {
            CommitBeforeLeaving(setup.ViewModel);
            var first = EditableLoc(setup, new FakeClipboard());
            var second = EditableLoc(setup, new FakeClipboard());
            setup.ViewModel.SelectedTab = first;
            Pump(setup.Window);
            var box = TypeIntoFirstRow(setup, first, "x" + (char)13 + "y");              // a carriage return: not a value of the column

            setup.TabControl.SelectedItem = second;                                     // what a click on the header does to the tab control
            Pump(setup.Window);

            Assert.Same(first, setup.ViewModel.SelectedTab);
            Assert.Same(first, setup.TabControl.SelectedItem);                          // the tab control really went back
            Assert.True(first.HasPendingEdit);
            Assert.NotNull(first.ValidationMessage);
            Assert.Same(box, setup.Grid.Columns[1].GetCellContent(first.Rows[0]));      // the editor with the typed text is still there
            Assert.Equal("Text", first.Document.Rows[0][1]);
        }
        finally
        {
            setup.Window.Close();
        }
    });

    [Fact] public void A_valid_value_is_committed_when_another_tab_is_selected_by_the_header() => Sta.Run(() =>
    {
        var setup = Create();
        try
        {
            CommitBeforeLeaving(setup.ViewModel);
            var first = EditableLoc(setup, new FakeClipboard());
            var second = EditableLoc(setup, new FakeClipboard());
            setup.ViewModel.SelectedTab = first;
            Pump(setup.Window);
            TypeIntoFirstRow(setup, first, "typed");

            setup.TabControl.SelectedItem = second;
            Pump(setup.Window);

            Assert.Same(second, setup.ViewModel.SelectedTab);
            Assert.Same(second, setup.TabControl.SelectedItem);
            Assert.Equal("typed", first.Document.Rows[0][1]);
            Assert.True(first.IsModified);
            Assert.False(first.HasPendingEdit);
        }
        finally
        {
            setup.Window.Close();
        }
    });

    [Fact] public void A_click_on_another_header_does_not_even_start_while_the_cell_holds_an_invalid_value() => Sta.Run(() =>
    {
        var setup = Create();
        try
        {
            CommitBeforeLeaving(setup.ViewModel);
            var first = EditableLoc(setup, new FakeClipboard());
            var second = EditableLoc(setup, new FakeClipboard());
            setup.ViewModel.SelectedTab = first;
            Pump(setup.Window);
            TypeIntoFirstRow(setup, first, "x" + (char)13 + "y");
            var item = Assert.IsType<TabItem>(setup.TabControl.ItemContainerGenerator.ContainerFromIndex(1));
            var header = Descendants<TextBlock>(item).First(t => t.Text == second.Title);
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = header };

            header.RaiseEvent(down);

            Assert.True(down.Handled);                                                  // refused before the tab control moved at all
            Assert.Same(first, setup.TabControl.SelectedItem);
            Assert.True(first.HasPendingEdit);
        }
        finally
        {
            setup.Window.Close();
        }
    });

    // ---- the real hook of the controller ------------------------------------------------------------------------------------------------

    private sealed record Real(Setup View, TestGame Game, FakeEditDialogs Dialogs, TotalWarModTool.UndoRedo.ActionHistory History,
        EditableRowsTabViewModel First, EditableRowsTabViewModel Second);

    // The tab list of the view is the one of a real DocumentsController (CanLeave and the rest are its own hooks), over two editable tabs that
    // share the history of the controller, in a shown window.
    private static void WithController(Func<Real, Task> body) => Sta.RunAsync(async () =>
    {
        using var game = new TestGame();
        var workspace = game.NewController();
        await workspace.LoadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var projects = game.NewProjects(workspace);
        var dialogs = new FakeEditDialogs();
        var history = new TotalWarModTool.UndoRedo.ActionHistory();
        var tabs = new TabsViewModel(game.Texts);
        _ = new DocumentsController(tabs, projects, history, game.Runner, dialogs, game.Log, game.Texts);
        var setup = Create(tabs);
        try
        {
            EditableRowsTabViewModel NewTab(string name)
            {
                var document = TotalWarModTool.Editing.RowsDocument.ForLoc("text/db/" + name, [new LocRow("k", "Text", false), new LocRow("k2", "More", true)]);
                var tab = new EditableRowsTabViewModel(TabSource.ForProject(Guid.NewGuid(), "Project"), "text/db/" + name + ".tsv", game.Texts, tabs.Close, document,
                    history, new FakeClipboard());
                tabs.Tabs.Add(tab);
                return tab;
            }

            var first = NewTab("a.loc");
            var second = NewTab("b.loc");
            tabs.SelectedTab = first;
            Pump(setup.Window);
            await body(new Real(setup, game, dialogs, history, first, second));
        }
        finally
        {
            setup.Window.Close();
        }
    });

    [Fact] public void The_controller_keeps_the_user_on_the_tab_while_its_cell_holds_an_invalid_value() => WithController(real =>
    {
        var (setup, game, dialogs, history, first, second) = real;
        var box = TypeIntoFirstRow(setup, first, "x" + (char)13 + "y");                  // a carriage return: not a value of the column

        setup.TabControl.SelectedItem = second;                                          // what a click on the header does to the tab control
        Pump(setup.Window);

        Assert.Same(first, setup.ViewModel.SelectedTab);
        Assert.Same(first, setup.TabControl.SelectedItem);                               // the tab control really went back
        Assert.Equal((game.Texts.EditPendingInvalidTitle, game.Texts.EditPendingInvalidMessage), Assert.Single(dialogs.Messages));   // one message, not two
        Assert.Equal("Text", first.Document.Rows[0][1]);
        Assert.False(first.IsModified);
        Assert.False(history.CanUndo);
        Assert.True(first.HasPendingEdit);
        Assert.Same(box, setup.Grid.Columns[1].GetCellContent(first.Rows[0]));           // the cell is still in edit mode with the typed text
        return Task.CompletedTask;
    });

    [Fact] public void The_controller_commits_a_valid_value_when_the_user_selects_the_other_tab() => WithController(real =>
    {
        var (setup, _, dialogs, history, first, second) = real;
        TypeIntoFirstRow(setup, first, "typed");

        setup.TabControl.SelectedItem = second;
        Pump(setup.Window);

        Assert.Same(second, setup.ViewModel.SelectedTab);
        Assert.Same(second, setup.TabControl.SelectedItem);
        Assert.Equal("typed", first.Document.Rows[0][1]);
        Assert.True(first.IsModified);
        Assert.False(first.HasPendingEdit);
        Assert.Empty(dialogs.Messages);

        history.Undo();                                                                  // one step takes the value back
        Assert.Equal("Text", first.Document.Rows[0][1]);
        Assert.False(history.CanUndo);
        return Task.CompletedTask;
    });

    // The keys that move the selection of the tab control are asked before it moves. The keyboard state of the test host has no modifiers, so the
    // decision is a function of the key and the modifiers that the view hands to it.
    [Theory]
    [InlineData(Key.Tab, ModifierKeys.Control, true, true)]
    [InlineData(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, true, true)]
    [InlineData(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, false, true)]
    [InlineData(Key.PageUp, ModifierKeys.Control, true, true)]
    [InlineData(Key.PageDown, ModifierKeys.Control | ModifierKeys.Shift, true, true)]
    [InlineData(Key.Tab, ModifierKeys.None, true, false)]
    [InlineData(Key.Tab, ModifierKeys.Shift, true, false)]
    [InlineData(Key.A, ModifierKeys.Control, true, false)]
    [InlineData(Key.Left, ModifierKeys.None, false, true)]
    [InlineData(Key.End, ModifierKeys.None, false, true)]
    [InlineData(Key.Left, ModifierKeys.None, true, false)]
    [InlineData(Key.Down, ModifierKeys.None, true, false)]
    [InlineData(Key.A, ModifierKeys.None, false, false)]
    public void The_keys_that_change_the_tab_are_recognised_with_and_without_Shift(Key key, ModifierKeys modifiers, bool inContent, bool expected)
    {
        // inContent is true for a key pressed in the content of the tab; for the Ctrl keys it makes no difference.
        Assert.Equal(expected, TabsView.ChangesTab(key, modifiers, inContent));
    }

    [Fact] public void The_text_view_is_refused_while_a_cell_holds_an_invalid_value_and_the_switch_goes_back() => Sta.Run(() =>
    {
        var setup = Create();
        try
        {
            var tab = EditableLoc(setup, new FakeClipboard());
            TypeIntoFirstRow(setup, tab, "x" + (char)13 + "y");
            var buttons = Descendants<RadioButton>(setup.View).ToList();

            ((ISelectionItemProvider)new RadioButtonAutomationPeer(buttons[1]).GetPattern(PatternInterface.SelectionItem)!).Select();
            Pump(setup.Window);

            Assert.False(tab.ShowText);
            Assert.Equal([true, false], buttons.Select(b => b.IsChecked == true));      // the grid switch is checked again
            Assert.Equal(Visibility.Visible, setup.Grid.Visibility);
        }
        finally
        {
            setup.Window.Close();
        }
    });

    [Fact] public void The_object_tab_template_shows_the_apply_button_the_result_line_and_the_editor() => Sta.Run(() =>
    {
        var setup = Create();
        try
        {
            var vanilla = new TableSource(SourceKind.Vanilla, "db.pack", @"D:\game\db.pack");
            var row = new ObjectRow([new FieldValue("effect", FieldType.StringU8, "v", null, ReferenceResolution.None, null, null)],
                new RowProvenance(vanilla, [], false, false), null, null);
            var id = new ObjectId("units_tables", ["a"]);
            var order = new LoadOrder([vanilla], null, []);
            var view = new ObjectView(id, row, [], [], [], order, ["units_tables"]);
            var tab = new ObjectTabViewModel(Texts, setup.ViewModel.Close, [new ObjectRef("units_tables", ["a"], null)], 0, key => key);
            tab.ApplyResult(new ObjectBuildResult(view, order, null, new Dictionary<string, string>(), "loc", new LocTable(new Dictionary<string, LocText>(), [])));
            tab.SetApplyResult(new WriteSummary(1, 0, 0, 0, 0, 1, 0, 0), [], 0);
            setup.ViewModel.Tabs.Add(tab);
            setup.ViewModel.SelectedTab = tab;
            Pump(setup.Window);

            var apply = Descendants<Button>(setup.View).Single(button => ReferenceEquals(button.Command, tab.ApplyCommand));
            Assert.Equal(Texts.ObjectsApply, apply.Content);
            Assert.Equal(tab.ApplyToolTip, apply.ToolTip);
            Assert.True(ToolTipService.GetShowOnDisabled(apply));
            Assert.Equal(tab.ResultLine, Descendants<TextBlock>(setup.View).Single(text => text.Text == tab.ResultLine).Text);
            Assert.Equal(tab.Text, Descendants<ObjectEditorView>(setup.View).Single().Editor.Text);
            Assert.NotEqual(string.Empty, tab.Text);

            // Ctrl+Enter applies from anywhere in the tab, not only from the editor.
            var keys = Descendants<DockPanel>(setup.View).SelectMany(panel => panel.InputBindings.OfType<KeyBinding>())
                .Where(binding => binding.Modifiers == ModifierKeys.Control && binding.Key == Key.Return).ToList();
            Assert.Contains(keys, binding => ReferenceEquals(binding.Command, tab.ApplyCommand));
        }
        finally
        {
            setup.Window.Close();
        }
    });
}
