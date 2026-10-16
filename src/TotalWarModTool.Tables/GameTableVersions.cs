namespace TotalWarModTool.Tables;

/// <summary>The table versions the game itself uses (table name to version). Names ignore case.</summary>
public sealed class GameTableVersions
{
    public GameTableVersions(IReadOnlyDictionary<string, int> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        var copy = new Dictionary<string, int>(versions.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (table, version) in versions)
        {
            copy[table] = version;
        }

        Versions = copy;
    }

    public static GameTableVersions Empty { get; } = new(new Dictionary<string, int>());

    public IReadOnlyDictionary<string, int> Versions { get; }

    /// <summary>The version the game uses for this table; null when the game has no such table.</summary>
    public int? VersionOf(string table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Versions.TryGetValue(table, out var version) ? version : null;
    }

    /// <summary>True when both hold the same tables (ignoring case) with the same versions.</summary>
    public bool SameAs(GameTableVersions other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Versions.Count != other.Versions.Count)
        {
            return false;
        }

        foreach (var (table, version) in Versions)
        {
            if (!other.Versions.TryGetValue(table, out var otherVersion) || otherVersion != version)
            {
                return false;
            }
        }

        return true;
    }
}
