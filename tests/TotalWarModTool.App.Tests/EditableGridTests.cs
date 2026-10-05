using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using TotalWarModTool.Editing;
using TotalWarModTool.Localization;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.App.Tests;

/// <summary>
/// The editable <see cref="TableGrid"/> in a real window: its columns, the commit of a cell, a refused value, the selection, the row numbers and
/// the keys. The window is closed in a finally, so that no test leaves one open. What cannot be driven here: a key with a modifier (the
/// keyboard state of the test host has none, so the key logic is called with the modifiers as an argument), the real focus, and the mouse.
/// </summary>
public class EditableGridTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly TabSource Source = TabSource.ForProject(Guid.NewGuid(), "Mod");

    // Column indexes of Def.
    private const int Name = 0, Cost = 1, Flag = 2;

    private static FieldDefinition F(string name, FieldType type, bool isKey = false) => new(name, type, type.ToString(), isKey, null);

    private static readonly TableDefinition Def = new(0, [F("unit_key", FieldType.StringU8, true), F("cost", FieldType.I32), F("flag", FieldType.Boolean)]);

    private sealed record Setup(EditableRowsTabViewModel Tab, RowsDocument Document, ActionHistory History, FakeClipboard Clipboard, TableGrid Grid, Window Window)
    {
        public DataGridColumn Column(int index) => Grid.Columns[index];

        public object Row(int index) => Tab.Rows[index];

        public FrameworkElement? Content(int row, int column) => Grid.Columns[column].GetCellContent(Row(row));
    }

    private static string ControlsXamlPath() => System.IO.Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml");

    private static EditableRowsTabViewModel NewTab(int rows, out RowsDocument document, out ActionHistory history, IClipboard? clipboard, AppTexts? texts = null)
    {
        var data = Enumerable.Range(0, rows).Select(i => new object?[] { "k" + i, i * 10, i % 2 == 1 }).ToList();
        document = RowsDocument.ForTable("units_tables", 3, @"db\units_tables\data.tsv", Def, data);
        history = new ActionHistory();
        return new EditableRowsTabViewModel(Source, "db/units_tables/data.tsv", texts ?? Texts, _ => { }, document, history, clipboard);
    }

    // The grid as the template of the tab view makes it: the bindings, then the data context; in a shown window with the styles of Controls.xaml.
    // The test host has no Application, so the styles go into the resources of the window.
    private static Setup Create(int rows = 3, AppTexts? texts = null)
    {
        var clipboard = new FakeClipboard();
        var tab = NewTab(rows, out var document, out var history, clipboard, texts);
        var grid = new TableGrid();
        grid.SetBinding(TableGrid.EditColumnsProperty, new Binding(nameof(EditableRowsTabViewModel.Columns)));
        grid.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new Binding(nameof(EditableRowsTabViewModel.Rows)));
        grid.DataContext = tab;
        var window = new Window { Width = 800, Height = 400, ShowActivated = false, Content = grid };
        window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(ControlsXamlPath())));
        window.Show();
        Pump(window);
        return new Setup(tab, document, history, clipboard, grid, window);
    }

    // Bindings and the generation of containers are delivered by the dispatcher; a window nobody pumps needs this nudge.
    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private static void WithGrid(Action<Setup> body, int rows = 3) => Sta.RunAsync(() =>
    {
        var setup = Create(rows);
        try
        {
            body(setup);
        }
        finally
        {
            setup.Window.Close();
        }

        return Task.CompletedTask;
    });

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

    // Puts a cell into edit mode, as a double click or F2 does.
    private static FrameworkElement BeginEdit(Setup setup, int row, int column)
    {
        var item = setup.Row(row);
        setup.Grid.CurrentCell = new DataGridCellInfo(item, setup.Column(column));
        Assert.True(setup.Grid.BeginEdit());
        Pump(setup.Window);
        return setup.Content(row, column)!;
    }

    private static void Select(Setup setup, params int[] rows)
    {
        setup.Grid.UnselectAll();
        foreach (var row in rows)
        {
            setup.Grid.SelectedItems.Add(setup.Row(row));
        }
    }

    [Fact] public void The_grid_builds_an_editable_column_per_field_and_a_check_box_for_booleans() => WithGrid(setup =>
    {
        var grid = setup.Grid;

        Assert.Equal(3, grid.Columns.Count);
        Assert.IsAssignableFrom<DataGridTextColumn>(grid.Columns[Name]);
        Assert.IsAssignableFrom<DataGridTextColumn>(grid.Columns[Cost]);
        Assert.IsAssignableFrom<DataGridCheckBoxColumn>(grid.Columns[Flag]);
        Assert.Equal(["unit_key", "cost", "flag"], grid.Columns.Select(c => Assert.IsType<TextBlock>(c.Header).Text));
        Assert.Equal([FontWeights.Bold, FontWeights.Normal, FontWeights.Normal], grid.Columns.Select(c => ((TextBlock)c.Header).FontWeight));   // the key column is bold
        Assert.False(grid.IsReadOnly);
        Assert.False(grid.CanUserAddRows);
        Assert.False(grid.CanUserDeleteRows);
        Assert.False(grid.CanUserSortColumns);                                  // the row index of the view is the row index of the document
        Assert.True(grid.CanUserReorderColumns);
        Assert.Equal((DataGridSelectionUnit.FullRow, DataGridSelectionMode.Extended), (grid.SelectionUnit, grid.SelectionMode));
        Assert.Equal(DataGridClipboardCopyMode.None, grid.ClipboardCopyMode);   // the own copy command, not the built-in format
        Assert.Equal(DataGridHeadersVisibility.All, grid.HeadersVisibility);

        // The cells show the text, a Boolean a check box.
        Assert.Equal("k1", Assert.IsType<TextBlock>(setup.Content(1, Name)).Text);
        Assert.Equal("10", Assert.IsType<TextBlock>(setup.Content(1, Cost)).Text);
        Assert.Equal((false, true), (Assert.IsType<CheckBox>(setup.Content(0, Flag)).IsChecked, Assert.IsType<CheckBox>(setup.Content(1, Flag)).IsChecked));
    });

    [Fact] public void Committing_a_valid_cell_changes_the_document() => WithGrid(setup =>
    {
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        Assert.False(setup.Tab.HasPendingEdit);                                             // opened, nothing typed: no change
        box.Text = "42";
        Assert.True(setup.Tab.HasPendingEdit);

        Assert.True(setup.Grid.CommitEdit(DataGridEditingUnit.Cell, true));
        Pump(setup.Window);

        Assert.Equal(42, setup.Document.Rows[1][Cost]);
        Assert.True(setup.Tab.IsModified);
        Assert.True(setup.History.CanUndo);
        Assert.False(setup.Tab.HasPendingEdit);
        Assert.Null(setup.Tab.ValidationMessage);
        Assert.Equal("42", Assert.IsType<TextBlock>(setup.Content(1, Cost)).Text);          // back to the shown element
    });

    [Fact] public void A_check_box_cell_ends_its_edit_right_after_the_toggle() => WithGrid(setup =>
    {
        Select(setup, 1);
        var box = Assert.IsType<CheckBox>(BeginEdit(setup, 0, Flag));
        box.IsChecked = true;
        Pump(setup.Window);                                                                 // the end of the edit is posted

        Assert.Equal(true, setup.Document.Rows[0][Flag]);
        Assert.True(setup.Tab.IsModified);
        setup.History.Undo();                                                               // exactly one step
        Assert.Equal(false, setup.Document.Rows[0][Flag]);
        Assert.False(setup.History.CanUndo);
        setup.History.Redo();
        Assert.False(setup.Tab.HasPendingEdit);
        Assert.False(Assert.IsType<CheckBox>(setup.Content(0, Flag)).IsHitTestVisible);     // the shown element again: the cell is not in edit mode

        // The keys of the grid work: a real Insert key goes through the grid and inserts a row below the selected row.
        var insert = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(setup.Grid)!, 0, Key.Insert) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        setup.Grid.RaiseEvent(insert);
        Assert.True(insert.Handled);
        Assert.Equal(4, setup.Document.Rows.Count);
    });

    [Fact] public void An_invalid_cell_stays_in_edit_mode_and_shows_the_message() => WithGrid(setup =>
    {
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        box.Text = "abc";

        Assert.False(setup.Grid.CommitEdit(DataGridEditingUnit.Cell, true));
        Pump(setup.Window);

        // The binding of the editing element has the error, not the DataGridCell: that is where the rule reports it.
        var expected = setup.Tab.CheckCell(Cost, "abc");
        Assert.NotNull(expected);
        Assert.True(Validation.GetHasError(box));
        Assert.Equal(expected, Validation.GetErrors(box)[0].ErrorContent);
        Assert.Equal(expected, box.ToolTip);                                               // the message is the tool tip of the cell
        Assert.Equal(expected, setup.Tab.ValidationMessage);                               // and the line under the header of the tab
        Assert.True(setup.Tab.HasPendingEdit);
        Assert.Same(box, setup.Content(1, Cost));                                          // still the editing element
        Assert.Equal(10, setup.Document.Rows[1][Cost]);
        Assert.False(setup.Tab.IsModified);
        Assert.False(setup.History.CanUndo);

        // A valid text takes the error away and commits.
        box.Text = "11";
        Assert.True(setup.Grid.CommitEdit(DataGridEditingUnit.Cell, true));
        Assert.Equal(11, setup.Document.Rows[1][Cost]);
        Assert.Null(setup.Tab.ValidationMessage);
        Assert.False(setup.Tab.HasPendingEdit);
    });

    [Fact] public void Escape_discards_the_input() => WithGrid(setup =>
    {
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        box.Text = "abc";
        Assert.False(setup.Grid.CommitEdit(DataGridEditingUnit.Cell, true));                // an error is showing

        // What the keyboard does: the preview event first, then the key event.
        var source = PresentationSource.FromVisual(box)!;
        box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        Pump(setup.Window);
        Assert.Null(setup.Tab.Notice);                                                      // Esc is the user's own decision: no notice

        Assert.Equal("10", Assert.IsType<TextBlock>(setup.Content(1, Cost)).Text);          // the shown element again, with the old value
        Assert.Equal(10, setup.Document.Rows[1][Cost]);
        Assert.False(setup.Tab.IsModified);
        Assert.False(setup.Tab.HasPendingEdit);
        Assert.Null(setup.Tab.ValidationMessage);
    });

    [Fact] public void The_editor_of_a_cell_has_the_themed_style() => WithGrid(setup =>
    {
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 0, Name));
        var checkBox = Assert.IsType<CheckBox>(setup.Content(0, Flag));

        Assert.Same(setup.Window.FindResource("ThemeGridCellEditor"), box.Style);
        Assert.Same(setup.Window.FindResource("ThemeGridCheckBox"), checkBox.Style);
    });

    [Fact] public void A_cell_in_edit_mode_is_committed_by_TryCommitPendingEdit() => WithGrid(setup =>
    {
        Assert.True(setup.Tab.TryCommitPendingEdit());                                      // nothing is pending: true
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        box.Text = "42";                                                                    // typed, the cell is not left
        Assert.False(setup.Tab.IsModified);
        Assert.Equal(10, setup.Document.Rows[1][Cost]);

        Assert.True(setup.Tab.TryCommitPendingEdit());

        Assert.Equal(42, setup.Document.Rows[1][Cost]);
        Assert.True(setup.Tab.IsModified);
        Assert.False(setup.Tab.HasPendingEdit);
        Assert.IsType<TextBlock>(setup.Content(1, Cost));
    });

    [Fact] public void A_pending_cell_with_an_invalid_value_is_not_committed() => WithGrid(setup =>
    {
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        box.Text = "abc";

        Assert.False(setup.Tab.TryCommitPendingEdit());

        Assert.Equal(10, setup.Document.Rows[1][Cost]);
        Assert.False(setup.Tab.IsModified);
        Assert.True(setup.Tab.HasPendingEdit);
        Assert.Same(box, setup.Content(1, Cost));
        Assert.Equal(setup.Tab.CheckCell(Cost, "abc"), setup.Tab.ValidationMessage);
    });

    [Theory]
    [InlineData("42")]
    [InlineData("abc")]
    public void A_pending_cell_is_dropped_on_purpose_without_writing_its_text_or_a_notice(string typed) => WithGrid(setup =>
    {
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        box.Text = typed;
        Assert.True(setup.Tab.HasPendingEdit);

        setup.Tab.DiscardPendingEdit();                                                     // what the app does before it replaces the document
        Pump(setup.Window);

        Assert.Equal(10, setup.Document.Rows[1][Cost]);                                     // the text is written nowhere
        Assert.False(setup.Tab.IsModified);
        Assert.False(setup.History.CanUndo);
        Assert.Null(setup.Tab.Notice);                                                      // the user chose this: no notice
        Assert.False(setup.Tab.HasPendingEdit);
        Assert.Null(setup.Tab.ValidationMessage);
        Assert.Equal("10", Assert.IsType<TextBlock>(setup.Content(1, Cost)).Text);          // the shown element again, with the old value
        Assert.True(setup.Tab.TryCommitPendingEdit());                                      // nothing is left to commit
        Assert.Equal(10, setup.Document.Rows[1][Cost]);
    });

    [Fact] public void The_selection_reaches_the_view_model_as_row_indexes() => WithGrid(setup =>
    {
        Select(setup, 2, 0);

        Assert.Equal([0, 2], setup.Tab.SelectedRows);                                       // ascending, whatever the order of the clicks

        setup.Grid.UnselectAll();
        Assert.Empty(setup.Tab.SelectedRows);
    });

    [Fact] public void The_rows_that_a_command_asks_for_are_selected_and_the_first_is_current() => WithGrid(setup =>
    {
        Select(setup, 0, 2);

        setup.Tab.InsertRowCommand.Execute(null);                                           // after the last selected row
        Pump(setup.Window);

        Assert.Equal(4, setup.Tab.Rows.Count);
        Assert.Same(setup.Row(3), Assert.Single(setup.Grid.SelectedItems.Cast<object>()));
        Assert.Equal([3], setup.Tab.SelectedRows);
        Assert.Same(setup.Row(3), setup.Grid.CurrentCell.Item);

        setup.Tab.DuplicateRowsCommand.Execute(null);                                       // a copy after row 3
        Pump(setup.Window);
        Assert.Equal(5, setup.Tab.Rows.Count);
        Assert.Equal([4], setup.Tab.SelectedRows);

        Select(setup, 1);
        setup.Tab.DeleteRowsCommand.Execute(null);                                          // the row that took its place
        Pump(setup.Window);
        Assert.Equal(4, setup.Tab.Rows.Count);
        Assert.Equal([1], setup.Tab.SelectedRows);
        Assert.Same(setup.Row(1), Assert.Single(setup.Grid.SelectedItems.Cast<object>()));
    });

    [Fact] public void The_column_headers_sit_above_their_cells_next_to_the_row_headers() => WithGrid(setup =>
    {
        var rowHeader = Descendants<DataGridRowHeader>(setup.Grid).First();
        Assert.True(rowHeader.ActualWidth > 0);

        // The corner above the row headers is as wide as they are, so the headers of the columns are not shifted against the cells.
        var firstRow = Descendants<DataGridRow>(setup.Grid).First(row => row.GetIndex() == 0);
        var cellX = Descendants<DataGridCell>(firstRow).First().TranslatePoint(new Point(0, 0), setup.Grid).X;
        var headerX = Descendants<DataGridColumnHeader>(setup.Grid).First(h => h.Column == setup.Column(Name)).TranslatePoint(new Point(0, 0), setup.Grid).X;
        Assert.True(cellX >= rowHeader.ActualWidth);
        Assert.InRange(Math.Abs(cellX - headerX), 0, 3);                                   // the border of the cell, a pixel or two
    });

    [Fact] public void A_paste_selects_all_pasted_rows_in_one_change_of_the_selection() => WithGrid(setup =>
    {
        object?[][] pasted = [["p", 1, false], ["q", 2, true], ["r", 3, false]];
        setup.Clipboard.Text = ClipboardRows.Write(Def.Fields, pasted).Text;
        Select(setup, 0);
        var changes = 0;
        setup.Grid.SelectionChanged += (_, _) => changes++;

        setup.Tab.PasteRowsCommand.Execute(null);
        Pump(setup.Window);

        Assert.Equal(6, setup.Tab.Rows.Count);
        Assert.Equal([setup.Row(1), setup.Row(2), setup.Row(3)], setup.Grid.SelectedItems.Cast<object>().OrderBy(item => setup.Tab.Rows.IndexOf((EditRow)item)));
        Assert.Equal([1, 2, 3], setup.Tab.SelectedRows);
        Assert.Equal(1, changes);
        Assert.Same(setup.Row(1), setup.Grid.CurrentCell.Item);
    });

    [Fact] public void The_row_header_shows_the_row_number_also_after_rows_were_inserted_in_front() => WithGrid(setup =>
    {
        void AssertNumbers(int count)
        {
            var rows = Descendants<DataGridRow>(setup.Grid).ToList();
            Assert.Equal(count, rows.Count);
            Assert.Equal(rows.Select(row => (row.GetIndex() + 1, row.GetIndex() + 1)), rows.Select(row => (row.GetIndex() + 1, (int)row.Header)));
            Assert.Equal(Enumerable.Range(1, count), rows.OrderBy(r => r.GetIndex()).Select(r => (int)r.Header));
        }

        AssertNumbers(3);

        setup.History.Execute(setup.Document.InsertRows(0, [setup.Document.NewRow()]));    // in front: every other row moves down
        Pump(setup.Window);
        AssertNumbers(4);

        setup.History.Undo();
        Pump(setup.Window);
        AssertNumbers(3);
        Assert.Equal(Visibility.Visible, Descendants<DataGridRowHeader>(setup.Grid).First().Visibility);
    });

    [Fact] public void The_keys_run_the_row_commands_when_no_cell_is_in_edit_mode() => WithGrid(setup =>
    {
        Select(setup, 1);
        Assert.True(setup.Grid.HandleKey(Key.Insert, ModifierKeys.None, false));
        Assert.Equal(4, setup.Document.Rows.Count);                                         // after row 1
        Assert.Equal([2], setup.Tab.SelectedRows);

        Assert.True(setup.Grid.HandleKey(Key.D, ModifierKeys.Control, false));
        Assert.Equal(5, setup.Document.Rows.Count);

        Select(setup, 0, 1);
        Assert.True(setup.Grid.HandleKey(Key.C, ModifierKeys.Control, false));
        Assert.StartsWith("unit_key\tcost\tflag\nk0\t0\tfalse\nk1\t10\ttrue\n", setup.Clipboard.Text);

        Select(setup, 4);
        Assert.True(setup.Grid.HandleKey(Key.V, ModifierKeys.Control, false));              // the two rows after the last row
        Assert.Equal(7, setup.Document.Rows.Count);
        Assert.Equal("k0", setup.Document.Rows[5][Name]);

        Select(setup, 5, 6);
        Assert.True(setup.Grid.HandleKey(Key.Delete, ModifierKeys.None, false));
        Assert.Equal(5, setup.Document.Rows.Count);
    });

    [Fact] public void Other_keys_are_left_to_the_grid_and_the_window() => WithGrid(setup =>
    {
        Select(setup, 1);

        Assert.False(setup.Grid.HandleKey(Key.Z, ModifierKeys.Control, false));            // the global undo of the window
        Assert.False(setup.Grid.HandleKey(Key.Y, ModifierKeys.Control, false));
        Assert.False(setup.Grid.HandleKey(Key.S, ModifierKeys.Control, false));            // save works from a cell: the grid does not take it
        Assert.False(setup.Grid.HandleKey(Key.A, ModifierKeys.None, false));
        Assert.False(setup.Grid.HandleKey(Key.Insert, ModifierKeys.Shift, false));
        Assert.Equal(3, setup.Document.Rows.Count);
    });

    [Fact] public void In_edit_mode_the_keys_belong_to_the_editor_except_undo_and_redo() => WithGrid(setup =>
    {
        Select(setup, 1);
        var editor = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        editor.Text = "7";
        Assert.True(setup.Tab.HasPendingEdit);
        setup.Tab.Rows[0][Cost] = "5";                                                   // an older step in the global history

        foreach (var key in new[] { Key.Insert, Key.Delete })
        {
            Assert.False(setup.Grid.HandleKey(key, ModifierKeys.None, true));
        }

        foreach (var key in new[] { Key.C, Key.V, Key.D })
        {
            Assert.False(setup.Grid.HandleKey(key, ModifierKeys.Control, true));
        }

        Assert.Equal(3, setup.Document.Rows.Count);
        Assert.Null(setup.Clipboard.Text);

        // Undo and redo are the editor's own: handled, so the window never sees them, also when the editor has nothing to undo.
        Assert.True(setup.Grid.HandleKey(Key.Z, ModifierKeys.Control, true));
        Assert.True(setup.Grid.HandleKey(Key.Y, ModifierKeys.Control, true));
        Assert.Equal(5, setup.Document.Rows[0][Cost]);
        Assert.True(setup.History.CanUndo);
        Assert.False(setup.History.CanRedo);
    });

    [Fact] public void The_context_menu_opens_only_when_no_cell_is_in_edit_mode_or_its_value_could_be_committed() => WithGrid(setup =>
    {
        Assert.True(setup.Grid.PrepareContextMenu());                                       // nothing is being edited

        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        box.Text = "abc";
        Assert.False(setup.Grid.PrepareContextMenu());                                      // an invalid value: the menu stays closed
        Assert.Same(box, setup.Content(1, Cost));                                           // and the editor stays
        Assert.Equal(10, setup.Document.Rows[1][Cost]);
        Assert.True(setup.Tab.HasPendingEdit);

        box.Text = "42";
        Assert.True(setup.Grid.PrepareContextMenu());                                       // a valid value is committed, then the menu opens
        Assert.Equal(42, setup.Document.Rows[1][Cost]);
        Assert.False(setup.Tab.HasPendingEdit);
        Assert.IsType<TextBlock>(setup.Content(1, Cost));
    });

    [Fact] public void A_row_that_leaves_under_a_cell_in_edit_mode_does_not_leave_a_pending_edit_behind() => WithGrid(setup =>
    {
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        box.Text = "77";
        Assert.True(setup.Tab.HasPendingEdit);

        setup.History.Execute(setup.Document.DeleteRows([1]));                              // an undo or a reload does the same to the rows
        Pump(setup.Window);

        Assert.False(setup.Tab.HasPendingEdit);
        Assert.Null(setup.Tab.ValidationMessage);
        Assert.Equal(Texts.GridEditDiscarded, setup.Tab.Notice);                            // the one case besides Esc: the user is told
        Assert.True(setup.Tab.TryCommitPendingEdit());
    });

    [Fact] public void A_cell_that_is_opened_and_not_changed_is_no_pending_change() => WithGrid(setup =>
    {
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));                        // F2, nothing typed
        Assert.False(setup.Tab.HasPendingEdit);

        box.Text = "11";
        Assert.True(setup.Tab.HasPendingEdit);
        box.Text = "10";                                                                    // the old text again: the change is taken back
        Assert.False(setup.Tab.HasPendingEdit);

        // The cell is still in edit mode, so the keys belong to it.
        Assert.False(setup.Grid.HandleKey(Key.Insert, ModifierKeys.None, true));
        Assert.True(setup.Tab.TryCommitPendingEdit());
        Assert.False(setup.Tab.IsModified);
        Assert.False(setup.History.CanUndo);
    });

    [Fact] public void The_editor_undoes_its_own_typing_with_Ctrl_Z_and_the_history_is_untouched() => WithGrid(setup =>
    {
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        box.Focus();
        box.SelectAll();
        box.SelectedText = "99";                                                            // typed
        Assert.Equal("99", box.Text);
        Assert.True(box.CanUndo);

        Assert.True(setup.Grid.HandleKey(Key.Z, ModifierKeys.Control, true));

        Assert.Equal("10", box.Text);                                                       // the text is back
        Assert.False(setup.History.CanUndo);
        Assert.Equal(10, setup.Document.Rows[1][Cost]);
    });

    [Fact] public void A_real_key_event_reaches_the_key_handling_of_the_grid() => WithGrid(setup =>
    {
        Select(setup, 1);
        var insert = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(setup.Grid)!, 0, Key.Insert) { RoutedEvent = Keyboard.PreviewKeyDownEvent };

        setup.Grid.RaiseEvent(insert);                                                      // no modifier: the host can do this one
        Assert.True(insert.Handled);
        Assert.Equal(4, setup.Document.Rows.Count);

        var other = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(setup.Grid)!, 0, Key.A) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        setup.Grid.RaiseEvent(other);
        Assert.False(other.Handled);
        // What the host cannot do: a key with Ctrl. Keyboard.Modifiers reads the real keyboard, which no test can press; that part is
        // tested through HandleKey, which takes the modifiers as an argument.
    });

    private static void RaiseRightDown(UIElement target) =>
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = target });

    private static DataGridCell CellOf(Setup setup, int row) =>
        Descendants<DataGridCell>(Descendants<DataGridRow>(setup.Grid).First(r => r.GetIndex() == row)).First();

    [Fact] public void A_right_click_on_another_row_commits_the_cell_and_selects_the_row_under_the_mouse() => WithGrid(setup =>
    {
        Select(setup, 0);
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        box.Text = "42";

        RaiseRightDown(CellOf(setup, 2));

        Assert.Equal(42, setup.Document.Rows[1][Cost]);                                     // committed
        Assert.False(setup.Tab.HasPendingEdit);
        Assert.Equal([2], setup.Tab.SelectedRows);                                          // the row that was pointed at, so Delete acts on it
    });

    [Fact] public void A_right_click_on_another_row_does_nothing_while_the_cell_holds_an_invalid_value() => WithGrid(setup =>
    {
        Select(setup, 0);
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        box.Text = "abc";

        RaiseRightDown(CellOf(setup, 2));

        Assert.Same(box, setup.Content(1, Cost));                                           // the editor and its message stay
        Assert.Equal([0], setup.Tab.SelectedRows);
        Assert.Equal(setup.Tab.CheckCell(Cost, "abc"), setup.Tab.ValidationMessage);
        Assert.Equal(10, setup.Document.Rows[1][Cost]);
    });

    [Fact] public void The_validation_message_follows_a_language_switch() => Sta.RunAsync(() =>
    {
        var ui = new UiText(System.IO.Path.Combine(RepoRoot.Find(), "locales"));
        var setup = Create(texts: new AppTexts(ui));
        try
        {
            var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
            box.Text = "abc";
            Assert.False(setup.Grid.CommitEdit(DataGridEditingUnit.Cell, true));
            var english = setup.Tab.ValidationMessage;
            Assert.StartsWith("Expected a whole number", english);

            ui.Load("de");

            Assert.StartsWith("Erwartet wird eine ganze Zahl", setup.Tab.ValidationMessage);
        }
        finally
        {
            setup.Window.Close();
        }

        return Task.CompletedTask;
    });

    [Fact] public void A_read_only_grid_copies_with_Ctrl_C_and_takes_no_other_key() => Sta.RunAsync(() =>
    {
        var clipboard = new FakeClipboard();
        var tab = new TableTabViewModel(Source, "db/x_tables/data__", Texts, _ => { },
            new DecodedTable(TableDecodeStatus.Ok, "x_tables", 0, false, new TableDefinition(0, [F("value", FieldType.I32)]), [[1], [2]], null), clipboard);
        var grid = new TableGrid { DataContext = tab };
        tab.SelectedRows = [1];

        Assert.True(grid.HandleKey(Key.C, ModifierKeys.Control, false));
        Assert.Equal("value\n2\n", clipboard.Text);
        Assert.False(grid.HandleKey(Key.Insert, ModifierKeys.None, false));
        Assert.False(grid.HandleKey(Key.Delete, ModifierKeys.None, false));
        Assert.False(grid.HandleKey(Key.V, ModifierKeys.Control, false));
        Assert.False(grid.HandleKey(Key.D, ModifierKeys.Control, false));
        return Task.CompletedTask;
    });

    [Fact] public void One_grid_shows_the_tabs_one_after_another_and_a_typed_value_is_committed_when_the_tab_is_left() => WithGrid(setup =>
    {
        var second = NewTab(2, out var secondDocument, out _, setup.Clipboard);
        Select(setup, 1);
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 2, Cost));
        box.Text = "77";                                                                    // typed into the first tab, not committed
        Assert.NotNull(setup.Tab.CommitPendingEdit);

        // What the template does when another editable tab is selected: the data context changes, the bindings follow. (In the app the
        // tab list has asked the tab before; this is the second line of defence.)
        setup.Grid.DataContext = second;
        Pump(setup.Window);

        Assert.Same(second.Rows, setup.Grid.ItemsSource);
        Assert.Equal(3, setup.Grid.Columns.Count);                                          // the same definition: the columns stay
        Assert.Null(setup.Tab.CommitPendingEdit);                                           // the first tab is let go
        Assert.NotNull(second.CommitPendingEdit);
        Assert.Equal(77, setup.Document.Rows[2][Cost]);                                     // the typed value is in the first document: not dropped
        Assert.True(setup.Tab.IsModified);
        setup.History.Undo();                                                               // as one step
        Assert.Equal(20, setup.Document.Rows[2][Cost]);
        Assert.False(setup.History.CanUndo);
        Assert.False(setup.Tab.HasPendingEdit);
        Assert.Null(setup.Tab.Notice);
        Assert.Empty(setup.Tab.SelectedRows);                                               // the first tab forgot a selection the grid does not show
        Assert.Empty(second.SelectedRows);

        // An edit in the second tab changes the second document only.
        var secondBox = Assert.IsType<TextBox>(BeginEdit(new Setup(second, secondDocument, new ActionHistory(), setup.Clipboard, setup.Grid, setup.Window), 1, Cost));
        secondBox.Text = "42";
        Assert.True(second.TryCommitPendingEdit());
        Assert.Equal(42, secondDocument.Rows[1][Cost]);
        Assert.Equal(10, setup.Document.Rows[1][Cost]);
        Assert.False(setup.Tab.IsModified);
        Assert.True(second.IsModified);
    });

    [Fact] public void A_value_that_cannot_be_committed_is_dropped_with_a_notice_when_the_grid_goes_away() => WithGrid(setup =>
    {
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        box.Text = "abc";
        Assert.False(setup.Grid.CommitEdit(DataGridEditingUnit.Cell, true));
        Assert.True(setup.Tab.HasPendingEdit);

        setup.Window.Content = null;                                                        // the tab leaves the screen (another kind of tab)
        Pump(setup.Window);

        Assert.False(setup.Tab.HasPendingEdit);
        Assert.Null(setup.Tab.ValidationMessage);
        Assert.Null(setup.Tab.CommitPendingEdit);
        Assert.Equal(10, setup.Document.Rows[1][Cost]);
        Assert.Equal(Texts.GridEditDiscarded, setup.Tab.Notice);                            // not silent
    });

    [Fact] public void A_valid_value_is_committed_when_the_grid_goes_away() => WithGrid(setup =>
    {
        var box = Assert.IsType<TextBox>(BeginEdit(setup, 1, Cost));
        box.Text = "42";

        setup.Window.Content = null;
        Pump(setup.Window);

        Assert.Equal(42, setup.Document.Rows[1][Cost]);
        Assert.Null(setup.Tab.Notice);
    });

    [Fact] public void The_read_only_grid_is_unchanged() => Sta.RunAsync(() =>
    {
        var rows = new List<object?[]> { new object?[] { "a", "1" }, new object?[] { "b", "2" } };
        var grid = new TableGrid { ColumnNames = ["unit_key", "cost"], ItemsSource = rows };
        var headers = grid.HeadersVisibility;

        Assert.True(grid.IsReadOnly);
        Assert.False(grid.CanUserAddRows);
        Assert.False(grid.CanUserDeleteRows);
        Assert.True(grid.CanUserSortColumns);
        Assert.Equal(2, grid.Columns.Count);
        Assert.All(grid.Columns, column =>
        {
            var binding = Assert.IsType<Binding>(Assert.IsType<DataGridTextColumn>(column).Binding);
            Assert.Equal(BindingMode.OneTime, binding.Mode);
        });
        Assert.Equal(["unit_key", "cost"], grid.Columns.Select(c => Assert.IsType<TextBlock>(c.Header).Text));
        Assert.Null(grid.EditColumns);

        // Clearing the edit columns of a grid that had them makes it a read-only grid again.
        grid.EditColumns = Def.Fields;
        Assert.False(grid.IsReadOnly);
        grid.EditColumns = null;
        Assert.True(grid.IsReadOnly);
        Assert.True(grid.CanUserSortColumns);
        Assert.Equal(headers, grid.HeadersVisibility);                                      // what the style says, not what the edit mode set
        Assert.Empty(grid.Columns);
        return Task.CompletedTask;
    });
}
