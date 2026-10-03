using System.Globalization;

namespace TotalWarModTool.Tables;

public sealed record LocRow(string Key, string Text, bool Tooltip);

public enum LocDecodeStatus { Ok, Failed }

/// <summary><paramref name="Rows"/> is empty unless <paramref name="Status"/> is Ok. <paramref name="Detail"/> is short technical English and null for Ok.</summary>
public sealed record DecodedLoc(LocDecodeStatus Status, IReadOnlyList<LocRow> Rows, string? Detail);

/// <summary>Decodes a Loc file (UTF-16 key, text and a tooltip flag per row). Never throws for the data.</summary>
public static class LocDecoder
{
    private const int SupportedVersion = 1;

    // A row is a key (u16 length), a text (u16 length) and a flag byte: at least 5 bytes.
    private const int MinRowBytes = 5;

    private static readonly byte[] Marker = [0xFF, 0xFE, 0x4C, 0x4F, 0x43, 0x00];

    public static DecodedLoc Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var cursor = new ByteCursor(data);
        if (!cursor.StartsWith(Marker))
        {
            return Fail("Header: the LOC marker is missing");
        }

        cursor.TryReadBytes(Marker.Length, out _, out _);
        if (!cursor.TryReadInt32(out var version, out var reason) || !cursor.TryReadUInt32(out var rowCount, out reason))
        {
            return Fail("Header: " + reason);
        }

        if (version != SupportedVersion)
        {
            return Fail("Unsupported Loc version " + version.ToString(CultureInfo.InvariantCulture));
        }

        if (rowCount > (uint)(cursor.Remaining / MinRowBytes))
        {
            return Fail($"Row count {rowCount} is larger than the data can hold ({cursor.Remaining} bytes remain)");
        }

        var rows = new List<LocRow>((int)rowCount);
        for (var r = 0; r < rowCount; r++)
        {
            if (!cursor.TryReadStringU16(out var key, out reason))
            {
                return Fail($"Row {r}, key: {reason}");
            }

            if (!cursor.TryReadStringU16(out var text, out reason))
            {
                return Fail($"Row {r}, text: {reason}");
            }

            if (!cursor.TryReadFlag(out var tooltip, out reason))
            {
                return Fail($"Row {r}, tooltip flag: {reason}");
            }

            rows.Add(new LocRow(key, text, tooltip));
        }

        if (cursor.Remaining != 0)
        {
            return Fail($"{cursor.Remaining} unexpected bytes after the last row (offset {cursor.Position})");
        }

        return new DecodedLoc(LocDecodeStatus.Ok, rows, null);
    }

    private static DecodedLoc Fail(string detail) => new(LocDecodeStatus.Failed, [], detail);
}
