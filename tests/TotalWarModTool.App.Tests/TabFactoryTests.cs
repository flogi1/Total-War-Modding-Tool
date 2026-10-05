using TotalWarModTool.Localization;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App.Tests;

public class TabFactoryTests
{
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly PackSource Pack = new(@"D:\data\a.pack", "a.pack", PackOrigin.DataFolderMod, null, null, null, PackProblem.None, null);
    private static OpenedEntry Entry(string path, EntryKind kind, EntryProblem problem = EntryProblem.None, string? detail = null, bool schemaMissing = false,
        DecodedTable? table = null, DecodedLoc? loc = null, TextContent? text = null, CompressionFormat format = CompressionFormat.None, long stored = 10, long size = 20) =>
        new(path, kind, stored, size, format, problem, detail, schemaMissing, table, loc, text);
    private static TabViewModel Create(OpenedEntry e, ResolvedSchema? schema = null) => TabFactory.Create(Pack, e, schema, Texts, _ => { });
    private static readonly TableDefinition Def = new(3, [new("unit_key", FieldType.StringU8, "StringU8", true, null), new("cost", FieldType.I32, "I32", false, null)]);

    [Theory]
    [InlineData(0, "0 B")] [InlineData(1023, "1023 B")] [InlineData(1024, "1.0 KB")] [InlineData(1536, "1.5 KB")]
    [InlineData(5 * 1024 * 1024, "5.0 MB")] [InlineData(1181116006, "1.1 GB")]
    [InlineData(1048575, "1.0 MB")] [InlineData(1073741823, "1.0 GB")]
    public void FormatSize_uses_binary_units(long bytes, string expected) => Assert.Equal(expected, TabFactory.FormatSize(bytes));

    [Fact] public void Table_tab_has_columns_rows_and_header()
    {
        var table = new DecodedTable(TableDecodeStatus.Ok, "units_tables", 3, false, Def, [["a", 1], ["b", 2]], null);
        var tab = Assert.IsType<TableTabViewModel>(Create(Entry("db/units_tables/data__", EntryKind.Table, table: table)));
        Assert.Equal(("data__", "a.pack: db/units_tables/data__", "units_tables, version 3, 2 row(s)", Pack.Path), (tab.Title, tab.ToolTip, tab.Header, tab.PackPath));
        Assert.Equal(["unit_key", "cost"], tab.Columns);
        Assert.Equal(2, tab.Rows.Count);
    }

    [Fact] public void The_clipboard_is_passed_to_table_and_loc_tabs_and_is_optional()
    {
        var table = new DecodedTable(TableDecodeStatus.Ok, "units_tables", 3, false, Def, [["a", 1], ["b", 2]], null);
        var tableEntry = Entry("db/units_tables/data__", EntryKind.Table, table: table);
        var loc = new DecodedLoc(LocDecodeStatus.Ok, [new("k1", "t1", true)], null);
        var locEntry = Entry("text/names.loc", EntryKind.Loc, loc: loc);
        var clipboard = new FakeClipboard();

        var withClipboard = Assert.IsType<TableTabViewModel>(TabFactory.Create(Pack, tableEntry, null, Texts, _ => { }, clipboard));
        var locWith = Assert.IsType<LocTabViewModel>(TabFactory.Create(Pack, locEntry, null, Texts, _ => { }, clipboard));
        withClipboard.SelectedRows = [0];
        locWith.SelectedRows = [0];
        Assert.True(withClipboard.CopyRowsCommand.CanExecute(null));
        Assert.True(locWith.CopyRowsCommand.CanExecute(null));

        var without = Assert.IsType<TableTabViewModel>(Create(tableEntry));
        without.SelectedRows = [0];
        Assert.False(without.CopyRowsCommand.CanExecute(null));
    }

    [Fact] public void Table_that_does_not_decode_becomes_an_info_tab_with_table_version_and_schema()
    {
        var table = new DecodedTable(TableDecodeStatus.VersionNotInSchema, "units_tables", 9, false, null, [], "Version 9 is not in the schema.");
        var schema = new ResolvedSchema(null, SchemaOrigin.Shipped, @"X:\s.ron", new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Local).ToUniversalTime(), null, null);
        var tab = Assert.IsType<InfoTabViewModel>(Create(Entry("db/units_tables/data__", EntryKind.Table, table: table, format: CompressionFormat.Zstd), schema));
        Assert.Equal(("This table cannot be decoded", "The schema does not know this version of the table."), (tab.Heading, tab.Reason));
        Assert.Equal([new("Table", "units_tables"), new("Version", "9"), new("Size in the pack", "10 B"), new("Size", "20 B"), new("Compression", "zstd"),
            new("Schema", "shipped with the app, 2026-09-24"), new InfoLine("Technical detail", "Version 9 is not in the schema.")], tab.Lines);
    }

    [Fact] public void Table_without_schema_names_the_missing_schema()
    {
        var tab = Assert.IsType<InfoTabViewModel>(Create(Entry("db/units_tables/data__", EntryKind.Table, schemaMissing: true)));
        Assert.StartsWith("No schema is loaded", tab.Reason);
        Assert.Contains(new InfoLine("Schema", "none"), tab.Lines);
        Assert.DoesNotContain(tab.Lines, l => l.Label == "Version");
    }

    [Fact] public void Unsupported_type_lists_type_sizes_and_compression()
    {
        var tab = Assert.IsType<InfoTabViewModel>(Create(Entry("movies/Intro.CA_VP8", EntryKind.Other, format: CompressionFormat.Lz4, stored: 2048, size: 4096)));
        Assert.Equal(("This file type is not supported", ""), (tab.Heading, tab.Reason));
        Assert.Equal([new("Type", ".ca_vp8"), new("Size in the pack", "2.0 KB"), new("Size", "4.0 KB"), new InfoLine("Compression", "LZ4")], tab.Lines);
    }

    [Fact] public void Read_problem_wins_over_the_kind()
    {
        var tab = Assert.IsType<InfoTabViewModel>(Create(Entry("script/main.lua", EntryKind.Text, EntryProblem.UnsupportedCompression, "LZMA is not supported.", format: CompressionFormat.Lzma)));
        Assert.Equal(("This entry cannot be opened", "This compression format is not supported yet."), (tab.Heading, tab.Reason));
        Assert.Contains(new InfoLine("Compression", "LZMA"), tab.Lines);
        Assert.Contains(new InfoLine("Technical detail", "LZMA is not supported."), tab.Lines);
    }

    [Fact] public void Loc_and_text_tabs()
    {
        var loc = Assert.IsType<LocTabViewModel>(Create(Entry("text/names.loc", EntryKind.Loc, loc: new DecodedLoc(LocDecodeStatus.Ok, [new("k", "t", false)], null))));
        Assert.Equal(("1 row(s)", 1), (loc.Header, loc.Rows.Count));
        var failedLoc = Assert.IsType<InfoTabViewModel>(Create(Entry("text/names.loc", EntryKind.Loc, loc: new DecodedLoc(LocDecodeStatus.Failed, [], "short"))));
        Assert.Equal(("This loc file cannot be decoded", "The file does not have the expected loc format."), (failedLoc.Heading, failedLoc.Reason));
        var text = Assert.IsType<TextTabViewModel>(Create(Entry("a.txt", EntryKind.Text, text: new TextContent("hi", "UTF-8", false, 2))));
        Assert.Equal(("hi", "UTF-8, 2 B", (string?)null), (text.Text, text.Header, text.Notice));
        var big = Assert.IsType<TextTabViewModel>(Create(Entry("a.txt", EntryKind.Text, text: new TextContent("hi", "UTF-8", true, 8 * 1024 * 1024))));
        Assert.Equal("Only the first 5.0 MB of 8.0 MB are shown.", big.Notice);
    }

    // The tabs read the texts of the language that is active; a switch at run time changes them in place and tells the view.
    [Fact] public void Open_tabs_follow_a_language_switch()
    {
        var ui = new UiText(System.IO.Path.Combine(RepoRoot.Find(), "locales")); var texts = new AppTexts(ui);
        TabViewModel Make(OpenedEntry e, ResolvedSchema? schema = null) => TabFactory.Create(Pack, e, schema, texts, _ => { });
        var raised = new List<string>();
        T Watch<T>(T tab) where T : TabViewModel { tab.PropertyChanged += (_, e) => raised.Add($"{tab.Title}:{e.PropertyName}"); return tab; }

        var okTable = new DecodedTable(TableDecodeStatus.Ok, "units_tables", 3, false, Def, [["a", 1], ["b", 2]], null);
        var table = Watch(Assert.IsType<TableTabViewModel>(Make(Entry("db/units_tables/table", EntryKind.Table, table: okTable))));
        var badTable = new DecodedTable(TableDecodeStatus.VersionNotInSchema, "units_tables", 9, false, null, [], "Version 9 is not in the schema.");
        var resolved = new ResolvedSchema(null, SchemaOrigin.Shipped, @"X:\s.ron", new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Local).ToUniversalTime(), null, null);
        var info = Watch(Assert.IsType<InfoTabViewModel>(Make(Entry("db/units_tables/info", EntryKind.Table, table: badTable, format: CompressionFormat.Zstd), resolved)));
        var text = Watch(Assert.IsType<TextTabViewModel>(Make(Entry("big.txt", EntryKind.Text, text: new TextContent("hi", "UTF-8", true, 8 * 1024 * 1024)))));
        var loc = Watch(Assert.IsType<LocTabViewModel>(Make(Entry("names.loc", EntryKind.Loc, loc: new DecodedLoc(LocDecodeStatus.Ok, [new("k", "t", false)], null)))));

        ui.Load("de");

        Assert.Equal("units_tables, Version 3, 2 Zeile(n)", table.Header);
        Assert.Equal(("Diese Tabelle ist nicht dekodierbar", "Das Schema kennt diese Version der Tabelle nicht."), (info.Heading, info.Reason));
        Assert.Equal([new("Tabelle", "units_tables"), new("Version", "9"), new("Größe im Pack", "10 B"), new("Größe", "20 B"), new("Kompression", "zstd"),
            new("Schema", "mit der App geliefert, 2026-09-24"), new InfoLine("Technisches Detail", "Version 9 is not in the schema.")], info.Lines);
        Assert.Equal("Es werden nur die ersten 5.0 MB von 8.0 MB gezeigt.", text.Notice);
        Assert.Equal("1 Zeile(n)", loc.Header);
        Assert.Contains("table:Header", raised); Assert.Contains("info:Heading", raised); Assert.Contains("info:Reason", raised);
        Assert.Contains("info:Lines", raised); Assert.Contains("big.txt:Notice", raised); Assert.Contains("names.loc:Header", raised);
    }

    [Fact] public void A_closed_tab_no_longer_follows_the_language()
    {
        var ui = new UiText(System.IO.Path.Combine(RepoRoot.Find(), "locales")); var texts = new AppTexts(ui);
        var loc = Assert.IsType<LocTabViewModel>(TabFactory.Create(Pack, Entry("names.loc", EntryKind.Loc, loc: new DecodedLoc(LocDecodeStatus.Ok, [new("k", "t", false)], null)),
            null, texts, _ => { }));
        loc.Dispose();
        ui.Load("de");
        Assert.Equal("1 row(s)", loc.Header);
    }

    [Fact] public void Every_kind_of_tab_takes_a_tab_source_and_shows_its_name_in_the_tool_tip()
    {
        var source = TabSource.ForProject(Guid.NewGuid(), "My Mod");
        var table = new DecodedTable(TableDecodeStatus.Ok, "units_tables", 3, false, Def, [["a", 1]], null);
        var tabs = new[]
        {
            TabFactory.Create(source, Entry("db/units_tables/x", EntryKind.Table, table: table), null, Texts, _ => { }),
            TabFactory.Create(source, Entry("text/n.loc", EntryKind.Loc, loc: new DecodedLoc(LocDecodeStatus.Ok, [new("k", "t", false)], null)), null, Texts, _ => { }),
            TabFactory.Create(source, Entry("a.txt", EntryKind.Text, text: new TextContent("hi", "UTF-8", false, 2)), null, Texts, _ => { }),
            TabFactory.Create(source, Entry("misc/blob", EntryKind.Other), null, Texts, _ => { }),
        };
        Assert.Equal(source.Key, Assert.Single(tabs.Select(t => t.PackPath).Distinct()));
        Assert.Equal(["My Mod: db/units_tables/x", "My Mod: text/n.loc", "My Mod: a.txt", "My Mod: misc/blob"], tabs.Select(t => t.ToolTip));
    }

    [Fact] public void File_without_extension_says_so() =>
        Assert.Contains(new InfoLine("Type", "(no extension)"), Assert.IsType<InfoTabViewModel>(Create(Entry("misc/blob", EntryKind.Other))).Lines);
}
