using System.Text;

namespace TotalWarModTool.Tables.Tests;

/// <summary>Builds the bytes of a binary DB table or Loc file piece by piece. BCL only.</summary>
internal sealed class TableBytes
{
    private readonly MemoryStream _stream = new();

    public TableBytes Guid(string guid = "46e87609-af6e-4857-bea4-f29b4fbd55fd")
    {
        Raw(0xFD, 0xFE, 0xFC, 0xFF);
        return Raw(BitConverter.GetBytes((ushort)guid.Length)).Raw(Encoding.Unicode.GetBytes(guid));
    }

    public TableBytes Version(int version) => Raw(0xFC, 0xFD, 0xFE, 0xFF).I32(version);

    public TableBytes Rows(uint count) => Raw(0x01).Raw(BitConverter.GetBytes(count));

    public TableBytes Bool(bool v) => Raw(v ? (byte)1 : (byte)0);

    public TableBytes I16(short v) => Raw(BitConverter.GetBytes(v));

    public TableBytes I32(int v) => Raw(BitConverter.GetBytes(v));

    public TableBytes I64(long v) => Raw(BitConverter.GetBytes(v));

    public TableBytes F32(float v) => Raw(BitConverter.GetBytes(v));

    public TableBytes F64(double v) => Raw(BitConverter.GetBytes(v));

    public TableBytes Colour(uint v) => Raw(BitConverter.GetBytes(v));

    public TableBytes Str8(string v)
    {
        var bytes = Encoding.UTF8.GetBytes(v);
        return Raw(BitConverter.GetBytes((ushort)bytes.Length)).Raw(bytes);
    }

    public TableBytes Str16(string v) => Raw(BitConverter.GetBytes((ushort)v.Length)).Raw(Encoding.Unicode.GetBytes(v));

    public TableBytes OptStr8(string? v) => v is null ? Raw(0) : Raw(1).Str8(v);

    public TableBytes Raw(params byte[] bytes)
    {
        _stream.Write(bytes);
        return this;
    }

    public byte[] Build() => _stream.ToArray();
}
