using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>
/// One object in the history of a tab. <paramref name="RootPackPath"/> is the pack of the row the chain started from when that pack is not in
/// the active mod list (it is inserted into the load order, spec 5.3); null otherwise. Equality: Table and RootPackPath ignoring case,
/// KeyValues ordinal.
/// </summary>
public sealed record ObjectRef(string Table, IReadOnlyList<string> KeyValues, string? RootPackPath)
{
    /// <summary>The key values joined with "", the form loc keys use.</summary>
    public string JoinedKey => string.Concat(KeyValues);

    /// <summary>The key values as <see cref="ObjectId.DisplayKey"/> joins them, the form the tab title and the progress title show.</summary>
    public string DisplayKey => ToObjectId().DisplayKey;

    public ObjectId ToObjectId() => new(Table, KeyValues);

    public bool Equals(ObjectRef? other) =>
        other is not null
        && string.Equals(Table, other.Table, StringComparison.OrdinalIgnoreCase)
        && string.Equals(RootPackPath, other.RootPackPath, StringComparison.OrdinalIgnoreCase)
        && KeyValues.SequenceEqual(other.KeyValues, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Table, StringComparer.OrdinalIgnoreCase);
        hash.Add(RootPackPath, StringComparer.OrdinalIgnoreCase);
        foreach (var value in KeyValues)
        {
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
