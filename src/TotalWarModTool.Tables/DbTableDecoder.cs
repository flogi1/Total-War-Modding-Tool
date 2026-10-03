using System.Globalization;
using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables;

/// <summary>
/// Decodes the binary DB table of Total War: Warhammer III with a definition from the schema. Never throws for the data:
/// a problem becomes a status with a detail text and no rows. Argument errors (null) still throw.
/// </summary>
public static class DbTableDecoder
{
    // A definition without fields has rows of zero bytes, so the remaining bytes cannot bound the row count. This cap does.
    private const uint MaxRowsWithoutFields = 1_000_000;

    private static readonly byte[] GuidMarker = [0xFD, 0xFE, 0xFC, 0xFF];
    private static readonly byte[] VersionMarker = [0xFC, 0xFD, 0xFE, 0xFF];

    public static DecodedTable Decode(byte[] data, string tableName, GameSchema schema)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(schema);

        var cursor = new ByteCursor(data);
        var version = 0;
        var hasGuid = false;

        if (!TryReadHeader(ref cursor, ref version, ref hasGuid, out var rowCount, out var headerReason))
        {
            return Result(TableDecodeStatus.Failed, tableName, version, hasGuid, null, "Header: " + headerReason);
        }

        if (!schema.Tables.ContainsKey(tableName))
        {
            return Result(TableDecodeStatus.TableNotInSchema, tableName, version, hasGuid, null, "Table is not in the schema: " + tableName);
        }

        var definition = schema.Find(tableName, version);
        if (definition is null)
        {
            return Result(TableDecodeStatus.VersionNotInSchema, tableName, version, hasGuid, null,
                "Version " + version.ToString(CultureInfo.InvariantCulture) + " of the table is not in the schema: " + tableName);
        }

        foreach (var field in definition.Fields)
        {
            if (field.Type == FieldType.Unsupported)
            {
                return Result(TableDecodeStatus.UnsupportedFieldType, tableName, version, hasGuid, definition,
                    "Field '" + field.Name + "' has the unsupported type '" + field.RawType + "'");
            }
        }

        // Every field type takes at least one byte, so more rows than remaining bytes cannot be real.
        var fieldCount = definition.Fields.Count;
        if (fieldCount > 0 ? rowCount > (uint)cursor.Remaining : rowCount > MaxRowsWithoutFields)
        {
            return Result(TableDecodeStatus.Failed, tableName, version, hasGuid, definition,
                $"Row count {rowCount} is larger than the data can hold ({cursor.Remaining} bytes remain)");
        }

        var rows = new List<object?[]>((int)rowCount);
        for (var r = 0; r < rowCount; r++)
        {
            var row = new object?[fieldCount];
            for (var c = 0; c < fieldCount; c++)
            {
                var field = definition.Fields[c];
                if (!TryReadCell(ref cursor, field.Type, out row[c], out var reason))
                {
                    return Result(TableDecodeStatus.Failed, tableName, version, hasGuid, definition,
                        $"Row {r}, column '{field.Name}' ({field.RawType}): {reason}");
                }
            }

            rows.Add(row);
        }

        if (cursor.Remaining != 0)
        {
            return Result(TableDecodeStatus.Failed, tableName, version, hasGuid, definition,
                $"{cursor.Remaining} unexpected bytes after the last row (offset {cursor.Position})");
        }

        return new DecodedTable(TableDecodeStatus.Ok, tableName, version, hasGuid, definition, rows, null);
    }

    private static DecodedTable Result(TableDecodeStatus status, string tableName, int version, bool hasGuid, TableDefinition? definition, string detail) =>
        new(status, tableName, version, hasGuid, definition, [], detail);

    private static bool TryReadHeader(ref ByteCursor cursor, ref int version, ref bool hasGuid, out uint rowCount, out string? reason)
    {
        rowCount = 0;
        if (cursor.StartsWith(GuidMarker))
        {
            cursor.TryReadBytes(GuidMarker.Length, out _, out _);
            if (!cursor.TryReadStringU16(out _, out reason))
            {
                reason = "GUID marker is cut off: " + reason;
                return false;
            }

            hasGuid = true;
        }

        if (cursor.StartsWith(VersionMarker))
        {
            cursor.TryReadBytes(VersionMarker.Length, out _, out _);
            if (!cursor.TryReadInt32(out var fileVersion, out reason))
            {
                reason = "version marker is cut off: " + reason;
                return false;
            }

            version = fileVersion;
        }

        if (!cursor.TryReadByte(out _, out reason) || !cursor.TryReadUInt32(out rowCount, out reason))
        {
            reason = "row count is cut off: " + reason;
            return false;
        }

        return true;
    }

    private static bool TryReadCell(ref ByteCursor cursor, FieldType type, out object? value, out string? reason)
    {
        value = null;
        reason = null;
        switch (type)
        {
            case FieldType.Boolean:
                {
                    var ok = cursor.TryReadFlag(out var v, out reason);
                    value = v;
                    return ok;
                }
            case FieldType.I16:
                {
                    var ok = cursor.TryReadInt16(out var v, out reason);
                    value = v;
                    return ok;
                }
            case FieldType.I32:
                {
                    var ok = cursor.TryReadInt32(out var v, out reason);
                    value = v;
                    return ok;
                }
            case FieldType.I64:
                {
                    var ok = cursor.TryReadInt64(out var v, out reason);
                    value = v;
                    return ok;
                }
            case FieldType.F32:
                {
                    var ok = cursor.TryReadSingle(out var v, out reason);
                    value = v;
                    return ok;
                }
            case FieldType.F64:
                {
                    var ok = cursor.TryReadDouble(out var v, out reason);
                    value = v;
                    return ok;
                }
            case FieldType.ColourRgb:
                {
                    var ok = cursor.TryReadUInt32(out var v, out reason);
                    value = v.ToString("X6", CultureInfo.InvariantCulture);
                    return ok;
                }
            case FieldType.StringU8:
                {
                    var ok = cursor.TryReadStringU8(out var v, out reason);
                    value = v;
                    return ok;
                }
            case FieldType.StringU16:
                {
                    var ok = cursor.TryReadStringU16(out var v, out reason);
                    value = v;
                    return ok;
                }
            case FieldType.OptionalStringU8:
                {
                    if (!cursor.TryReadFlag(out var present, out reason))
                    {
                        return false;
                    }

                    if (!present)
                    {
                        value = string.Empty;
                        return true;
                    }

                    var ok = cursor.TryReadStringU8(out var v, out reason);
                    value = v;
                    return ok;
                }
            default:
                reason = "unsupported field type " + type;
                return false;
        }
    }
}
