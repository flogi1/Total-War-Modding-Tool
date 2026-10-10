namespace TotalWarModTool.Objects;

public enum RowChangeKind
{
    Set,
    Insert,
    Delete,
}

/// <summary>
/// Table with suffix. RootField is the field that points at the root and RootValue the root's key value for it (null for the root row itself).
/// Cells holds every field of the table in schema order as text: for Set the baseline values with the changed ones replaced, for Insert the
/// parsed values with defaults for the rest, for Delete the baseline values. ChangedFields names the fields that differ (Set only).
/// Provenance is the baseline row's (Set, Delete) or null (Insert). Line is the text line of the row head (0 for a row of a section the
/// text lacks). PreviousCells holds every field of the baseline row in schema order as stored (Set, Delete), null for Insert.
/// </summary>
public sealed record RowChange(RowChangeKind Kind, string Table, string? RootField, string? RootValue, IReadOnlyList<string> Cells,
    IReadOnlyList<string> ChangedFields, RowProvenance? Provenance, int Line, IReadOnlyList<string>? PreviousCells = null);

public enum TextChangeKind
{
    Set,
    Insert,
}

/// <summary>Key is the full loc key (table_column_ownerKey). Source is the baseline entry's source (Set) or null (Insert).</summary>
public sealed record TextChange(TextChangeKind Kind, string Key, string Text, TableSource? Source, int Line);

public sealed record ObjectChanges(IReadOnlyList<RowChange> Rows, IReadOnlyList<TextChange> Texts, IReadOnlyList<BlockDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public bool IsEmpty => Rows.Count == 0 && Texts.Count == 0;
}

/// <summary>Answers whether a value exists in the target column of a table over the load order.</summary>
public delegate bool ReferenceExists(string table, string column, string value);
