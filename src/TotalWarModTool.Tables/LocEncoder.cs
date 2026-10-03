using System.Globalization;

namespace TotalWarModTool.Tables;

/// <summary>Encodes Loc rows into the binary Loc format, the inverse of <see cref="LocDecoder"/>.</summary>
public static class LocEncoder
{
    private const int Version = 1;

    private static readonly byte[] Marker = [0xFF, 0xFE, 0x4C, 0x4F, 0x43, 0x00];

    /// <summary>Throws an ArgumentException that names the row when a key or a text has more than 65535 UTF-16 units.</summary>
    public static byte[] Encode(IReadOnlyList<LocRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var writer = new ByteWriter();
        writer.WriteBytes(Marker);
        writer.WriteInt32(Version);
        writer.WriteUInt32((uint)rows.Count);

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r] ?? throw new ArgumentException($"Row {r.ToString(CultureInfo.InvariantCulture)} is null", nameof(rows));
            if (!writer.TryWriteStringU16(row.Key, out var reason))
            {
                throw new ArgumentException($"Row {r.ToString(CultureInfo.InvariantCulture)}, key: {reason}", nameof(rows));
            }

            if (!writer.TryWriteStringU16(row.Text, out reason))
            {
                throw new ArgumentException($"Row {r.ToString(CultureInfo.InvariantCulture)}, text: {reason}", nameof(rows));
            }

            writer.WriteFlag(row.Tooltip);
        }

        return writer.ToArray();
    }
}
