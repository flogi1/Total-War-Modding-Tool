using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects;

public enum SourceProblemKind
{
    Unreadable,
    NotInSchema,
    VersionNotInSchema,
    DecodeFailed,
    TsvInvalid,
}

/// <summary>EntryPath is the pack entry path or the project file relative to the project folder. It is empty when the whole source is meant.</summary>
public sealed record SourceProblem(TableSource Source, string EntryPath, SourceProblemKind Kind, string Detail);

/// <summary>Overridden lists the sources whose row with the same key this row replaced, earliest first.</summary>
public sealed record RowProvenance(TableSource Source, IReadOnlyList<TableSource> Overridden, bool OlderLayout, bool Duplicate);

public sealed record MergedRow(object?[] Cells, RowProvenance Provenance);

/// <summary>One table merged over a load order: later sources replace rows with the same key. Read-only after construction.</summary>
public sealed class MergedTable
{
    private const char KeySeparator = '\u0001';

    // Rough size model of one row: object overhead, one reference slot and one boxed value per cell, UTF-16 text.
    private const long RowOverheadBytes = 32;
    private const long CellBytes = 24;
    private const long CharBytes = 2;

    private static readonly IReadOnlyList<MergedRow> NoRows = [];

    private readonly Lazy<Dictionary<string, List<MergedRow>>> _byKey;

    internal MergedTable(string tableName, TableDefinition? definition, IReadOnlyList<int> keyFields,
        IReadOnlyList<MergedRow> rows, IReadOnlyList<SourceProblem> problems)
    {
        TableName = tableName;
        Definition = definition;
        KeyFields = keyFields;
        Rows = rows;
        Problems = problems;
        EstimatedBytes = Estimate(rows);
        _byKey = new Lazy<Dictionary<string, List<MergedRow>>>(BuildKeyIndex);
    }

    public string TableName { get; }

    /// <summary>Latest schema definition; null when the schema does not know the table (then Rows is empty).</summary>
    public TableDefinition? Definition { get; }

    public IReadOnlyList<int> KeyFields { get; }

    public IReadOnlyList<MergedRow> Rows { get; }

    public IReadOnlyList<SourceProblem> Problems { get; }

    public long EstimatedBytes { get; }

    /// <summary>Rows whose key fields equal the values (ordinal, in KeyFields order). Empty when the table has no key.</summary>
    public IReadOnlyList<MergedRow> FindByKey(IReadOnlyList<string> keyValues)
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        if (KeyFields.Count == 0 || keyValues.Count != KeyFields.Count)
        {
            return NoRows;
        }

        return _byKey.Value.TryGetValue(string.Join(KeySeparator, keyValues), out var rows) ? rows : NoRows;
    }

    /// <summary>Rows whose cell in the field (by name, OrdinalIgnoreCase) formats to the value. Empty for an unknown field.</summary>
    public IReadOnlyList<MergedRow> FindByField(string fieldName, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var index = FieldIndex(fieldName);
        if (index < 0)
        {
            return NoRows;
        }

        var type = Definition!.Fields[index].Type;
        var found = new List<MergedRow>();
        foreach (var row in Rows)
        {
            if (string.Equals(CellText.Format(type, row.Cells[index]), value, StringComparison.Ordinal))
            {
                found.Add(row);
            }
        }

        return found;
    }

    /// <summary>The index of the field in <see cref="Definition"/>; -1 when unknown.</summary>
    public int FieldIndex(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        if (Definition is null)
        {
            return -1;
        }

        for (var i = 0; i < Definition.Fields.Count; i++)
        {
            if (string.Equals(Definition.Fields[i].Name, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    internal static string KeyOf(TableDefinition definition, IReadOnlyList<int> keyFields, object?[] cells)
    {
        if (keyFields.Count == 1)
        {
            return CellText.Format(definition.Fields[keyFields[0]].Type, cells[keyFields[0]]);
        }

        var parts = new string[keyFields.Count];
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = CellText.Format(definition.Fields[keyFields[i]].Type, cells[keyFields[i]]);
        }

        return string.Join(KeySeparator, parts);
    }

    private Dictionary<string, List<MergedRow>> BuildKeyIndex()
    {
        var index = new Dictionary<string, List<MergedRow>>(StringComparer.Ordinal);
        if (Definition is null)
        {
            return index;
        }

        foreach (var row in Rows)
        {
            var key = KeyOf(Definition, KeyFields, row.Cells);
            if (!index.TryGetValue(key, out var list))
            {
                list = [];
                index[key] = list;
            }

            list.Add(row);
        }

        return index;
    }

    private static long Estimate(IReadOnlyList<MergedRow> rows)
    {
        long total = 0;
        foreach (var row in rows)
        {
            total += RowOverheadBytes + CellBytes * row.Cells.Length;
            foreach (var cell in row.Cells)
            {
                if (cell is string text)
                {
                    total += CharBytes * text.Length;
                }
            }
        }

        return total;
    }
}
