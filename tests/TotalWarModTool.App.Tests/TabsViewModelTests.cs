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

    [Fact] public void A_project_source_is_a_tab_source_of_its_own()
    {
        var id = Guid.NewGuid();
        var source = TabSource.ForProject(id, "My Mod");
        Assert.Equal("project:" + id.ToString("D"), source.Key);
        Assert.True(TabSource.TryGetProjectId(source.Key, out var back));
        Assert.Equal(id, back);
        Assert.False(TabSource.TryGetProjectId(@"D:\a.pack", out _));
        Assert.False(TabSource.TryGetProjectId("project:not-a-guid", out _));

        var tabs = new TabsViewModel(Texts);
        tabs.Add(source, Text("scripts/a.txt"), null);
        Assert.True(tabs.TryActivate("project:" + id.ToString("D"), "scripts/a.txt"));
        Assert.Equal("My Mod: scripts/a.txt", tabs.SelectedTab!.ToolTip);
        Assert.False(tabs.TryActivate(A.Path, "scripts/a.txt"));
    }

    [Fact] public void A_pack_source_converts_to_a_tab_source_with_its_path_and_name()
    {
        TabSource source = A;
        Assert.Equal((A.Path, A.Name), (source.Key, source.Name));
    }

    private static readonly TableDefinition UnitsDef = new(2, [new("key", FieldType.StringU8, "StringU8", true, null), new("cost", FieldType.I32, "I32", false, null)]);

    private static TableTabViewModel UnitsTable(AppTexts texts, IReadOnlyList<object?[]>? rows = null)
    {
        var tabs = new TabsViewModel(texts);
        var table = new DecodedTable(TableDecodeStatus.Ok, "units_tables", 2, false, UnitsDef, rows ?? [["a", 1]], null);
        tabs.Add(A, new OpenedEntry("db/units_tables/x", EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, table, null, null), null);
        return Assert.IsType<TableTabViewModel>(tabs.SelectedTab);
    }

    [Fact] public void The_text_view_of_a_table_is_built_on_first_use()
    {
        var tab = UnitsTable(Texts);
        Assert.Null(tab.Text);
        Assert.True(tab.ShowGrid);
        var raised = new List<string?>(); tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        tab.ShowText = true;

        Assert.Equal("key\tcost\n#units_tables;2;db/units_tables/x\t\na\t1\n", tab.Text);
        Assert.False(tab.ShowGrid);
        Assert.Null(tab.TextNotice);
        Assert.Contains(nameof(TableTabViewModel.ShowText), raised);
        Assert.Contains(nameof(TableTabViewModel.ShowGrid), raised);
        Assert.Contains(nameof(TableTabViewModel.Text), raised);
        Assert.Contains(nameof(TableTabViewModel.TextNotice), raised);

        var text = tab.Text; tab.ShowText = false; tab.ShowText = true;
        Assert.Same(text, tab.Text);                                       // built once
        Assert.True(tab.ShowText == !tab.ShowGrid);
    }

    [Fact] public void A_table_the_dialect_cannot_carry_has_a_notice_instead_of_text()
    {
        var def = new TableDefinition(0, [new("r", FieldType.I32, "I32", false, null), new("g", FieldType.I32, "I32", false, null), new("b", FieldType.I32, "I32", false, null)]);
        var tabs = new TabsViewModel(Texts);
        var table = new DecodedTable(TableDecodeStatus.Ok, "colours_tables", 0, false, def, [[300, 1, 2]], null);
        tabs.Add(A, new OpenedEntry("db/colours_tables/x", EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, table, null, null), null);
        var tab = Assert.IsType<TableTabViewModel>(tabs.SelectedTab);
        var detail = TableTsv.Write("colours_tables", 0, "db/colours_tables/x", def, table.Rows).Detail;
        Assert.NotNull(detail);

        tab.ShowText = true;

        Assert.Null(tab.Text);
        Assert.Equal(Texts.TabTextViewUnavailable(detail), tab.TextNotice);
        Assert.Contains(detail, tab.TextNotice);
    }

    [Fact] public void A_table_with_a_name_the_dialect_rejects_has_a_notice_and_does_not_throw()
    {
        var tabs = new TabsViewModel(Texts);
        var table = new DecodedTable(TableDecodeStatus.Ok, "bad;name", 2, false, UnitsDef, [["a", 1]], null);
        tabs.Add(A, new OpenedEntry("db/x/y", EntryKind.Table, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, table, null, null), null);
        var tab = Assert.IsType<TableTabViewModel>(tabs.SelectedTab);

        tab.ShowText = true;

        Assert.Null(tab.Text);
        Assert.StartsWith("This table cannot be shown as text: The table name must not contain", tab.TextNotice);
        Assert.DoesNotContain("'tableName'", tab.TextNotice);          // the suffix " (Parameter 'tableName')" of .NET is not shown
        Assert.DoesNotContain("Parameter", tab.TextNotice);
    }

    [Fact] public void A_table_without_a_definition_has_a_notice_and_no_text()
    {
        var tabs = new TabsViewModel(Texts);
        // A decoded table that carries rows but no definition cannot come from the factory; the tab still has to survive it.
        var table = new DecodedTable(TableDecodeStatus.Ok, "units_tables", 2, false, null, [], null);
        var tab = new TableTabViewModel(A, "db/units_tables/x", Texts, _ => { }, table);

        tab.ShowText = true;

        Assert.Null(tab.Text);
        Assert.Equal("This table cannot be shown as text: The table has no definition.", tab.TextNotice);
        Assert.Equal(Texts.TabTextViewUnavailable(Texts.TabTextViewNoDefinition), tab.TextNotice);
        tabs.Close(tab);
    }

    [Fact] public void The_no_definition_notice_follows_a_language_switch()
    {
        var ui = new UiText(System.IO.Path.Combine(RepoRoot.Find(), "locales")); var texts = new AppTexts(ui);
        var table = new DecodedTable(TableDecodeStatus.Ok, "units_tables", 2, false, null, [], null);
        var tab = new TableTabViewModel(A, "db/units_tables/x", texts, _ => { }, table);
        tab.ShowText = true;
        var raised = new List<string?>(); tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        ui.Load("de");

        Assert.Equal("Diese Tabelle lässt sich nicht als Text zeigen: Die Tabelle hat keine Definition.", tab.TextNotice);
        Assert.Contains(nameof(TableTabViewModel.TextNotice), raised);
        tab.Dispose();
    }

    [Fact] public void A_long_text_is_cut_at_a_line_end()
    {
        var tab = UnitsTable(Texts, [["a", 1], ["b", 2], ["c", 3], ["d", 4]]);
        tab.MaxChars = 40;

        tab.ShowText = true;

        Assert.EndsWith("\n", tab.Text);
        Assert.True(tab.Text!.Length <= 40);
        Assert.StartsWith("key\tcost\n", tab.Text);
        Assert.Equal(Texts.TabTextViewTruncated, tab.TextNotice);
    }

    [Fact] public void A_text_that_fits_exactly_is_not_cut()
    {
        var tab = UnitsTable(Texts);
        tab.MaxChars = "key\tcost\n#units_tables;2;db/units_tables/x\t\na\t1\n".Length;
        tab.ShowText = true;
        Assert.Null(tab.TextNotice);
        Assert.EndsWith("a\t1\n", tab.Text);
    }

    [Fact] public void The_notice_follows_a_language_switch()
    {
        var ui = new UiText(System.IO.Path.Combine(RepoRoot.Find(), "locales")); var texts = new AppTexts(ui);
        var tab = UnitsTable(texts, [["a", 1], ["b", 2]]);
        tab.MaxChars = 40;
        tab.ShowText = true;
        Assert.Equal("Only the first part of the text is shown.", tab.TextNotice);
        var raised = new List<string?>(); tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        ui.Load("de");

        Assert.Equal("Nur der Anfang des Textes wird gezeigt.", tab.TextNotice);
        Assert.Contains(nameof(TableTabViewModel.TextNotice), raised);
    }

    [Fact] public void The_text_view_of_a_loc_tab_uses_the_loc_form()
    {
        var tabs = new TabsViewModel(Texts);
        var loc = new DecodedLoc(LocDecodeStatus.Ok, [new("k1", "t1", true)], null);
        tabs.Add(A, new OpenedEntry("text/names.loc", EntryKind.Loc, 1, 1, CompressionFormat.None, EntryProblem.None, null, false, null, loc, null), null);
        var tab = Assert.IsType<LocTabViewModel>(tabs.SelectedTab);
        Assert.Null(tab.Text);

        tab.ShowText = true;

        Assert.StartsWith("key\ttext\ttooltip\n#Loc;1;", tab.Text);
        Assert.Contains("k1\tt1\ttrue\n", tab.Text);
        Assert.False(tab.ShowGrid);
    }
}
