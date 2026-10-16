using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables.Tests;

public class DbTableDecoderHeaderTests
{
    private const string Guid = "46e87609-af6e-4857-bea4-f29b4fbd55fd";

    private static readonly TableDefinition Definition =
        new(1, [new FieldDefinition("n", FieldType.I32, "I32", true, null)]);

    private static byte[] Table(int version, string? guid, int rows = 1)
    {
        var data = new List<object?[]>();
        for (var i = 0; i < rows; i++)
        {
            data.Add([i]);
        }

        return DbTableEncoder.Encode(Definition, version, guid, data);
    }

    [Fact]
    public void A_table_without_version_marker_has_version_zero()
    {
        var header = DbTableDecoder.ReadHeader(Table(0, null));

        Assert.NotNull(header);
        Assert.Equal(0, header.Version);
        Assert.Null(header.Guid);
    }

    [Fact]
    public void A_version_without_guid_is_read()
    {
        var header = DbTableDecoder.ReadHeader(Table(3, null));

        Assert.Equal(new TableHeader(3, null), header);
    }

    [Fact]
    public void A_version_with_guid_is_read()
    {
        var header = DbTableDecoder.ReadHeader(Table(3, Guid));

        Assert.Equal(new TableHeader(3, Guid), header);
    }

    [Fact]
    public void The_first_header_bytes_of_a_large_table_are_enough()
    {
        var bytes = Table(7, Guid, rows: 5000);

        var header = DbTableDecoder.ReadHeader(bytes.AsSpan(0, DbTableDecoder.TableHeaderBytes));

        Assert.Equal(new TableHeader(7, Guid), header);
    }

    [Fact]
    public void A_cut_off_version_marker_gives_null()
    {
        var bytes = new TableBytes().Raw(0xFC, 0xFD, 0xFE, 0xFF, 0x03).Build();

        Assert.Null(DbTableDecoder.ReadHeader(bytes));
    }

    [Fact]
    public void A_missing_row_count_gives_null()
    {
        var bytes = new TableBytes().Version(3).Build();

        Assert.Null(DbTableDecoder.ReadHeader(bytes));
    }
}
