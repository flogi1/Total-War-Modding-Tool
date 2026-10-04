using System.Text;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Workspace.Tests;

public class ProjectFileOpenerTests
{
    private const string Guid = "46e87609-af6e-4857-bea4-f29b4fbd55fd";

    // Table "units_tables", version 2: key StringU8, cost I32.
    private static readonly TableDefinition Units = new(2,
    [
        new FieldDefinition("key", FieldType.StringU8, "StringU8", true, null),
        new FieldDefinition("cost", FieldType.I32, "I32", false, null),
    ]);

    private static readonly GameSchema Schema = new(new Dictionary<string, IReadOnlyList<TableDefinition>> { ["units_tables"] = [Units] });

    private static readonly IReadOnlyList<object?[]> Rows = [new object?[] { "unit_a", 100 }, new object?[] { "unit_b", 250 }];

    private static string Put(TempDir dir, string relative, byte[] bytes)
    {
        var file = Path.Combine(dir.Path, "pack", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);
        return file;
    }

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    private static string UnitsTsv() => TableTsv.Write("units_tables", 2, "db/units_tables/a", Units, Rows).Text!;

    [Fact] public void Opens_a_table_tsv_as_a_decoded_table()
    {
        using var dir = new TempDir();
        var bytes = Utf8(UnitsTsv());
        Put(dir, @"db\units_tables\a.tsv", bytes);

        var opened = ProjectFileOpener.Open(dir.Path, @"db\units_tables\a.tsv", Schema, CancellationToken.None);

        Assert.Equal("db/units_tables/a.tsv", opened.EntryPath);
        Assert.Equal(EntryKind.Table, opened.Kind);
        Assert.Equal((EntryProblem.None, false, CompressionFormat.None), (opened.ReadProblem, opened.SchemaMissing, opened.Format));
        Assert.Equal((long)bytes.Length, opened.StoredSize);
        Assert.Equal((long)bytes.Length, opened.UncompressedSize);
        var table = opened.Table!;
        Assert.Equal((TableDecodeStatus.Ok, "units_tables", 2, false, (string?)null), (table.Status, table.TableName, table.Version, table.HasGuid, table.Detail));
        Assert.Same(Units, table.Definition);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(["unit_a", 100], table.Rows[0]);
        Assert.Equal(["unit_b", 250], table.Rows[1]);
        Assert.Null(opened.Loc);
        Assert.Null(opened.Text);
    }

    [Fact] public void A_broken_tsv_gives_a_failed_table_with_the_line()
    {
        using var dir = new TempDir();
        Put(dir, @"db\units_tables\a.tsv", Utf8(UnitsTsv().Replace("100", "many")));

        var opened = ProjectFileOpener.Open(dir.Path, @"db\units_tables\a.tsv", Schema, CancellationToken.None);

        Assert.Equal(EntryProblem.None, opened.ReadProblem);
        var table = opened.Table!;
        Assert.Equal((TableDecodeStatus.Failed, "units_tables", 0, false), (table.Status, table.TableName, table.Version, table.HasGuid));
        Assert.Null(table.Definition);
        Assert.Empty(table.Rows);
        Assert.StartsWith("line 3:", table.Detail);
    }

    [Fact] public void A_tsv_that_is_not_utf8_gives_a_failed_table()
    {
        using var dir = new TempDir();
        Put(dir, @"db\units_tables\a.tsv", [0x6B, 0xFF]);

        var opened = ProjectFileOpener.Open(dir.Path, @"db\units_tables\a.tsv", Schema, CancellationToken.None);

        Assert.Equal((TableDecodeStatus.Failed, "the file is not valid UTF-8"), (opened.Table!.Status, opened.Table.Detail));
    }

    [Fact] public void A_table_without_schema_is_not_read()
    {
        using var dir = new TempDir();
        var bytes = Utf8(UnitsTsv());
        Put(dir, @"db\units_tables\a.tsv", bytes);

        var opened = ProjectFileOpener.Open(dir.Path, @"db\units_tables\a.tsv", null, CancellationToken.None);

        Assert.True(opened.SchemaMissing);
        Assert.Null(opened.Table);
        Assert.Equal(EntryKind.Table, opened.Kind);
        Assert.Equal((EntryProblem.None, (long)bytes.Length), (opened.ReadProblem, opened.UncompressedSize));
    }

    [Fact] public void Opens_loc_tsv_binary_tables_text_and_other_files()
    {
        using var dir = new TempDir();
        var locRows = new[] { new LocRow("k1", "Text one", false), new LocRow("k2", "Text two", true) };
        var locTsv = Utf8(TableTsv.WriteLoc("text/x.loc", locRows).Text!);
        var binary = DbTableEncoder.Encode(Units, 2, Guid, Rows);
        var lua = Utf8("print(1)");
        Put(dir, @"text\x.loc.tsv", locTsv);
        Put(dir, @"db\units_tables\bin", binary);
        Put(dir, @"text\bin.loc", LocEncoder.Encode(locRows));
        Put(dir, @"script\a.lua", lua);
        Put(dir, "model.bin", [1, 2, 3, 4, 5]);

        var loc = ProjectFileOpener.Open(dir.Path, @"text\x.loc.tsv", Schema, CancellationToken.None);
        Assert.Equal((EntryKind.Loc, "text/x.loc.tsv", (long)locTsv.Length), (loc.Kind, loc.EntryPath, loc.UncompressedSize));
        Assert.Equal((LocDecodeStatus.Ok, (string?)null), (loc.Loc!.Status, loc.Loc.Detail));
        Assert.Equal(locRows, loc.Loc.Rows);

        var table = ProjectFileOpener.Open(dir.Path, @"db\units_tables\bin", Schema, CancellationToken.None);
        Assert.Equal((EntryKind.Table, (long)binary.Length, false), (table.Kind, table.StoredSize, table.SchemaMissing));
        Assert.Equal((TableDecodeStatus.Ok, true), (table.Table!.Status, table.Table.HasGuid));
        Assert.Equal(2, table.Table.Rows.Count);

        var binaryLoc = ProjectFileOpener.Open(dir.Path, @"text\bin.loc", Schema, CancellationToken.None);
        Assert.Equal(EntryKind.Loc, binaryLoc.Kind);
        Assert.Equal(LocDecodeStatus.Ok, binaryLoc.Loc!.Status);

        var text = ProjectFileOpener.Open(dir.Path, @"script\a.lua", Schema, CancellationToken.None);
        Assert.Equal((EntryKind.Text, "script/a.lua"), (text.Kind, text.EntryPath));
        Assert.Equal("print(1)", text.Text!.Text);

        var other = ProjectFileOpener.Open(dir.Path, "model.bin", Schema, CancellationToken.None);
        Assert.Equal((EntryKind.Other, 5L, 5L, CompressionFormat.None, EntryProblem.None), (other.Kind, other.StoredSize, other.UncompressedSize, other.Format, other.ReadProblem));
        Assert.Null(other.Table); Assert.Null(other.Loc); Assert.Null(other.Text);
    }

    [Fact] public void A_missing_file_is_unreadable()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "pack"));

        var opened = ProjectFileOpener.Open(dir.Path, @"script\gone.lua", Schema, CancellationToken.None);

        Assert.Equal(EntryProblem.Unreadable, opened.ReadProblem);
        Assert.False(string.IsNullOrWhiteSpace(opened.ReadDetail));
        Assert.Equal((EntryKind.Text, "script/gone.lua"), (opened.Kind, opened.EntryPath));
        Assert.Null(opened.Text);
    }

    [Theory]
    [InlineData(@"..\outside.lua")]
    [InlineData(@"a\..\..\outside.lua")]
    [InlineData(@"C:\outside.lua")]
    [InlineData("")]
    public void A_path_outside_the_pack_folder_is_an_argument_error(string relative)
    {
        using var dir = new TempDir();
        Assert.Throws<ArgumentException>(() => ProjectFileOpener.Open(dir.Path, relative, Schema, CancellationToken.None));
    }

    [Fact] public void A_cancelled_token_stops_opening()
    {
        using var dir = new TempDir();
        Put(dir, @"script\a.lua", [1]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => ProjectFileOpener.Open(dir.Path, @"script\a.lua", Schema, cts.Token));
    }
}
