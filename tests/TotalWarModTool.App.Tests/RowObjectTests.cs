using TotalWarModTool.Editing;
using TotalWarModTool.Localization;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.UndoRedo;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class RowObjectTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly TabSource Source = new(@"D:\data\a.pack", "a.pack");

    private static FieldDefinition F(string name, FieldType type, bool isKey = false) => new(name, type, type.ToString(), isKey, null);

    private static readonly TableDefinition KeyedDef = new(0, [F("a", FieldType.I32, true), F("b", FieldType.StringU8), F("c", FieldType.StringU8, true)]);
    private static readonly TableDefinition UnkeyedDef = new(0, [F("a", FieldType.I32), F("b", FieldType.StringU8)]);

    private sealed class FakeRowObjectOpener : IRowObjectOpener
    {
        public string? Reason { get; set; }
        public List<(RowsTabViewModel Tab, RowKey Key)> Shown { get; } = [];
        public event EventHandler? Changed;
        public string? DisabledReason(RowsTabViewModel tab) => Reason;
        public void Show(RowsTabViewModel tab, RowKey key) => Shown.Add((tab, key));
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private static DecodedTable Decoded(TableDefinition? definition, IReadOnlyList<object?[]> rows) =>
        new(TableDecodeStatus.Ok, "units_tables", 0, false, definition, rows, null);

    private static TableTabViewModel MakeTable(TableDefinition? definition, IReadOnlyList<object?[]> rows, IRowObjectOpener? opener = null) =>
        new(Source, "db/units_tables/data__", Texts, _ => { }, Decoded(definition, rows), null, opener);

    private static readonly IReadOnlyList<object?[]> KeyedRows = [[7, "n0", "x/y:z|w"], [8, "n1", "k1"], [9, "n2", "k2"]];

    [Fact] public void The_key_of_a_row_takes_every_key_column_in_schema_order()
    {
        var tab = MakeTable(KeyedDef, KeyedRows);

        Assert.Equal("units_tables", tab.TableName);
        Assert.Equal(new RowKey("units_tables", ["7", "x/y:z|w"]).Table, tab.KeyOfRow(0)!.Table);
        Assert.Equal(["7", "x/y:z|w"], tab.KeyOfRow(0)!.KeyValues);
        Assert.Null(tab.KeyOfRow(-1));
        Assert.Null(tab.KeyOfRow(3));

        tab.SelectedRows = [5, 2, 1];
        Assert.Equal(["8", "k1"], tab.SelectedRowKey()!.KeyValues);
        tab.SelectedRows = [5];
        Assert.Null(tab.SelectedRowKey());
    }

    [Fact] public void A_table_without_key_column_gives_no_key_and_says_why()
    {
        var opener = new FakeRowObjectOpener();
        var tab = MakeTable(UnkeyedDef, [[1, "x"]], opener);
        tab.SelectedRows = [0];

        Assert.Null(tab.KeyOfRow(0));
        Assert.Equal(Texts.ObjectsNoKeyField, tab.ShowObjectReason);
        Assert.False(tab.ShowObjectCommand.CanExecute(null));
        Assert.Null(MakeTable(null, [[1, "x"]], opener).KeyOfRow(0));
    }

    [Fact] public void Without_a_selected_row_the_reason_is_no_row()
    {
        var opener = new FakeRowObjectOpener();
        var tab = MakeTable(KeyedDef, KeyedRows, opener);

        Assert.Equal(Texts.ObjectsNoRowSelected, tab.ShowObjectReason);
        Assert.False(tab.ShowObjectCommand.CanExecute(null));

        tab.SelectedRows = [1];
        Assert.Null(tab.ShowObjectReason);
        Assert.True(tab.ShowObjectCommand.CanExecute(null));
    }

    [Fact] public void Without_an_opener_the_command_is_disabled_without_a_reason()
    {
        var tab = MakeTable(KeyedDef, KeyedRows);
        tab.SelectedRows = [1];

        Assert.Null(tab.ShowObjectReason);
        Assert.False(tab.ShowObjectCommand.CanExecute(null));
    }

    [Fact] public void The_opener_reason_wins_after_the_tab_reasons()
    {
        var opener = new FakeRowObjectOpener { Reason = "busy" };
        var tab = MakeTable(KeyedDef, KeyedRows, opener);
        tab.SelectedRows = [1];

        Assert.Equal("busy", tab.ShowObjectReason);
        Assert.False(tab.ShowObjectCommand.CanExecute(null));

        opener.Reason = null;
        opener.RaiseChanged();
        Assert.Null(tab.ShowObjectReason);
        Assert.True(tab.ShowObjectCommand.CanExecute(null));
        tab.ShowObjectCommand.Execute(null);

        var (shown, key) = Assert.Single(opener.Shown);
        Assert.Same(tab, shown);
        Assert.Equal("units_tables", key.Table);
        Assert.Equal(["8", "k1"], key.KeyValues);
    }

    [Fact] public void A_change_of_the_opener_refreshes_the_reason()
    {
        var opener = new FakeRowObjectOpener();
        var tab = MakeTable(KeyedDef, KeyedRows, opener);
        var raisedProperties = new List<string?>();
        var canExecuteChanged = 0;
        tab.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);
        tab.ShowObjectCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;

        opener.RaiseChanged();
        Assert.Contains(nameof(RowsTabViewModel.ShowObjectReason), raisedProperties);
        Assert.Equal(1, canExecuteChanged);

        tab.SelectedRows = [0];
        Assert.Equal(2, canExecuteChanged);

        tab.Dispose();
        raisedProperties.Clear();
        opener.RaiseChanged();
        Assert.DoesNotContain(nameof(RowsTabViewModel.ShowObjectReason), raisedProperties);
        Assert.Equal(2, canExecuteChanged);
    }

    [Fact] public void A_loc_tab_has_no_table_name_and_no_key()
    {
        var opener = new FakeRowObjectOpener();
        var tab = new LocTabViewModel(Source, "text/db/x.loc", Texts, _ => { }, [new LocRow("k", "t", true)], null, opener);
        tab.SelectedRows = [0];

        Assert.Null(tab.TableName);
        Assert.Null(tab.KeyOfRow(0));
        Assert.Null(tab.SelectedRowKey());
        Assert.False(tab.SelectRowByKey(["k"]));
        Assert.Equal(Texts.ObjectsNoKeyField, tab.ShowObjectReason);
    }

    [Fact] public void SelectRowByKey_requests_the_first_matching_row()
    {
        var tab = MakeTable(KeyedDef, KeyedRows);
        var requested = new List<IReadOnlyList<int>>();
        tab.SelectionRequested += (_, rows) => requested.Add(rows);

        Assert.True(tab.SelectRowByKey(["8", "k1"]));
        Assert.Equal([1], Assert.Single(requested));

        requested.Clear();
        Assert.False(tab.SelectRowByKey(["8", "other"]));
        Assert.False(tab.SelectRowByKey(["8"]));
        Assert.Empty(requested);
    }

    [Fact] public void An_editable_tab_names_its_table_and_raises_the_base_event()
    {
        var document = RowsDocument.ForTable("units_tables", 3, @"db\units_tables\data.tsv", KeyedDef, [[7, "n0", "k0"]]);
        var tab = new EditableRowsTabViewModel(TabSource.ForProject(Guid.NewGuid(), "My Mod"), "db/units_tables/data.tsv", Texts, _ => { },
            document, new ActionHistory());
        RowsTabViewModel asBase = tab;
        var requested = new List<IReadOnlyList<int>>();
        asBase.SelectionRequested += (_, rows) => requested.Add(rows);

        Assert.Equal("units_tables", tab.TableName);
        Assert.Equal(["7", "k0"], tab.KeyOfRow(0)!.KeyValues);

        tab.InsertRowCommand.Execute(null);

        Assert.Equal([1], Assert.Single(requested));
    }

    [Fact] public void An_editable_loc_tab_has_no_table_name()
    {
        var document = RowsDocument.ForLoc(@"text\db\x.loc.tsv", [new LocRow("k", "t", true)]);
        var tab = new EditableRowsTabViewModel(TabSource.ForProject(Guid.NewGuid(), "My Mod"), "text/db/x.loc.tsv", Texts, _ => { },
            document, new ActionHistory());

        Assert.Null(tab.TableName);
        Assert.Null(tab.KeyOfRow(0));
    }

    [Fact] public void AddTab_appends_and_selects()
    {
        var tabs = new TabsViewModel(Texts);
        var first = MakeTable(KeyedDef, KeyedRows);
        var second = MakeTable(KeyedDef, KeyedRows);

        tabs.AddTab(first);
        Assert.Same(first, tabs.SelectedTab);

        tabs.AddTab(second, select: false);
        Assert.Equal([first, second], tabs.Tabs);
        Assert.Same(first, tabs.SelectedTab);
    }

    [Fact] public void Tabs_made_by_the_list_get_its_object_opener()
    {
        var opener = new FakeRowObjectOpener();
        var tabs = new TabsViewModel(Texts) { ObjectOpener = opener };
        var entry = new OpenedEntry("db/units_tables/data__", EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, false,
            Decoded(KeyedDef, KeyedRows), null, null);

        tabs.Add(Source, entry, null);
        var tab = Assert.IsType<TableTabViewModel>(tabs.SelectedTab);
        tab.SelectedRows = [0];

        tab.ShowObjectCommand.Execute(null);
        Assert.Single(opener.Shown);
    }
}
