using System.Globalization;
using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables;

/// <summary>
/// Encodes rows into the binary DB table format, the inverse of <see cref="DbTableDecoder"/>. The rows are the decoder's model.
/// A model error throws an ArgumentException that names the row and the column; nothing is returned in that case.
/// </summary>
public static class DbTableEncoder
{
    private const int MaxColourDigits = 8;

    /// <summary>
    /// Writes the header (GUID block only when <paramref name="guid"/> is not null, version block only when
    /// <paramref name="version"/> is not 0), then the rows. An empty optional text is written as absent.
    /// </summary>
    public static byte[] Encode(TableDefinition definition, int version, string? guid, IReadOnlyList<object?[]> rows)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(rows);

        foreach (var field in definition.Fields)
        {
            if (field.Type == FieldType.Unsupported)
            {
                throw new ArgumentException("Field '" + field.Name + "' has the unsupported type '" + field.RawType + "'", nameof(definition));
            }
        }

        var writer = new ByteWriter();
        if (guid is not null)
        {
            writer.WriteBytes(DbTableDecoder.GuidMarker);
            if (!writer.TryWriteStringU16(guid, out var guidReason))
            {
                throw new ArgumentException("GUID: " + guidReason, nameof(guid));
            }
        }

        if (version != 0)
        {
            writer.WriteBytes(DbTableDecoder.VersionMarker);
            writer.WriteInt32(version);
        }

        writer.WriteByte(1);
        writer.WriteUInt32((uint)rows.Count);

        var fieldCount = definition.Fields.Count;
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row is null || row.Length != fieldCount)
            {
                throw new ArgumentException(
                    $"Row {r.ToString(CultureInfo.InvariantCulture)} has {row?.Length.ToString(CultureInfo.InvariantCulture) ?? "no"} cells, " +
                    $"the definition has {fieldCount.ToString(CultureInfo.InvariantCulture)} fields", nameof(rows));
            }

            for (var c = 0; c < fieldCount; c++)
            {
                var field = definition.Fields[c];
                if (!TryWriteCell(writer, field.Type, row[c], out var reason))
                {
                    throw new ArgumentException($"Row {r.ToString(CultureInfo.InvariantCulture)}, column '{field.Name}' ({field.RawType}): {reason}", nameof(rows));
                }
            }
        }

        return writer.ToArray();
    }

    private static bool TryWriteCell(ByteWriter writer, FieldType type, object? value, out string? reason)
    {
        reason = null;
        switch (type)
        {
            case FieldType.Boolean when value is bool v:
                writer.WriteFlag(v);
                return true;
            case FieldType.I16 when value is short v:
                writer.WriteInt16(v);
                return true;
            case FieldType.I32 when value is int v:
                writer.WriteInt32(v);
                return true;
            case FieldType.I64 when value is long v:
                writer.WriteInt64(v);
                return true;
            case FieldType.F32 when value is float v:
                writer.WriteSingle(v);
                return true;
            case FieldType.F64 when value is double v:
                writer.WriteDouble(v);
                return true;
            case FieldType.ColourRgb when value is string v:
                if (v.Length is < 1 or > MaxColourDigits
                    || !uint.TryParse(v, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var colour))
                {
                    reason = $"'{v}' is not a colour of 1 to {MaxColourDigits.ToString(CultureInfo.InvariantCulture)} hex digits";
                    return false;
                }

                writer.WriteUInt32(colour);
                return true;
            case FieldType.StringU8 when value is string v:
                return writer.TryWriteStringU8(v, out reason);
            case FieldType.StringU16 when value is string v:
                return writer.TryWriteStringU16(v, out reason);
            case FieldType.OptionalStringU8 when value is string v:
                if (v.Length == 0)
                {
                    writer.WriteFlag(false);
                    return true;
                }

                writer.WriteFlag(true);
                return writer.TryWriteStringU8(v, out reason);
            default:
                reason = value is null
                    ? "the cell is null"
                    : "the cell is a " + value.GetType().Name + ", which does not fit the field type " + type;
                return false;
        }
    }
}
