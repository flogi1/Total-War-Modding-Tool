using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables;

public enum TableDecodeStatus { Ok, TableNotInSchema, VersionNotInSchema, UnsupportedFieldType, Failed }

/// <summary>
/// Rows is empty unless <paramref name="Status"/> is Ok. Cell types: Boolean bool, I16 short, I32 int, I64 long, F32 float, F64 double,
/// ColourRgb string "RRGGBB", StringU8 and StringU16 string, OptionalStringU8 string (empty when absent).
/// <paramref name="Version"/> and <paramref name="HasGuid"/> are set whenever the header was readable.
/// <paramref name="Detail"/> is short technical English, not a UI text; it is null for Ok.
/// </summary>
public sealed record DecodedTable(TableDecodeStatus Status, string TableName, int Version, bool HasGuid,
    TableDefinition? Definition, IReadOnlyList<object?[]> Rows, string? Detail);
