using TotalWarModTool.Schema;

namespace TotalWarModTool.Objects;

/// <summary>Table (with suffix) and the key values in KeyFields order. Equality is ordinal on the values and ignores case on the table.</summary>
public sealed class ObjectId : IEquatable<ObjectId>
{
    public ObjectId(string table, IReadOnlyList<string> keyValues)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(keyValues);
        Table = table;
        KeyValues = keyValues.ToArray();
        JoinedKey = string.Concat(KeyValues);
    }

    public string Table { get; }

    public IReadOnlyList<string> KeyValues { get; }

    /// <summary>The values joined with "" — the form loc keys use.</summary>
    public string JoinedKey { get; }

    public bool Equals(ObjectId? other)
    {
        if (other is null)
        {
            return false;
        }

        if (!string.Equals(Table, other.Table, StringComparison.OrdinalIgnoreCase) || KeyValues.Count != other.KeyValues.Count)
        {
            return false;
        }

        for (var i = 0; i < KeyValues.Count; i++)
        {
            if (!string.Equals(KeyValues[i], other.KeyValues[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ObjectId);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Table.ToLowerInvariant(), StringComparer.Ordinal);
        foreach (var value in KeyValues)
        {
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => Table + ":" + string.Join('|', KeyValues);
}

public enum ReferenceResolution
{
    None,
    Found,
    NotFound,
    LocOnly,
    Unchecked,
}

/// <summary>Target is set for Found, and for Unchecked when the target table has exactly one key field and the referenced column is that key field. LocOnly is a reference whose target table is unknown to the schema but whose loc text exists (also in section rows, which never ask the table store). LocText is the loc text for LocOnly and the display name for Found when one exists.</summary>
public sealed record FieldValue(string Name, FieldType Type, string Text, TableReference? Reference, ReferenceResolution Resolution, ObjectId? Target, string? LocText);

/// <summary>RowId is null for tables without key. OtherEnd is set for rows of junction sections.</summary>
public sealed record ObjectRow(IReadOnlyList<FieldValue> Fields, RowProvenance Provenance, ObjectId? RowId, FieldValue? OtherEnd);

/// <summary>FieldName is the field of SourceTable that points at the root.</summary>
public sealed record ObjectSection(string SourceTable, string FieldName, bool IsJunction, IReadOnlyList<ObjectRow> Rows);

/// <summary>Table without the _tables suffix, as the loc key spells it.</summary>
public sealed record LocEntry(string Table, string Column, string Key, string Text, TableSource Source);

public enum ObjectWarningKind
{
    RootNotFound,
    SourceProblem,
    TableNotInSchema,
    LoadOrder,
}

/// <summary>Detail is short technical English, not a UI text.</summary>
public sealed record ObjectWarning(ObjectWarningKind Kind, string? Name, string Detail);

/// <summary>The immutable model of one object: its row, its loc texts and the rows of other tables that point at it.</summary>
public sealed record ObjectView(ObjectId Id, ObjectRow? Root, IReadOnlyList<LocEntry> Texts, IReadOnlyList<ObjectSection> Sections,
    IReadOnlyList<ObjectWarning> Warnings, LoadOrder Order);
