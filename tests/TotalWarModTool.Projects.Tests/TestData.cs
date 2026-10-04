using System.Text;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Projects.Tests;

internal static class TestData
{
    public const string HeaderGuid = "46e87609-af6e-4857-bea4-f29b4fbd55fd";
    public static readonly FieldDefinition[] UnitFields =
    [
        new("key", FieldType.StringU8, "StringU8", true, null), new("cost", FieldType.I32, "I32", false, null), new("speed", FieldType.F32, "F32", false, null),
    ];
    public static readonly GameSchema Schema = new(new Dictionary<string, IReadOnlyList<TableDefinition>>
    {
        ["units_tables"] = [new TableDefinition(2, UnitFields)],
        ["colours_tables"] = [new TableDefinition(1, [new("r", FieldType.I32, "I32", false, null), new("g", FieldType.I32, "I32", false, null), new("b", FieldType.I32, "I32", false, null)])],
    });

    public static byte[] Units(params (string Key, int Cost, float Speed)[] rows) =>
        DbTableEncoder.Encode(Schema.Find("units_tables", 2)!, 2, HeaderGuid, rows.Select(r => new object?[] { r.Key, r.Cost, r.Speed }).ToList());

    public static byte[] Loc(params (string Key, string Text)[] rows) => LocEncoder.Encode(rows.Select(r => new LocRow(r.Key, r.Text, false)).ToList());

    public static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);
}
