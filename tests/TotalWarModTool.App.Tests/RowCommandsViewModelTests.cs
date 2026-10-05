using System.Windows.Input;
using TotalWarModTool.Editing;
using TotalWarModTool.Localization;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class RowCommandsViewModelTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly TableDefinition Def = new(0, [new FieldDefinition("value", FieldType.I32, nameof(FieldType.I32), false, null)]);

    private sealed record Fixture(TabsViewModel Tabs, RowCommandsViewModel Commands, FakeClipboard Clipboard);

    private static Fixture Make(AppTexts? texts = null)
    {
        var shown = texts ?? Texts;
        var tabs = new TabsViewModel(shown);
        return new Fixture(tabs, new RowCommandsViewModel(tabs, shown), new FakeClipboard());
    }

    private static EditableRowsTabViewModel EditableTab(FakeClipboard? clipboard, AppTexts? texts = null, int rows = 3, string file = "data.tsv")
    {
        var data = Enumerable.Range(1, rows).Select(i => new object?[] { i }).ToList();
        var document = RowsDocument.ForTable("x_tables", 0, @"db\x_tables\" + file, Def, data);
        return new EditableRowsTabViewModel(TabSource.ForProject(Guid.NewGuid(), "My Mod"), "db/x_tables/" + file, texts ?? Texts, _ => { },
            document, new ActionHistory(), clipboard);
    }

    private static TableTabViewModel ReadOnlyTab(FakeClipboard clipboard) =>
        new(new TabSource(@"D:\data\a.pack", "a.pack"), "db/x_tables/data__", Texts, _ => { },
            new DecodedTable(TableDecodeStatus.Ok, "x_tables", 0, false, Def, [[1], [2]], null), clipboard);

    private static void Select(TabsViewModel tabs, TabViewModel tab)
    {
        tabs.Tabs.Add(tab);
        tabs.SelectedTab = tab;
    }

    private static ICommand[] All(RowCommandsViewModel c) => [c.InsertRowCommand, c.DuplicateRowsCommand, c.DeleteRowsCommand, c.CopyCommand, c.PasteCommand];

    private static string?[] Reasons(RowCommandsViewModel c) => [c.InsertRowReason, c.DuplicateRowsReason, c.DeleteRowsReason, c.CopyReason, c.PasteReason];

    private static bool[] Enabled(RowCommandsViewModel c) => All(c).Select(command => command.CanExecute(null)).ToArray();

    private static readonly string[] ReasonNames =
    [
        nameof(RowCommandsViewModel.InsertRowReason), nameof(RowCommandsViewModel.DuplicateRowsReason), nameof(RowCommandsViewModel.DeleteRowsReason),
        nameof(RowCommandsViewModel.CopyReason), nameof(RowCommandsViewModel.PasteReason),
    ];

    [Fact] public void Without_a_tab_all_five_are_disabled_and_say_to_open_a_table()
    {
        var f = Make();

        Assert.All(Enabled(f.Commands), enabled => Assert.False(enabled));
        Assert.All(Reasons(f.Commands), reason => Assert.Equal(Texts.ToolbarOpenTable, reason));
        Assert.Equal("Open a table first.", Texts.ToolbarOpenTable);
        Assert.All(All(f.Commands), command => command.Execute(null));                        // nothing to forward to, nothing happens
    }

    [Fact] public void A_tab_that_is_no_table_gives_the_same_reason_as_no_tab()
    {
        var f = Make();
        Select(f.Tabs, new TextTabViewModel(new TabSource(@"D:\data\a.pack", "a.pack"), "text/a.txt", Texts, _ => { }, new TextContent("x", "UTF-8", false, 1)));

        Assert.All(Enabled(f.Commands), enabled => Assert.False(enabled));
        Assert.All(Reasons(f.Commands), reason => Assert.Equal(Texts.ToolbarOpenTable, reason));
    }

    [Fact] public void A_read_only_table_enables_only_copy_and_the_others_say_read_only()
    {
        var f = Make();
        var tab = ReadOnlyTab(f.Clipboard);
        Select(f.Tabs, tab);
        tab.SelectedRows = [1];

        Assert.Equal([false, false, false, true, false], Enabled(f.Commands));
        Assert.Equal<IEnumerable<string?>>([Texts.ToolbarReadOnly, Texts.ToolbarReadOnly, Texts.ToolbarReadOnly, null, Texts.ToolbarReadOnly], Reasons(f.Commands));

        f.Commands.CopyCommand.Execute(null);
        Assert.Equal("value\n2\n", f.Clipboard.Text);
    }

    [Fact] public void A_read_only_table_without_a_selection_says_to_select_a_row_for_copy()
    {
        var f = Make();
        Select(f.Tabs, ReadOnlyTab(f.Clipboard));

        Assert.False(f.Commands.CopyCommand.CanExecute(null));
        Assert.Equal(Texts.ToolbarSelectRows, f.Commands.CopyReason);
        Assert.Equal(Texts.ToolbarReadOnly, f.Commands.InsertRowReason);
    }

    [Fact] public void An_editable_tab_without_a_selection_enables_insert_and_paste_only()
    {
        var f = Make();
        Select(f.Tabs, EditableTab(f.Clipboard));

        Assert.Equal([true, false, false, false, true], Enabled(f.Commands));
        Assert.Equal<IEnumerable<string?>>([null, Texts.ToolbarSelectRows, Texts.ToolbarSelectRows, Texts.ToolbarSelectRows, null], Reasons(f.Commands));
        Assert.Equal("Select at least one row.", Texts.ToolbarSelectRows);
    }

    [Fact] public void With_a_selection_all_five_are_enabled_and_each_forwards_to_the_tab()
    {
        var f = Make();
        var tab = EditableTab(f.Clipboard);
        Select(f.Tabs, tab);
        tab.SelectedRows = [0];

        Assert.All(Enabled(f.Commands), enabled => Assert.True(enabled));
        Assert.All(Reasons(f.Commands), reason => Assert.Null(reason));

        f.Commands.InsertRowCommand.Execute(null);
        Assert.Equal(4, tab.Document.Rows.Count);
        f.Commands.DuplicateRowsCommand.Execute(null);
        Assert.Equal(5, tab.Document.Rows.Count);
        f.Commands.DeleteRowsCommand.Execute(null);
        Assert.Equal(4, tab.Document.Rows.Count);

        tab.SelectedRows = [3];
        f.Commands.CopyCommand.Execute(null);
        Assert.Equal("value\n3\n", f.Clipboard.Text);
        f.Commands.PasteCommand.Execute(null);
        Assert.Equal(5, tab.Document.Rows.Count);
        Assert.Equal(3, tab.Document.Rows[4][0]);
    }

    [Fact] public void An_editable_command_that_cannot_run_is_not_forwarded()
    {
        var f = Make();
        var tab = EditableTab(f.Clipboard);
        Select(f.Tabs, tab);

        f.Commands.DeleteRowsCommand.Execute(null);                                           // no selection

        Assert.Equal(3, tab.Document.Rows.Count);
    }

    [Fact] public void A_pending_cell_edit_is_committed_before_the_command_and_a_value_that_cannot_be_committed_stops_it()
    {
        var f = Make();
        var tab = EditableTab(f.Clipboard);
        Select(f.Tabs, tab);
        tab.SelectedRows = [0];
        var calls = new List<string>();
        tab.CommitPendingEdit = () =>
        {
            calls.Add("commit rows=" + tab.Document.Rows.Count);
            return false;
        };

        f.Commands.InsertRowCommand.Execute(null);
        Assert.Equal(3, tab.Document.Rows.Count);                                             // the cell stays in edit mode: nothing happened

        tab.CommitPendingEdit = () =>
        {
            calls.Add("commit rows=" + tab.Document.Rows.Count);
            return true;
        };
        f.Commands.InsertRowCommand.Execute(null);

        Assert.Equal(4, tab.Document.Rows.Count);
        Assert.Equal(["commit rows=3", "commit rows=3"], calls);                               // committed before the row was inserted
    }

    [Fact] public void Switching_the_selected_tab_updates_the_states_and_raises_the_events()
    {
        var f = Make();
        var editable = EditableTab(f.Clipboard);
        var readOnly = ReadOnlyTab(f.Clipboard);
        f.Tabs.Tabs.Add(editable);
        f.Tabs.Tabs.Add(readOnly);
        f.Tabs.SelectedTab = editable;
        editable.SelectedRows = [0];
        Assert.All(Enabled(f.Commands), enabled => Assert.True(enabled));

        var canExecute = new int[5];
        var commands = All(f.Commands);
        for (var i = 0; i < commands.Length; i++)
        {
            var index = i;
            commands[i].CanExecuteChanged += (_, _) => canExecute[index]++;
        }

        var raised = new List<string?>();
        f.Commands.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        f.Tabs.SelectedTab = readOnly;

        Assert.All(canExecute, count => Assert.True(count > 0));
        Assert.All(ReasonNames, name => Assert.Contains(name, raised));
        Assert.Equal([false, false, false, false, false], Enabled(f.Commands));                // read-only without a selection
        Assert.Equal(Texts.ToolbarReadOnly, f.Commands.DeleteRowsReason);

        raised.Clear();
        f.Tabs.SelectedTab = editable;
        Assert.Equal([true, true, true, true, true], Enabled(f.Commands));
        Assert.All(ReasonNames, name => Assert.Contains(name, raised));
        Assert.Contains(nameof(RowCommandsViewModel.DeleteRowsToolTip), raised);
    }

    [Fact] public void Closing_the_selected_tab_makes_all_five_follow_the_new_selection()
    {
        var f = Make();
        var editable = EditableTab(f.Clipboard);
        Select(f.Tabs, editable);
        Assert.True(f.Commands.InsertRowCommand.CanExecute(null));

        f.Tabs.CloseWithoutAsking(editable);

        Assert.All(Enabled(f.Commands), enabled => Assert.False(enabled));
        Assert.Equal(Texts.ToolbarOpenTable, f.Commands.InsertRowReason);
    }

    [Fact] public void A_change_of_the_selection_in_the_tab_is_followed()
    {
        var f = Make();
        var tab = EditableTab(f.Clipboard);
        Select(f.Tabs, tab);
        var raised = 0; var reasons = new List<string?>();
        f.Commands.DeleteRowsCommand.CanExecuteChanged += (_, _) => raised++;
        f.Commands.PropertyChanged += (_, e) => reasons.Add(e.PropertyName);

        tab.SelectedRows = [1];

        Assert.True(raised > 0);
        Assert.Contains(nameof(RowCommandsViewModel.DeleteRowsReason), reasons);
        Assert.True(f.Commands.DeleteRowsCommand.CanExecute(null));
        Assert.Null(f.Commands.DeleteRowsReason);

        tab.SelectedRows = [];
        Assert.False(f.Commands.DeleteRowsCommand.CanExecute(null));
        Assert.Equal(Texts.ToolbarSelectRows, f.Commands.DeleteRowsReason);
    }

    [Fact] public void The_old_tab_is_no_longer_followed_after_the_switch()
    {
        var f = Make();
        var first = EditableTab(f.Clipboard, file: "a.tsv");
        var second = EditableTab(f.Clipboard, file: "b.tsv");
        f.Tabs.Tabs.Add(first);
        f.Tabs.Tabs.Add(second);
        f.Tabs.SelectedTab = first;
        f.Tabs.SelectedTab = second;

        var raised = 0; var reasons = 0;
        foreach (var command in All(f.Commands))
        {
            command.CanExecuteChanged += (_, _) => raised++;
        }

        f.Commands.PropertyChanged += (_, _) => reasons++;

        first.SelectedRows = [0];                                                              // raises delete, duplicate and copy of the old tab
        first.InsertRowCommand.Execute(null);

        Assert.Equal((0, 0), (raised, reasons));
        Assert.Equal(Texts.ToolbarSelectRows, f.Commands.DeleteRowsReason);                    // still the state of the second tab
    }

    [Fact] public void A_language_switch_changes_the_reasons_and_the_tool_tips()
    {
        var ui = new UiText(Path.Combine(RepoRoot.Find(), "locales")); var texts = new AppTexts(ui);
        var f = Make(texts);
        var raised = new List<string?>();
        f.Commands.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.Equal("Open a table first.", f.Commands.InsertRowReason);

        ui.Load("de");

        Assert.Equal("Öffne zuerst eine Tabelle.", f.Commands.InsertRowReason);
        Assert.All(ReasonNames, name => Assert.Contains(name, raised));

        var tab = EditableTab(f.Clipboard, texts);
        Select(f.Tabs, tab);
        Assert.Equal("Wähle mindestens eine Zeile.", f.Commands.DeleteRowsReason);
        tab.SelectedRows = [0];
        Assert.Equal("Zeilen löschen (Entf)", f.Commands.DeleteRowsToolTip);
    }

    [Fact] public void The_tool_tip_shows_the_action_with_its_key_and_the_reason_while_disabled()
    {
        var f = Make();
        var tab = EditableTab(f.Clipboard);
        Select(f.Tabs, tab);
        tab.SelectedRows = [0];

        Assert.Equal(["Insert row (Ins)", "Duplicate rows (Ctrl+D)", "Delete rows (Del)", "Copy (Ctrl+C)", "Paste (Ctrl+V)"],
            [f.Commands.InsertRowToolTip, f.Commands.DuplicateRowsToolTip, f.Commands.DeleteRowsToolTip, f.Commands.CopyToolTip, f.Commands.PasteToolTip]);

        tab.SelectedRows = [];
        Assert.Equal(Texts.ToolbarSelectRows, f.Commands.DeleteRowsToolTip);
        Assert.Equal("Insert row (Ins)", f.Commands.InsertRowToolTip);

        f.Tabs.CloseWithoutAsking(tab);
        Assert.Equal(Texts.ToolbarOpenTable, f.Commands.InsertRowToolTip);
    }

    [Fact] public void The_toolbar_tool_tips_of_the_document_buttons_name_the_keys()
    {
        Assert.Equal(["Save (Ctrl+S)", "Save all (Ctrl+Shift+S)", "Undo (Ctrl+Z)", "Redo (Ctrl+Y)"],
            [Texts.ToolbarSaveToolTip, Texts.ToolbarSaveAllToolTip, Texts.ToolbarUndoToolTip, Texts.ToolbarRedoToolTip]);
    }
}
