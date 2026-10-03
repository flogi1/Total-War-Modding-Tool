using System.Buffers.Binary;
using System.Text;

namespace TotalWarModTool.Tables;

/// <summary>
/// Forward-only little-endian reader over untrusted bytes. Every read checks the remaining length first and reports a
/// problem as a reason text instead of throwing; a length taken from the file never sizes anything before it is checked.
/// </summary>
internal ref struct ByteCursor
{
    private readonly ReadOnlySpan<byte> _data;

    public ByteCursor(ReadOnlySpan<byte> data)
    {
        _data = data;
        Position = 0;
    }

    public int Position { get; private set; }

    public readonly int Remaining => _data.Length - Position;

    public readonly bool StartsWith(ReadOnlySpan<byte> prefix) => _data[Position..].StartsWith(prefix);

    public bool TryReadBytes(int count, out ReadOnlySpan<byte> bytes, out string? reason)
    {
        if (count > Remaining)
        {
            bytes = default;
            reason = $"needs {count} bytes at offset {Position}, only {Remaining} remain";
            return false;
        }

        bytes = _data.Slice(Position, count);
        Position += count;
        reason = null;
        return true;
    }

    public bool TryReadByte(out byte value, out string? reason)
    {
        var ok = TryReadBytes(1, out var bytes, out reason);
        value = ok ? bytes[0] : (byte)0;
        return ok;
    }

    public bool TryReadUInt16(out ushort value, out string? reason)
    {
        var ok = TryReadBytes(2, out var bytes, out reason);
        value = ok ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : (ushort)0;
        return ok;
    }

    public bool TryReadInt16(out short value, out string? reason)
    {
        var ok = TryReadBytes(2, out var bytes, out reason);
        value = ok ? BinaryPrimitives.ReadInt16LittleEndian(bytes) : (short)0;
        return ok;
    }

    public bool TryReadInt32(out int value, out string? reason)
    {
        var ok = TryReadBytes(4, out var bytes, out reason);
        value = ok ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : 0;
        return ok;
    }

    public bool TryReadUInt32(out uint value, out string? reason)
    {
        var ok = TryReadBytes(4, out var bytes, out reason);
        value = ok ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : 0;
        return ok;
    }

    public bool TryReadInt64(out long value, out string? reason)
    {
        var ok = TryReadBytes(8, out var bytes, out reason);
        value = ok ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : 0;
        return ok;
    }

    public bool TryReadSingle(out float value, out string? reason)
    {
        var ok = TryReadBytes(4, out var bytes, out reason);
        value = ok ? BinaryPrimitives.ReadSingleLittleEndian(bytes) : 0f;
        return ok;
    }

    public bool TryReadDouble(out double value, out string? reason)
    {
        var ok = TryReadBytes(8, out var bytes, out reason);
        value = ok ? BinaryPrimitives.ReadDoubleLittleEndian(bytes) : 0d;
        return ok;
    }

    /// <summary>A byte 0 or 1; any other value is a problem.</summary>
    public bool TryReadFlag(out bool value, out string? reason)
    {
        value = false;
        if (!TryReadByte(out var raw, out reason))
        {
            return false;
        }

        if (raw > 1)
        {
            reason = $"value {raw} at offset {Position - 1} is not 0 or 1";
            return false;
        }

        value = raw == 1;
        return true;
    }

    /// <summary>A u16 byte count followed by UTF-8 bytes.</summary>
    public bool TryReadStringU8(out string value, out string? reason)
    {
        value = string.Empty;
        if (!TryReadUInt16(out var length, out reason) || !TryReadBytes(length, out var bytes, out reason))
        {
            return false;
        }

        value = Encoding.UTF8.GetString(bytes);
        return true;
    }

    /// <summary>A u16 code-unit count followed by UTF-16LE code units.</summary>
    public bool TryReadStringU16(out string value, out string? reason)
    {
        value = string.Empty;
        if (!TryReadUInt16(out var units, out reason) || !TryReadBytes(units * 2, out var bytes, out reason))
        {
            return false;
        }

        value = Encoding.Unicode.GetString(bytes);
        return true;
    }
}
