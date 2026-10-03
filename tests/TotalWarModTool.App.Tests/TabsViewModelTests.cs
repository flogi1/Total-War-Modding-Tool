using TotalWarModTool.Localization;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class TabsViewModelTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly PackSource A = new(@"D:\data\a.pack", "a.pack", PackOrigin.DataFolderMod, null, null, null, PackProblem.None, null);
    private static readonly PackSource B = A with { Path = @"D:\data\b.pack", Name = "b.pack" };
    private static OpenedEntry Text(string path) => new(path, EntryKind.Text, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, null, new TextContent("x", "UTF-8", false, 1));

    [Fact] public void Opening_an_open_entry_activates_its_tab()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("a.txt"), null); tabs.Add(A, Text("b.txt"), null);
        Assert.Equal("b.txt", tabs.SelectedTab!.Title);
        Assert.True(tabs.TryActivate(@"d:\DATA\a.pack", "A.TXT"));
        Assert.Equal(("a.txt", 2), (tabs.SelectedTab!.Title, tabs.Tabs.Count));
        Assert.False(tabs.TryActivate(B.Path, "a.txt"));
    }

    [Fact] public void Add_without_select_keeps_the_selection()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("a.txt"), null); tabs.Add(A, Text("b.txt"), null, select: false);
        Assert.Equal("a.txt", tabs.SelectedTab!.Title);
    }

    [Fact] public void Close_selects_the_right_neighbour_then_the_left_one()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("1"), null); tabs.Add(A, Text("2"), null); tabs.Add(A, Text("3"), null);
        tabs.SelectedTab = tabs.Tabs[1];
        tabs.SelectedTab.CloseCommand.Execute(null);
        Assert.Equal("3", tabs.SelectedTab!.Title);
        tabs.CloseSelectedCommand.Execute(null);
        Assert.Equal("1", tabs.SelectedTab!.Title);
        tabs.CloseSelectedCommand.Execute(null);
        Assert.Null(tabs.SelectedTab); Assert.Empty(tabs.Tabs);
        tabs.CloseSelectedCommand.Execute(null);                       // nothing selected: nothing happens
    }

    [Fact] public void MarkStale_marks_only_the_tabs_of_that_pack()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, Text("a.txt"), null); tabs.Add(B, Text("b.txt"), null);
        tabs.MarkStale(@"D:\DATA\A.PACK");
        Assert.Equal([true, false], tabs.Tabs.Select(t => t.IsStale));
    }

    private const string SchemaStaleText = "The schema changed. Close this tab and open the entry again to see it with the new schema.";
    private static readonly TableDefinition Def = new(0, [new("value", FieldType.I32, "I32", false, null)]);
    private static OpenedEntry TableEntry(string path) => new(path, EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, false,
        new DecodedTable(TableDecodeStatus.Ok, "x_tables", 0, false, Def, [[1]], null), null, null);
    private static OpenedEntry TableHint(string path) => new(path, EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, true, null, null, null);

    [Fact] public void MarkSchemaStale_marks_table_tabs_and_table_hints_but_not_other_tabs()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, TableEntry("db/x_tables/data__"), null); tabs.Add(A, TableHint("db/y_tables/data__"), null); tabs.Add(A, Text("a.txt"), null);
        tabs.MarkSchemaStale();
        Assert.Equal([true, true, false], tabs.Tabs.Select(t => t.IsStale));
        Assert.Equal([SchemaStaleText, SchemaStaleText, ""], tabs.Tabs.Select(t => t.StaleText));
    }

    [Fact] public void A_pack_change_wins_over_a_schema_change_in_either_order()
    {
        var tabs = new TabsViewModel(Texts);
        tabs.Add(A, TableEntry("db/x_tables/data__"), null); tabs.Add(B, TableEntry("db/x_tables/data__"), null);
        tabs.MarkSchemaStale(); tabs.MarkStale(A.Path);                           // schema first, then the pack
        tabs.MarkStale(B.Path); tabs.MarkSchemaStale();                           // pack first: the schema does not replace it
        Assert.Equal([Texts.TabStale, Texts.TabStale], tabs.Tabs.Select(t => t.StaleText));
    }

    [Fact] public void The_stale_text_follows_a_language_switch()
    {
        var ui = new UiText(System.IO.Path.Combine(RepoRoot.Find(), "locales")); var texts = new AppTexts(ui);
        var tabs = new TabsViewModel(texts);
        tabs.Add(A, TableEntry("db/x_tables/data__"), null);
        var raised = new List<string?>(); tabs.Tabs[0].PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        tabs.MarkSchemaStale();
        ui.Load("de");
        Assert.Equal("Das Schema hat sich geändert. Schließe diesen Tab und öffne den Eintrag erneut, um ihn mit dem neuen Schema zu sehen.", tabs.Tabs[0].StaleText);
        Assert.Contains(nameof(TabViewModel.StaleText), raised);
        tabs.MarkStale(A.Path);
        Assert.Equal("Das Pack wurde auf der Platte geändert. Dieser Tab zeigt den alten Inhalt.", tabs.Tabs[0].StaleText);
    }

    [Fact] public void Changed_is_raised_for_add_close_and_selection()
    {
        var tabs = new TabsViewModel(Texts); var changes = 0; tabs.Changed += (_, _) => changes++;
        tabs.Add(A, Text("a.txt"), null); var afterAdd = changes;
        tabs.Add(A, Text("b.txt"), null); tabs.SelectedTab = tabs.Tabs[0]; var afterSelect = changes;
        tabs.Close(tabs.Tabs[0]);
        Assert.True(afterAdd >= 1); Assert.True(afterSelect > afterAdd); Assert.True(changes > afterSelect);
    }
}
