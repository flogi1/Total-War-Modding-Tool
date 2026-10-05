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

    private static Fixture Make(int rows = 3, AppTexts? texts = null)
    {
        var data = new List<object?[]>();
        for (var i = 0; i < rows; i++)
        {
            data.Add(Row("k" + i, i * 10, i % 2 == 1));
        }

        var document = RowsDocument.ForTable("units_tables", 3, @"db\units_tables\data.tsv", Def, data);
        var history = new ActionHistory();
        var tab = new EditableRowsTabViewModel(Source, @"db/units_tables/data.tsv", texts ?? Texts, _ => { }, document, history);
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

    [Fact] public void A_technical_detail_with_control_characters_is_made_safe()
    {
        var (tab, _, _) = Make();

        var message = tab.CheckCell(Cost, "a\u0001b");

        Assert.NotNull(message);
        Assert.DoesNotContain('\u0001', message);
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

        // Not showing: the text is built on the next switch to "Text", from the state at that time.
        tab.ShowText = true;
        Assert.Equal(document.ToText().Text, tab.Text);
        tab.ShowText = false;

        tab.Rows[0][Key] = "edited";
        Assert.Null(tab.Text);
        tab.ShowText = true;
        Assert.Contains("edited", tab.Text);
        Assert.Equal(document.ToText().Text, tab.Text);

        // Showing: a change from outside (an undo of the global history) shows at once.
        tab.Rows[1][Key] = "second";
        Assert.Contains("second", tab.Text);
        Assert.DoesNotContain("k1", tab.Text);
    }

    [Fact] public void A_cut_text_shows_a_notice_that_goes_with_the_next_rebuild()
    {
        var (tab, _, history) = Make();
        tab.ShowText = true;
        Assert.Null(tab.TextNotice);
        tab.MaxChars = 10;

        tab.Rows[0][Cost] = "1";                                        // rebuilt with the small limit

        Assert.Equal(Texts.TabTextViewTruncated, tab.TextNotice);
        tab.MaxChars = RowsTabViewModel.MaxTextChars;
        history.Undo();
        Assert.Null(tab.TextNotice);
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

    [Fact] public void An_edit_of_a_row_that_left_the_tab_does_nothing()
    {
        var (tab, _, history) = Make();
        var row = tab.Rows[0];
        tab.ReplaceDocument(RowsDocument.ForTable("units_tables", 3, "p", Def, [Row("z", 99)]));

        row[Cost] = "5";

        Assert.False(history.CanUndo);
        Assert.False(tab.IsModified);
    }
}
