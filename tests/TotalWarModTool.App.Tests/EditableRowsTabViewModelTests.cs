using System.Collections.Specialized;
using System.ComponentModel;
using TotalWarModTool.Editing;
using TotalWarModTool.Localization;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.App.Tests;

public class EditableRowsTabViewModelTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly TabSource Source = TabSource.ForProject(ProjectId, "My Mod");

    // Column indexes of Def.
    private const int Key = 0, Cost = 1, Flag = 2, Ratio = 3, Big = 4, Small = 5, Single = 6, Name = 7, Colour = 8, ChannelR = 9;

    private static FieldDefinition F(string name, FieldType type, bool isKey = false) => new(name, type, type.ToString(), isKey, null);

    private static readonly TableDefinition Def = new(0,
    [
        F("key", FieldType.StringU8, true), F("cost", FieldType.I32), F("flag", FieldType.Boolean), F("ratio", FieldType.F64),
        F("big", FieldType.I64), F("small", FieldType.I16), F("single", FieldType.F32), F("name", FieldType.StringU16),
        F("colour", FieldType.ColourRgb), F("x_r", FieldType.I32), F("x_g", FieldType.I32), F("x_b", FieldType.I32),
    ]);

    private static object?[] Row(string key, int cost, bool flag = false) =>
        [key, cost, flag, 1.5, 5L, (short)2, 0.5f, "n", "FF0000", 1, 2, 3];

    private sealed record Fixture(EditableRowsTabViewModel Tab, RowsDocument Document, ActionHistory History);

    private static Fixture Make(int rows = 3, AppTexts? texts = null, IClipboard? clipboard = null)
    {
        var data = new List<object?[]>();
        for (var i = 0; i < rows; i++)
        {
            data.Add(Row("k" + i, i * 10, i % 2 == 1));
        }

        var document = RowsDocument.ForTable("units_tables", 3, @"db\units_tables\data.tsv", Def, data);
        var history = new ActionHistory();
        var tab = new EditableRowsTabViewModel(Source, @"db/units_tables/data.tsv", texts ?? Texts, _ => { }, document, history, clipboard);
        return new Fixture(tab, document, history);
    }

    [Fact] public void A_cell_shows_its_text_and_a_boolean_shows_a_bool()
    {
        var (tab, _, _) = Make();
        var row = tab.Rows[1];

        Assert.Equal("k1", row[Key]);
        Assert.Equal("10", row[Cost]);
        Assert.Equal(true, row[Flag]);
        Assert.Equal(false, tab.Rows[0][Flag]);
        Assert.Equal("1.5", row[Ratio]);
        Assert.Equal("FF0000", row[Colour]);
        Assert.Equal("1", row[ChannelR]);
        Assert.Equal(3, tab.Rows.Count);
        Assert.Equal(Def.Fields, tab.Columns);
        Assert.Equal((ProjectId, @"db\units_tables\data.tsv", "data.tsv"), (tab.ProjectId, tab.RelativeFile, tab.Title));
    }

    [Fact] public void A_source_that_is_not_a_project_is_refused()
    {
        var document = RowsDocument.ForTable("t", 0, "p", Def, []);
        Assert.Throws<ArgumentException>(() =>
            new EditableRowsTabViewModel(new TabSource(@"D:\data\a.pack", "a.pack"), "db/t/x.tsv", Texts, _ => { }, document, new ActionHistory()));
    }

    [Fact] public void Setting_a_valid_text_changes_the_document_through_the_history()
    {
        var (tab, document, history) = Make();
        var row = tab.Rows[1];
        var rowRaised = new List<string?>(); ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => rowRaised.Add(e.PropertyName);
        var tabRaised = new List<string?>(); tab.PropertyChanged += (_, e) => tabRaised.Add(e.PropertyName);
        var otherRaised = new List<string?>(); ((INotifyPropertyChanged)tab.Rows[0]).PropertyChanged += (_, e) => otherRaised.Add(e.PropertyName);
        Assert.False(tab.IsModified);

        row[Cost] = "42";

        Assert.Equal(42, document.Rows[1][Cost]);
        Assert.Equal("42", row[Cost]);
        Assert.True(history.CanUndo);
        Assert.True(tab.IsModified);
        Assert.Contains("Item[]", rowRaised);
        Assert.Contains(nameof(EditableRowsTabViewModel.IsModified), tabRaised);
        Assert.Empty(otherRaised);

        row[Flag] = false;                                             // a bool for a Boolean column
        Assert.Equal(false, document.Rows[1][Flag]);
        row[Flag] = "TRUE";                                            // and the texts CellText accepts
        Assert.Equal(true, document.Rows[1][Flag]);
    }

    [Fact] public void Setting_the_same_value_records_nothing()
    {
        var (tab, _, history) = Make();

        tab.Rows[1][Cost] = "10";
        tab.Rows[1][Flag] = true;
        tab.Rows[1][Key] = "k1";

        Assert.False(history.CanUndo);
        Assert.False(tab.IsModified);
    }

    [Fact] public void A_different_spelling_of_the_same_value_records_nothing()
    {
        var (tab, document, history) = Make();
        tab.Rows[0][Ratio] = "1";                                      // 1.5 becomes 1: one step
        Assert.True(history.CanUndo);

        tab.Rows[0][Ratio] = "1.0";                                    // 1.0 is the double 1 that is there
        tab.Rows[0][Colour] = "ff0000";                                // the colour is stored as FF0000
        tab.Rows[0][Single] = "5e-1";                                  // 0.5f is there

        history.Undo();                                                // exactly the one step
        Assert.False(history.CanUndo);
        Assert.Equal(1.5, document.Rows[0][Ratio]);
        Assert.False(tab.IsModified);
    }

    [Fact] public void A_valid_set_is_exactly_one_history_step()
    {
        var (tab, document, history) = Make();

        tab.Rows[1][Cost] = "42";
        history.Undo();

        Assert.Equal(10, document.Rows[1][Cost]);
        Assert.Equal("10", tab.Rows[1][Cost]);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.False(tab.IsModified);
    }

    [Fact] public void A_set_whose_change_event_fails_leaves_cell_rows_and_modified_state_as_before()
    {
        var (tab, document, history) = Make();
        document.Changed += (_, _) => throw new InvalidOperationException("subscriber failed");     // runs after the tab's handler

        Assert.Throws<InvalidOperationException>(() => tab.Rows[1][Cost] = "42");

        Assert.Equal(10, document.Rows[1][Cost]);
        Assert.Equal("10", tab.Rows[1][Cost]);
        Assert.Equal(3, tab.Rows.Count);
        Assert.False(tab.IsModified);
        Assert.False(history.CanUndo);
    }

    [Fact] public void Setting_an_invalid_text_throws_FormatException_with_the_message_and_changes_nothing()
    {
        var (tab, document, history) = Make();

        var ex = Assert.Throws<FormatException>(() => tab.Rows[1][Cost] = "abc");

        Assert.Equal(tab.CheckCell(Cost, "abc"), ex.Message);
        Assert.Contains("whole number from -2147483648 to 2147483647", ex.Message);
        Assert.Contains("not a 32-bit integer", ex.Message);
        Assert.Equal(10, document.Rows[1][Cost]);
        Assert.False(history.CanUndo);
        Assert.False(tab.IsModified);
    }

    [Fact] public void CheckCell_names_the_expected_type()
    {
        var (tab, _, _) = Make();

        Assert.Null(tab.CheckCell(Cost, "7"));
        Assert.Null(tab.CheckCell(Key, "anything"));
        Assert.Null(tab.CheckCell(Colour, "00FF00"));

        Assert.Contains("true or false", tab.CheckCell(Flag, "maybe"));
        Assert.Contains("whole number from -32768 to 32767", tab.CheckCell(Small, "40000"));
        Assert.Contains("whole number from -2147483648 to 2147483647", tab.CheckCell(Cost, "1.5"));
        Assert.Contains("whole number from -9223372036854775808 to 9223372036854775807", tab.CheckCell(Big, "x"));
        Assert.Contains("number with a decimal point", tab.CheckCell(Ratio, "x"));
        Assert.Contains("number with a decimal point", tab.CheckCell(Single, "x"));
        Assert.Contains("colour", tab.CheckCell(Colour, "zz"));
        Assert.Contains("text", tab.CheckCell(Name, "a\rb"));
        Assert.Contains("carriage return", tab.CheckCell(Name, "a\rb"));

        // A colour channel: the range 0 to 255, for a number outside it and for text that is no number.
        Assert.Contains("0 to 255", tab.CheckCell(ChannelR, "256"));
        Assert.Contains("0 to 255", tab.CheckCell(ChannelR, "-1"));
        Assert.Contains("0 to 255", tab.CheckCell(ChannelR, "x"));
        Assert.Null(tab.CheckCell(ChannelR, "255"));
    }

    // No Detail of RowsDocument.Check (that is: of CellText) embeds the input: the reasons are fixed English phrases. So DisplayText.Safe
    // cannot be made to fail by a cell text; the test pins what the message is made of and that the input does not reach it.
    [Fact] public void The_message_is_the_expected_text_plus_the_detail_and_never_the_input()
    {
        var (tab, document, _) = Make();
        var input = "a\u0001b";

        var message = tab.CheckCell(Cost, input);

        var detail = document.Check(Cost, input).Detail!;
        Assert.Equal(Texts.EditCheckMessage(Texts.EditExpectedInteger("-2147483648", "2147483647"), DisplayText.Safe(detail)), message);
        Assert.DoesNotContain('\u0001', message!);
        Assert.DoesNotContain("a\u0001b", message!);
        var unpaired = tab.CheckCell(Name, "x" + (char)0xD800);                  // a lone surrogate in a text column
        Assert.Equal(Texts.EditCheckMessage(Texts.EditExpectedText, DisplayText.Safe(document.Check(Name, "x" + (char)0xD800).Detail!)), unpaired);
    }

    [Fact] public void Undo_and_redo_update_the_rows_without_a_reset()
    {
        var (tab, _, history) = Make(4);
        var instances = tab.Rows.ToList();
        var actions = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)tab.Rows).CollectionChanged += (_, e) => actions.Add(e.Action);

        tab.SelectedRows = [1];
        tab.DeleteRowsCommand.Execute(null);
        Assert.Equal(["k0", "k2", "k3"], tab.Rows.Select(r => r[Key]));

        history.Undo();
        Assert.Equal(["k0", "k1", "k2", "k3"], tab.Rows.Select(r => r[Key]));
        Assert.Same(instances[0], tab.Rows[0]);                         // untouched rows keep their instances
        Assert.Same(instances[2], tab.Rows[2]);
        Assert.Same(instances[3], tab.Rows[3]);

        history.Redo();
        Assert.Equal(["k0", "k2", "k3"], tab.Rows.Select(r => r[Key]));
        Assert.Same(instances[3], tab.Rows[2]);

        history.Undo();
        tab.SelectedRows = [0, 3];
        tab.DuplicateRowsCommand.Execute(null);
        history.Undo();

        Assert.All(actions, a => Assert.True(a is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove, a.ToString()));
        Assert.NotEmpty(actions);
        Assert.Equal(["k0", "k1", "k2", "k3"], tab.Rows.Select(r => r[Key]));
    }

    // What the grid sees: the action and the index of every collection event, in order.
    private static List<string> Record(EditableRowsTabViewModel tab)
    {
        var events = new List<string>();
        ((INotifyCollectionChanged)tab.Rows).CollectionChanged += (_, e) => events.Add(e.Action switch
        {
            NotifyCollectionChangedAction.Add => "Add@" + e.NewStartingIndex,
            NotifyCollectionChangedAction.Remove => "Remove@" + e.OldStartingIndex,
            _ => e.Action.ToString(),
        });
        return events;
    }

    private static void AssertKeys(EditableRowsTabViewModel tab, params string[] keys)
    {
        Assert.Equal(keys, tab.Rows.Select(r => (string)r[Key]!));
        Assert.Equal(keys, tab.Document.Rows.Select(r => (string)r[Key]!));
    }

    [Fact] public void A_delete_with_a_gap_in_the_selection_removes_from_the_highest_index_down_and_undo_and_redo_follow()
    {
        var (tab, _, history) = Make(7);
        var before = tab.Rows.ToList();
        var events = Record(tab);

        tab.SelectedRows = [5, 0, 2];
        tab.DeleteRowsCommand.Execute(null);

        AssertKeys(tab, "k1", "k3", "k4", "k6");
        Assert.Equal(["Remove@5", "Remove@2", "Remove@0"], events);
        Assert.Equal([before[1], before[3], before[4], before[6]], tab.Rows);          // the untouched rows keep their instances

        events.Clear();
        history.Undo();
        AssertKeys(tab, "k0", "k1", "k2", "k3", "k4", "k5", "k6");
        Assert.Equal(["Add@0", "Add@2", "Add@5"], events);
        Assert.Same(before[1], tab.Rows[1]); Assert.Same(before[3], tab.Rows[3]);
        Assert.Same(before[4], tab.Rows[4]); Assert.Same(before[6], tab.Rows[6]);

        events.Clear();
        history.Redo();
        AssertKeys(tab, "k1", "k3", "k4", "k6");
        Assert.Equal(["Remove@5", "Remove@2", "Remove@0"], events);
        Assert.Same(before[1], tab.Rows[0]); Assert.Same(before[6], tab.Rows[3]);
    }

    [Fact] public void A_duplicate_with_a_gap_in_the_selection_inserts_one_block_after_the_last_selected_row()
    {
        var (tab, _, history) = Make(7);
        var before = tab.Rows.ToList();
        var events = Record(tab);

        tab.SelectedRows = [4, 1, 5];
        tab.DuplicateRowsCommand.Execute(null);

        AssertKeys(tab, "k0", "k1", "k2", "k3", "k4", "k5", "k1", "k4", "k5", "k6");
        Assert.Equal(["Add@6", "Add@7", "Add@8"], events);
        for (var i = 0; i < 6; i++)
        {
            Assert.Same(before[i], tab.Rows[i]);
        }

        Assert.Same(before[6], tab.Rows[9]);

        events.Clear();
        history.Undo();
        AssertKeys(tab, "k0", "k1", "k2", "k3", "k4", "k5", "k6");
        Assert.Equal(["Remove@8", "Remove@7", "Remove@6"], events);
        Assert.Equal(before, tab.Rows);

        events.Clear();
        history.Redo();
        AssertKeys(tab, "k0", "k1", "k2", "k3", "k4", "k5", "k1", "k4", "k5", "k6");
        Assert.Equal(["Add@6", "Add@7", "Add@8"], events);
        Assert.Same(before[6], tab.Rows[9]);
    }

    [Fact] public void An_insert_in_the_middle_adds_one_row_there_and_undo_and_redo_follow()
    {
        var (tab, _, history) = Make(7);
        var before = tab.Rows.ToList();
        var events = Record(tab);

        tab.SelectedRows = [2];
        tab.InsertRowCommand.Execute(null);

        AssertKeys(tab, "k0", "k1", "k2", "", "k3", "k4", "k5", "k6");
        Assert.Equal(["Add@3"], events);
        Assert.Equal(before.Take(3), tab.Rows.Take(3));
        Assert.Equal(before.Skip(3), tab.Rows.Skip(4));

        events.Clear();
        history.Undo();
        AssertKeys(tab, "k0", "k1", "k2", "k3", "k4", "k5", "k6");
        Assert.Equal(["Remove@3"], events);
        Assert.Equal(before, tab.Rows);

        events.Clear();
        history.Redo();
        AssertKeys(tab, "k0", "k1", "k2", "", "k3", "k4", "k5", "k6");
        Assert.Equal(["Add@3"], events);
        Assert.Equal(before.Skip(3), tab.Rows.Skip(4));
    }

    [Fact] public void A_change_of_up_to_the_threshold_is_followed_row_by_row_and_a_larger_one_with_one_reset()
    {
        var (tab, _, history) = Make(8);
        Assert.Equal(EditableRowsTabViewModel.DefaultLargeChangeRows, tab.LargeChangeRows);
        tab.LargeChangeRows = 3;
        var before = tab.Rows.ToList();
        var events = Record(tab);

        tab.SelectedRows = [0, 1, 2];                                   // 3 rows: not more than the threshold
        tab.DeleteRowsCommand.Execute(null);
        Assert.Equal(["Remove@2", "Remove@1", "Remove@0"], events);
        history.Undo();
        Assert.Equal(["Remove@2", "Remove@1", "Remove@0", "Add@0", "Add@1", "Add@2"], events);
        Assert.Equal(before.Skip(3), tab.Rows.Skip(3));

        events.Clear();
        tab.SelectedRows = [0, 1, 2, 3];                                // 4 rows: more
        tab.DeleteRowsCommand.Execute(null);
        Assert.Equal(["Reset"], events);
        AssertKeys(tab, "k4", "k5", "k6", "k7");
        Assert.Equal(before.Skip(4), tab.Rows);                         // every row that stays keeps its instance

        events.Clear();
        history.Undo();
        Assert.Equal(["Reset"], events);
        AssertKeys(tab, "k0", "k1", "k2", "k3", "k4", "k5", "k6", "k7");
        Assert.Equal(before.Skip(4), tab.Rows.Skip(4));
        Assert.Equal("units_tables, version 3, 8 row(s)", tab.Header);

        events.Clear();
        tab.SelectedRows = [4, 5, 6, 7];                                // an insert of 4 rows
        tab.DuplicateRowsCommand.Execute(null);
        Assert.Equal(["Reset"], events);
        AssertKeys(tab, "k0", "k1", "k2", "k3", "k4", "k5", "k6", "k7", "k4", "k5", "k6", "k7");
        Assert.Equal(before.Skip(4), tab.Rows.Skip(4).Take(4));
        Assert.Equal(12, tab.Rows.Count);

        events.Clear();
        tab.SelectedRows = [8, 9, 10];                                  // and 3 duplicates are row by row again
        tab.DuplicateRowsCommand.Execute(null);
        Assert.Equal(["Add@11", "Add@12", "Add@13"], events);
    }

    [Fact] public void Undo_of_a_cell_edit_tells_the_row()
    {
        var (tab, _, history) = Make();
        tab.Rows[2][Cost] = "5";
        var raised = new List<string?>(); ((INotifyPropertyChanged)tab.Rows[2]).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        history.Undo();

        Assert.Equal("20", tab.Rows[2][Cost]);
        Assert.Contains("Item[]", raised);
        Assert.False(tab.IsModified);
    }

    [Fact] public void Insert_delete_and_duplicate_use_the_selection()
    {
        var (tab, document, history) = Make(4);

        // Insert: a default row after the last selected row.
        tab.SelectedRows = [0, 2];
        tab.InsertRowCommand.Execute(null);
        Assert.Equal(5, tab.Rows.Count);
        Assert.Equal("", tab.Rows[3][Key]);
        Assert.Equal("0", tab.Rows[3][Cost]);
        Assert.Equal(false, tab.Rows[3][Flag]);
        Assert.Equal(["k0", "k1", "k2", "", "k3"], tab.Rows.Select(r => r[Key]));

        // Without a selection: at the end.
        tab.SelectedRows = [];
        tab.InsertRowCommand.Execute(null);
        Assert.Equal(6, tab.Rows.Count);
        Assert.Equal("", tab.Rows[5][Key]);

        // Duplicate: copies as one block after the last selected row.
        tab.SelectedRows = [0, 1];
        tab.DuplicateRowsCommand.Execute(null);
        Assert.Equal(["k0", "k1", "k0", "k1", "k2", "", "k3", ""], tab.Rows.Select(r => r[Key]));
        tab.Rows[2][Key] = "copy";
        Assert.Equal("k0", tab.Rows[0][Key]);                           // the copy is independent

        // Delete: the selected rows.
        tab.SelectedRows = [2, 3, 5];
        tab.DeleteRowsCommand.Execute(null);
        Assert.Equal(["k0", "k1", "k2", "k3", ""], tab.Rows.Select(r => r[Key]));
        Assert.Equal(5, document.Rows.Count);
        Assert.Equal(document.Rows.Select(r => r[Key]), tab.Rows.Select(r => r[Key]));
        Assert.True(history.CanUndo);
    }

    [Fact] public void A_stale_selection_is_clamped_or_ignored()
    {
        var (tab, _, _) = Make(2);

        tab.SelectedRows = [7, -1];
        tab.DeleteRowsCommand.Execute(null);
        tab.DuplicateRowsCommand.Execute(null);
        Assert.Equal(2, tab.Rows.Count);

        tab.InsertRowCommand.Execute(null);                             // no valid index: at the end
        Assert.Equal(3, tab.Rows.Count);
        Assert.Equal("", tab.Rows[2][Key]);

        tab.SelectedRows = [1, 99];                                     // the valid part counts
        tab.DeleteRowsCommand.Execute(null);
        Assert.Equal(2, tab.Rows.Count);
        Assert.Equal(["k0", ""], tab.Rows.Select(r => r[Key]));
    }

    [Fact] public void Delete_and_duplicate_are_disabled_without_a_selection()
    {
        var (tab, _, _) = Make();
        var raised = 0; tab.DeleteRowsCommand.CanExecuteChanged += (_, _) => raised++;

        Assert.False(tab.DeleteRowsCommand.CanExecute(null));
        Assert.False(tab.DuplicateRowsCommand.CanExecute(null));
        Assert.True(tab.InsertRowCommand.CanExecute(null));

        tab.SelectedRows = [1];

        Assert.True(tab.DeleteRowsCommand.CanExecute(null));
        Assert.True(tab.DuplicateRowsCommand.CanExecute(null));
        Assert.True(raised > 0);

        tab.SelectedRows = [];
        Assert.False(tab.DeleteRowsCommand.CanExecute(null));
    }

    [Fact] public void The_row_commands_re_evaluate_when_the_row_count_changes()
    {
        var (tab, _, history) = Make(2);
        var delete = 0; var duplicate = 0;
        tab.DeleteRowsCommand.CanExecuteChanged += (_, _) => delete++;
        tab.DuplicateRowsCommand.CanExecuteChanged += (_, _) => duplicate++;
        tab.SelectedRows = [1];
        Assert.True(tab.DeleteRowsCommand.CanExecute(null));
        var (d, u) = (delete, duplicate);

        tab.DeleteRowsCommand.Execute(null);                            // the selection is stale now: nothing valid is left selected
        Assert.True(delete > d && duplicate > u);
        Assert.False(tab.DeleteRowsCommand.CanExecute(null));
        Assert.False(tab.DuplicateRowsCommand.CanExecute(null));

        (d, u) = (delete, duplicate);
        history.Undo();                                                 // the row is back: the stale index is valid again
        Assert.True(delete > d && duplicate > u);
        Assert.True(tab.DeleteRowsCommand.CanExecute(null));

        (d, u) = (delete, duplicate);
        tab.Rows[0][Cost] = "9";                                        // a cell change does not change what a command can act on
        Assert.Equal((d, u), (delete, duplicate));
    }

    [Fact] public void The_header_follows_the_row_count_and_the_language()
    {
        var ui = new UiText(System.IO.Path.Combine(RepoRoot.Find(), "locales")); var texts = new AppTexts(ui);
        var (tab, _, history) = Make(3, texts);
        var raised = new List<string?>(); tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.Equal("units_tables, version 3, 3 row(s)", tab.Header);

        tab.SelectedRows = [0];
        tab.InsertRowCommand.Execute(null);
        Assert.Equal("units_tables, version 3, 4 row(s)", tab.Header);
        Assert.Contains(nameof(EditableRowsTabViewModel.Header), raised);

        history.Undo();
        Assert.Equal("units_tables, version 3, 3 row(s)", tab.Header);

        ui.Load("de");
        Assert.Equal("units_tables, Version 3, 3 Zeile(n)", tab.Header);
    }

    [Fact] public void The_header_of_a_loc_file_shows_the_row_count()
    {
        var document = RowsDocument.ForLoc(@"text\a.loc.tsv", [new LocRow("k", "t", false)]);
        var tab = new EditableRowsTabViewModel(Source, "text/a.loc.tsv", Texts, _ => { }, document, new ActionHistory());

        Assert.Equal("1 row(s)", tab.Header);
        Assert.False(tab.IsTableEntry);
        Assert.Equal(["k", "t", false], [tab.Rows[0][0], tab.Rows[0][1], tab.Rows[0][2]]);
    }

    [Fact] public void A_table_document_is_a_table_entry()
    {
        var (tab, _, _) = Make();
        Assert.True(tab.IsTableEntry);
    }

    [Fact] public void The_text_view_shows_the_current_state_after_an_edit()
    {
        var (tab, document, _) = Make();
        Assert.Null(tab.Text);

        tab.ShowText = true;
        Assert.Equal(document.ToText().Text, tab.Text);
        tab.ShowText = false;

        // A change drops the text; it is built again on the next switch to "Text", from the state at that time.
        tab.Rows[0][Key] = "edited";
        Assert.Null(tab.Text);
        tab.ShowText = true;
        Assert.Contains("edited", tab.Text);
        Assert.Equal(document.ToText().Text, tab.Text);
    }

    [Fact] public void A_change_while_the_text_shows_switches_to_the_grid_without_building_the_text()
    {
        var (tab, document, history) = Make();
        tab.ShowText = true;
        Assert.NotNull(tab.Text);
        var raised = new List<string?>(); tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        tab.Rows[1][Key] = "second";                                    // or an undo or redo: any change of the document

        Assert.True(tab.ShowGrid);
        Assert.False(tab.ShowText);
        Assert.Null(tab.Text);                                          // not built by the change: ToText ran in no event handler
        Assert.Null(tab.TextNotice);
        Assert.Contains(nameof(RowsTabViewModel.ShowText), raised);
        Assert.Contains(nameof(RowsTabViewModel.ShowGrid), raised);
        Assert.Contains(nameof(RowsTabViewModel.Text), raised);

        tab.ShowText = true;
        Assert.Contains("second", tab.Text);
        Assert.DoesNotContain("k1", tab.Text);

        history.Undo();                                                 // an undo from the text view: back to the grid, the state is shown there
        Assert.True(tab.ShowGrid);
        Assert.Equal("k1", tab.Rows[1][Key]);
        tab.ShowText = true;
        Assert.Equal(document.ToText().Text, tab.Text);
        Assert.Contains("k1", tab.Text);
    }

    [Fact] public void A_cut_text_keeps_its_notice_until_the_next_change()
    {
        var (tab, _, history) = Make();
        tab.MaxChars = 10;
        tab.ShowText = true;
        Assert.Equal(Texts.TabTextViewTruncated, tab.TextNotice);

        tab.Rows[0][Cost] = "1";                                        // the change drops text and notice

        Assert.Null(tab.TextNotice);
        tab.MaxChars = RowsTabViewModel.MaxTextChars;
        history.Undo();
        tab.ShowText = true;
        Assert.Null(tab.TextNotice);
        Assert.NotNull(tab.Text);
    }

    [Fact] public void IsModified_follows_the_document()
    {
        var (tab, document, history) = Make();
        var raised = new List<bool>(); tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditableRowsTabViewModel.IsModified))
            {
                raised.Add(tab.IsModified);
            }
        };

        tab.Rows[0][Cost] = "1";
        Assert.True(tab.IsModified);
        document.MarkSaved();
        Assert.False(tab.IsModified);
        history.Undo();
        Assert.True(tab.IsModified);
        history.Redo();
        Assert.False(tab.IsModified);

        Assert.Equal([true, false, true, false], raised);
    }

    [Fact] public void ReplaceDocument_replaces_the_rows_with_one_reset()
    {
        var (tab, _, _) = Make();
        var other = RowsDocument.ForTable("units_tables", 3, @"db\units_tables\data.tsv", Def, [Row("z", 99)]);
        var actions = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)tab.Rows).CollectionChanged += (_, e) => actions.Add(e.Action);
        var raised = new List<string?>(); tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        tab.ReplaceDocument(other);

        Assert.Equal([NotifyCollectionChangedAction.Reset], actions);
        Assert.Same(other, tab.Document);
        Assert.Equal(["z"], tab.Rows.Select(r => r[Key]));
        Assert.Equal("units_tables, version 3, 1 row(s)", tab.Header);
        Assert.Contains(nameof(EditableRowsTabViewModel.Document), raised);
        Assert.Contains(nameof(EditableRowsTabViewModel.IsModified), raised);

        tab.Rows[0][Cost] = "5";                                        // the new document is the one that changes
        Assert.Equal(5, other.Rows[0][Cost]);
        Assert.True(tab.IsModified);
    }

    [Fact] public void The_old_document_no_longer_reaches_the_tab_after_ReplaceDocument()
    {
        var (tab, oldDocument, _) = Make();
        var other = RowsDocument.ForTable("units_tables", 3, "p", Def, [Row("z", 99)]);
        tab.ReplaceDocument(other);

        oldDocument.SetCell(0, Cost, 1).Do();

        Assert.Single(tab.Rows);
        Assert.Equal("99", tab.Rows[0][Cost]);
    }

    [Fact] public void Dispose_stops_listening_to_the_document_and_the_texts()
    {
        var ui = new UiText(System.IO.Path.Combine(RepoRoot.Find(), "locales")); var texts = new AppTexts(ui);
        var (tab, document, _) = Make(3, texts);

        tab.Dispose();
        tab.Dispose();
        document.InsertRows(0, [document.NewRow()]).Do();
        ui.Load("de");

        Assert.Equal(3, tab.Rows.Count);
        Assert.Equal("units_tables, version 3, 3 row(s)", tab.Header);
    }

    [Fact] public void ReplaceDocument_after_Dispose_does_nothing()
    {
        var (tab, document, _) = Make();
        var other = RowsDocument.ForTable("units_tables", 3, "p", Def, [Row("z", 99)]);
        tab.Dispose();
        var actions = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)tab.Rows).CollectionChanged += (_, e) => actions.Add(e.Action);

        tab.ReplaceDocument(other);
        other.InsertRows(0, [other.NewRow()]).Do();                     // the new document was not subscribed to

        Assert.Empty(actions);
        Assert.Same(document, tab.Document);
        Assert.Equal(3, tab.Rows.Count);
    }

    [Fact] public void An_edit_of_a_row_that_left_the_tab_does_nothing()
    {
        var (tab, _, history) = Make();
        var row = tab.Rows[0];
        tab.ReplaceDocument(RowsDocument.ForTable("units_tables", 3, "p", Def, [Row("z", 99)]));

        row[Cost] = "5";

        Assert.False(history.CanUndo);
        Assert.False(tab.IsModified);
    }

    // ---- copy and paste ----------------------------------------------------------------------------------------------------------

    private static string Header => string.Join("\t", Def.Fields.Select(f => f.Name));

    private static string TextOf(params object?[][] rows) => ClipboardRows.Write(Def.Fields, rows).Text!;

    [Fact] public void Copy_puts_the_selected_rows_with_the_header_on_the_clipboard()
    {
        var clipboard = new FakeClipboard { Text = "before" };
        var (tab, document, _) = Make(clipboard: clipboard);
        tab.SelectedRows = [2, 0];                                      // the order of the rows, not of the selection

        Assert.True(tab.CopyRowsCommand.CanExecute(null));
        tab.CopyRowsCommand.Execute(null);

        Assert.Equal(TextOf(document.Rows[0], document.Rows[2]), clipboard.Text);
        Assert.StartsWith(Header + "\n", clipboard.Text);
        Assert.Equal(3, clipboard.Text!.Split('\n').Length - 1);        // the header and two rows, every line ends with a line feed
        Assert.Null(tab.Notice);
        Assert.Equal(1, clipboard.Sets);
    }

    [Fact] public void Copy_needs_a_selection_and_a_clipboard()
    {
        var clipboard = new FakeClipboard();
        var (tab, _, _) = Make(clipboard: clipboard);
        var (without, _, _) = Make();
        var raised = 0;
        tab.CopyRowsCommand.CanExecuteChanged += (_, _) => raised++;

        Assert.False(tab.CopyRowsCommand.CanExecute(null));             // nothing selected
        tab.SelectedRows = [1];
        Assert.True(tab.CopyRowsCommand.CanExecute(null));
        Assert.True(raised > 0);
        tab.SelectedRows = [77];                                        // a stale index is no selection
        Assert.False(tab.CopyRowsCommand.CanExecute(null));

        without.SelectedRows = [1];
        Assert.False(without.CopyRowsCommand.CanExecute(null));
        Assert.False(without.PasteRowsCommand.CanExecute(null));
        without.CopyRowsCommand.Execute(null);                          // does nothing, does not throw
        without.PasteRowsCommand.Execute(null);
        Assert.Null(clipboard.Text);
    }

    [Fact] public void More_rows_than_the_limit_are_refused_with_a_notice()
    {
        var clipboard = new FakeClipboard { Text = "before" };
        var (tab, _, _) = Make(clipboard: clipboard);
        tab.MaxCopyRows = 2;
        tab.SelectedRows = [0, 1, 2];

        tab.CopyRowsCommand.Execute(null);

        Assert.Equal(Texts.GridCopyTooManyRows(3, 2), tab.Notice);
        Assert.Equal("before", clipboard.Text);
    }

    [Fact] public void A_row_that_the_format_cannot_carry_is_refused_with_a_masked_notice()
    {
        // Two backslashes and an n in a text cell are the escape sequence of a line feed: the dialect would read the cell back as another text.
        var escape = new string((char)0x5C, 2) + "n";
        var document = RowsDocument.ForTable("units_tables", 3, "p", Def, [Row("a" + escape + "b" + (char)7, 1)]);
        var clipboard = new FakeClipboard { Text = "before" };
        var tab = new EditableRowsTabViewModel(Source, "db/units_tables/data.tsv", Texts, _ => { }, document, new ActionHistory(), clipboard);
        tab.SelectedRows = [0];

        tab.CopyRowsCommand.Execute(null);

        Assert.StartsWith("The selected rows cannot be copied as text.", tab.Notice);
        Assert.DoesNotContain((char)7, tab.Notice!);
        Assert.Equal("before", clipboard.Text);
    }

    [Fact] public void Paste_inserts_the_rows_as_one_undo_step_after_the_selection()
    {
        var clipboard = new FakeClipboard { Text = TextOf(Row("p", 1), Row("q", 2)) };
        var (tab, document, history) = Make(clipboard: clipboard);
        var requested = new List<IReadOnlyList<int>>();
        tab.SelectionRequested += (_, rows) => requested.Add(rows);
        tab.SelectedRows = [0];

        tab.PasteRowsCommand.Execute(null);

        Assert.Equal(["k0", "p", "q", "k1", "k2"], document.Rows.Select(r => (string)r[Key]!));
        Assert.Equal(["k0", "p", "q", "k1", "k2"], tab.Rows.Select(r => (string)r[Key]!));
        Assert.Equal([1, 2], Assert.Single(requested));
        Assert.Null(tab.Notice);
        Assert.True(tab.IsModified);

        history.Undo();                                                 // exactly one step
        Assert.Equal(3, document.Rows.Count);
        Assert.False(history.CanUndo);
        Assert.False(tab.IsModified);
    }

    [Fact] public void Paste_without_a_selection_inserts_at_the_end()
    {
        var clipboard = new FakeClipboard { Text = TextOf(Row("p", 1)) };
        var (tab, document, _) = Make(clipboard: clipboard);
        var requested = new List<IReadOnlyList<int>>();
        tab.SelectionRequested += (_, rows) => requested.Add(rows);

        tab.PasteRowsCommand.Execute(null);

        Assert.Equal("p", document.Rows[^1][Key]);
        Assert.Equal(4, document.Rows.Count);
        Assert.Equal([3], Assert.Single(requested));
    }

    [Fact] public void A_refused_paste_changes_nothing_and_says_why()
    {
        var clipboard = new FakeClipboard { Text = "nothing\tof\tthese\nx\ty\tz\n" };
        var (tab, document, history) = Make(clipboard: clipboard);
        var requested = 0;
        tab.SelectionRequested += (_, _) => requested++;

        tab.PasteRowsCommand.Execute(null);                             // no column of the table is named

        Assert.StartsWith("Nothing was pasted. Line 1 of the text on the clipboard: " + Texts.GridPasteProblemNoKnownColumn, tab.Notice);
        Assert.False(history.CanUndo);
        Assert.False(tab.IsModified);
        Assert.Equal(3, document.Rows.Count);

        clipboard.Text = "key\tcost\nk\t1\nk\n";                          // line 3 has one cell
        tab.PasteRowsCommand.Execute(null);
        Assert.Equal(Texts.GridPasteRefusedAtLine(3, Texts.GridPasteProblemWrongCellCount, "Line 3 has 1 cell, expected 2"), tab.Notice);

        clipboard.Text = "key\tcost\nk\tabc\n";                          // a value the column does not accept
        tab.PasteRowsCommand.Execute(null);
        Assert.StartsWith("Nothing was pasted. Line 2 of the text on the clipboard: " + Texts.GridPasteProblemInvalidValue, tab.Notice);
        Assert.Contains("column 'cost'", tab.Notice);

        clipboard.Text = "key\tcost\n";                                  // a header and no rows: no line to name
        tab.PasteRowsCommand.Execute(null);
        Assert.StartsWith("Nothing was pasted. " + Texts.GridPasteProblemEmpty, tab.Notice);

        Assert.False(history.CanUndo);
        Assert.Equal(3, document.Rows.Count);
        Assert.Equal(0, requested);
    }

    [Fact] public void A_paste_names_missing_and_dropped_columns()
    {
        var clipboard = new FakeClipboard { Text = "key\tcost\tstranger\tother\np\t7\tzzz\tyyy\n" };
        var (tab, document, _) = Make(clipboard: clipboard);

        tab.PasteRowsCommand.Execute(null);

        var missing = string.Join(", ", Def.Fields.Select(f => f.Name).Where(name => name is not ("key" or "cost")));
        Assert.Equal(Texts.GridPasteMissingColumns(missing) + " " + Texts.GridPasteExtraColumns("stranger, other"), tab.Notice);
        Assert.Equal(("p", 7, false), (document.Rows[3][Key], document.Rows[3][Cost], document.Rows[3][Flag]));     // missing columns have their default
        Assert.Equal(4, document.Rows.Count);

        // The notice is about the rows of that moment: the next change of the document takes it away.
        tab.Rows[0][Cost] = "5";
        Assert.Null(tab.Notice);
    }

    [Fact] public void An_unavailable_clipboard_is_reported_and_nothing_changes()
    {
        var clipboard = new FakeClipboard { Text = TextOf(Row("p", 1)), Available = false };
        var (tab, document, history) = Make(clipboard: clipboard);
        tab.SelectedRows = [0, 1];

        tab.PasteRowsCommand.Execute(null);
        Assert.Equal(Texts.GridPasteNoText, tab.Notice);
        Assert.Equal(3, document.Rows.Count);
        Assert.False(history.CanUndo);

        tab.CopyRowsCommand.Execute(null);
        Assert.Equal(Texts.GridCopyClipboardFailed, tab.Notice);
        Assert.Equal(0, clipboard.Sets);

        clipboard.Available = true;                                     // the next try works and the notice goes
        tab.CopyRowsCommand.Execute(null);
        Assert.Null(tab.Notice);
        Assert.Equal(1, clipboard.Sets);
    }

    [Fact] public void A_notice_follows_a_language_switch()
    {
        var ui = new UiText(Path.Combine(RepoRoot.Find(), "locales"));
        var texts = new AppTexts(ui);
        var (tab, _, _) = Make(texts: texts, clipboard: new FakeClipboard { Available = false });

        tab.PasteRowsCommand.Execute(null);
        Assert.Equal("There is no text on the clipboard, or the clipboard cannot be read right now.", tab.Notice);

        ui.Load("de");
        Assert.Equal("In der Zwischenablage ist kein Text, oder die Zwischenablage lässt sich gerade nicht lesen.", tab.Notice);
    }

    // ---- row commands and the selection they ask for -------------------------------------------------------------------------------

    private static List<IReadOnlyList<int>> Requests(EditableRowsTabViewModel tab)
    {
        var requested = new List<IReadOnlyList<int>>();
        tab.SelectionRequested += (_, rows) => requested.Add(rows);
        return requested;
    }

    [Fact] public void Insert_and_duplicate_ask_for_the_new_rows_to_be_selected()
    {
        var (tab, document, _) = Make(rows: 5);
        var requested = Requests(tab);

        tab.SelectedRows = [1];
        tab.InsertRowCommand.Execute(null);                             // after the selected row
        Assert.Equal([2], requested[^1]);
        Assert.Equal(6, document.Rows.Count);

        tab.SelectedRows = [4, 0];
        tab.DuplicateRowsCommand.Execute(null);                         // a block of two after the last selected row
        Assert.Equal([5, 6], requested[^1]);
        Assert.Equal(8, document.Rows.Count);

        tab.SelectedRows = [];
        tab.InsertRowCommand.Execute(null);                             // without a selection: at the end
        Assert.Equal([8], requested[^1]);
        Assert.Equal(3, requested.Count);
    }

    [Fact] public void Delete_asks_for_the_row_that_took_the_place_of_the_first_deleted_one()
    {
        var (tab, document, _) = Make(rows: 6);
        var requested = Requests(tab);

        tab.SelectedRows = [1, 2];
        tab.DeleteRowsCommand.Execute(null);
        Assert.Equal([1], requested[^1]);                               // the old row 3 is row 1 now
        Assert.Equal(4, document.Rows.Count);

        tab.SelectedRows = [2, 3];                                      // the last rows: the last row that is left
        tab.DeleteRowsCommand.Execute(null);
        Assert.Equal([1], requested[^1]);
        Assert.Equal(2, document.Rows.Count);

        tab.SelectedRows = [0, 1];                                      // every row: nothing to select
        tab.DeleteRowsCommand.Execute(null);
        Assert.Empty(requested[^1]);
        Assert.Empty(document.Rows);
    }

    // ---- the cell that is being edited ---------------------------------------------------------------------------------------------

    [Fact] public void Committing_a_pending_edit_asks_the_view_and_without_a_view_nothing_is_pending()
    {
        var (tab, _, _) = Make();
        Assert.True(tab.TryCommitPendingEdit());
        Assert.False(tab.HasPendingEdit);

        var calls = 0;
        var result = true;
        tab.CommitPendingEdit = () =>
        {
            calls++;
            return result;
        };

        Assert.True(tab.TryCommitPendingEdit());
        result = false;
        Assert.False(tab.TryCommitPendingEdit());
        Assert.Equal(2, calls);
    }

    [Fact] public void The_pending_edit_and_the_validation_message_are_set_by_the_view_and_raise_changes()
    {
        var (tab, _, _) = Make();
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        tab.SetPendingEdit(true);
        tab.SetValidationMessage("not a number");
        Assert.Equal((true, "not a number"), (tab.HasPendingEdit, tab.ValidationMessage));
        Assert.Contains(nameof(EditableRowsTabViewModel.HasPendingEdit), raised);
        Assert.Contains(nameof(EditableRowsTabViewModel.ValidationMessage), raised);

        tab.SetPendingEdit(false);                                      // the edit is over: the message goes with it
        Assert.Equal((false, null), (tab.HasPendingEdit, tab.ValidationMessage));
    }
}
