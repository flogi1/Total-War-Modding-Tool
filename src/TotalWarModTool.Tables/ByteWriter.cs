using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace TotalWarModTool.Tables;

/// <summary>
/// Little-endian writer into memory, the counterpart of <see cref="ByteCursor"/>. A string that does not fit its u16 length
/// field is reported as a reason text, like the cursor reports a problem, so the caller can name the row and column.
/// </summary>
internal sealed class ByteWriter
{
    private const int MaxLength = ushort.MaxValue;

    private readonly MemoryStream _stream = new();

    public void WriteBytes(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);

    public void WriteByte(byte value) => _stream.WriteByte(value);

    public void WriteFlag(bool value) => _stream.WriteByte(value ? (byte)1 : (byte)0);

    public void WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteInt16(short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteInt64(long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteSingle(float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteDouble(double value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    /// <summary>A u16 byte count followed by UTF-8 bytes. Writes nothing when the text needs more than 65535 bytes or has an unpaired surrogate.</summary>
    public bool TryWriteStringU8(string value, out string? reason)
    {
        if (!IsWellFormed(value, out reason))
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaxLength)
        {
            reason = $"text needs {bytes.Length.ToString(CultureInfo.InvariantCulture)} UTF-8 bytes, the limit is {MaxLength.ToString(CultureInfo.InvariantCulture)}";
            return false;
        }

        WriteUInt16((ushort)bytes.Length);
        _stream.Write(bytes);
        reason = null;
        return true;
    }

    /// <summary>A u16 code-unit count followed by UTF-16LE code units. Writes nothing when the text has more than 65535 units or an unpaired surrogate.</summary>
    public bool TryWriteStringU16(string value, out string? reason)
    {
        if (!IsWellFormed(value, out reason))
        {
            return false;
        }

        if (value.Length > MaxLength)
        {
            reason = $"text has {value.Length.ToString(CultureInfo.InvariantCulture)} UTF-16 units, the limit is {MaxLength.ToString(CultureInfo.InvariantCulture)}";
            return false;
        }

        WriteUInt16((ushort)value.Length);
        _stream.Write(Encoding.Unicode.GetBytes(value));
        reason = null;
        return true;
    }

    // An unpaired surrogate is no valid text: UTF-8 would silently turn it into U+FFFD, so every string path rejects it.
    // Internal so that TableTsv applies the very same rule when it reads text.
    internal static bool IsWellFormed(string value, out string? reason)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(value[i]))
            {
                reason = $"text has an unpaired surrogate at index {i.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }
        }

        reason = null;
        return true;
    }

    public byte[] ToArray() => _stream.ToArray();
}
