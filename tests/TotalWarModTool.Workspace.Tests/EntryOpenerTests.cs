using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Workspace.Tests;

public class EntryOpenerTests
{
    // Table "x_tables", version 0, one I32 field "value".
    private static readonly GameSchema schema = new(new Dictionary<string, IReadOnlyList<TableDefinition>>
    {
        ["x_tables"] = [new TableDefinition(0, [new FieldDefinition("value", FieldType.I32, "I32", false, null)])],
    });

    private static readonly byte[] TableBytes = PackBuilder.Hex("01 | 01 00 00 00 | 2A 00 00 00");                               // 1 row, value 42
    private static readonly byte[] LocBytes = PackBuilder.Hex("FF FE 4C 4F 43 00 | 01 00 00 00 | 01 00 00 00 | 01 00 6B 00 | 01 00 74 00 | 01");

    private static (PackIndex Index, PackEntry Entry) One(TempDir dir, string entryPath, byte[] stored, bool compressed = false)
    {
        var path = new PackBuilder().AddEntry(entryPath.Replace('/', '\\'), stored, compressed).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        return (index, index.Entries[0]);
    }

    [Theory]
    [InlineData("db/x_tables/data__", EntryKind.Table)]
    [InlineData("text/db/names.LOC", EntryKind.Loc)]
    [InlineData("script/campaign/main.lua", EntryKind.Text)]
    [InlineData("ui/layout.XML", EntryKind.Text)]
    [InlineData("a.txt", EntryKind.Text)] [InlineData("a.json", EntryKind.Text)] [InlineData("a.csv", EntryKind.Text)] [InlineData("a.tsv", EntryKind.Text)]
    [InlineData("movies/intro.ca_vp8", EntryKind.Other)]
    [InlineData("db/x_tables", EntryKind.Other)]                 // a file directly below db is not a table
    [InlineData("noextension", EntryKind.Other)]
    public void KindOf_decides_by_path(string path, EntryKind kind) => Assert.Equal(kind, EntryOpener.KindOf(path));

    [Fact] public void Table_is_decoded_with_the_schema()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, "db/x_tables/data__", TableBytes);
        var opened = EntryOpener.Open(index, entry, schema, CancellationToken.None);
        Assert.Equal((EntryKind.Table, EntryProblem.None, false, 9L, 9L), (opened.Kind, opened.ReadProblem, opened.SchemaMissing, opened.StoredSize, opened.UncompressedSize));
        Assert.Equal((TableDecodeStatus.Ok, 42), (opened.Table!.Status, (int)opened.Table.Rows[0][0]!));
        Assert.Null(opened.Loc); Assert.Null(opened.Text);
    }

    [Fact] public void Table_that_does_not_decode_keeps_its_diagnosis()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, "db/unknown_tables/data__", TableBytes);
        var opened = EntryOpener.Open(index, entry, schema, CancellationToken.None);
        Assert.Equal((EntryProblem.None, TableDecodeStatus.TableNotInSchema), (opened.ReadProblem, opened.Table!.Status));
    }

    [Fact] public void Table_without_a_schema_reads_no_content()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, "db/x_tables/data__", TableBytes);
        var opened = EntryOpener.Open(index, entry, null, CancellationToken.None);
        Assert.Equal((true, (DecodedTable?)null, 9L), (opened.SchemaMissing, opened.Table, opened.UncompressedSize));
    }

    [Fact] public void Loc_and_text_are_decoded()
    {
        using var dir = new TempDir();
        var (locIndex, locEntry) = One(dir, "text/names.loc", LocBytes);
        var loc = EntryOpener.Open(locIndex, locEntry, null, CancellationToken.None);
        Assert.Equal(new LocRow("k", "t", true), Assert.Single(loc.Loc!.Rows));
        using var dir2 = new TempDir();
        var (textIndex, textEntry) = One(dir2, "script/main.lua", System.Text.Encoding.UTF8.GetBytes("print('hä')"));
        Assert.Equal("print('hä')", EntryOpener.Open(textIndex, textEntry, null, CancellationToken.None).Text!.Text);
    }

    // Review Focus 3: a big or unknown file is never read. The claimed size is far above what the pack holds; a real read would fail.
    [Fact] public void Other_kind_reads_no_content()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, "movies/intro.ca_vp8", PackBuilder.Stored(PackBuilder.Hex("28 B5 2F FD 00 00 00 00 00"), 200_000_000), compressed: true);
        var opened = EntryOpener.Open(index, entry, null, CancellationToken.None);
        Assert.Equal((EntryKind.Other, EntryProblem.None, CompressionFormat.Zstd, 200_000_000L, 13L),
            (opened.Kind, opened.ReadProblem, opened.Format, opened.UncompressedSize, opened.StoredSize));
        Assert.Null(opened.Text);
    }

    [Fact] public void Read_problem_gives_no_content()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, "script/main.lua", PackBuilder.Stored(PackBuilder.Hex("5D 00 00 80 00 FF FF FF FF"), 100), compressed: true);
        var opened = EntryOpener.Open(index, entry, null, CancellationToken.None);
        Assert.Equal((EntryProblem.UnsupportedCompression, CompressionFormat.Lzma, (TextContent?)null), (opened.ReadProblem, opened.Format, opened.Text));
        Assert.False(string.IsNullOrWhiteSpace(opened.ReadDetail));
    }

    [Fact] public void Cancelled_token_throws()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, "a.txt", [1]);
        Assert.Throws<OperationCanceledException>(() => EntryOpener.Open(index, entry, null, new CancellationToken(true)));
    }
}
