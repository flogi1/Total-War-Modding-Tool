using System.Globalization;
using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables;

public enum CurrentDefinitionKind
{
    /// <summary>The definition has the version the game uses.</summary>
    GameVersion,

    /// <summary>The game does not have the table; the definition is the highest schema version.</summary>
    SchemaNewest,

    /// <summary>The game uses a version the schema does not know; there is no definition.</summary>
    SchemaLacksGameVersion,

    /// <summary>The table is not in the schema; there is no definition.</summary>
    NotInSchema,
}

public sealed record CurrentDefinition(TableDefinition? Definition, CurrentDefinitionKind Kind, int? GameVersion);

/// <summary>Which definition of a table is the current one: the version the game uses, never a guess when the game's version is known.</summary>
public static class TableVersionRule
{
    public static CurrentDefinition Current(GameSchema schema, GameTableVersions versions, string table)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(table);

        if (!schema.Tables.TryGetValue(table, out var definitions) || definitions.Count == 0)
        {
            return new CurrentDefinition(null, CurrentDefinitionKind.NotInSchema, versions.VersionOf(table));
        }

        var gameVersion = versions.VersionOf(table);
        if (gameVersion is { } wanted)
        {
            var match = schema.Find(table, wanted);
            return match is null
                ? new CurrentDefinition(null, CurrentDefinitionKind.SchemaLacksGameVersion, wanted)
                : new CurrentDefinition(match, CurrentDefinitionKind.GameVersion, wanted);
        }

        // Highest version; with equal versions the first one wins.
        var newest = definitions[0];
        foreach (var definition in definitions)
        {
            if (definition.Version > newest.Version)
            {
                newest = definition;
            }
        }

        return new CurrentDefinition(newest, CurrentDefinitionKind.SchemaNewest, null);
    }

    /// <summary>The build problem text for a table file in another version than the game uses; null when the game lacks the table or the version fits.</summary>
    public static string? MismatchDetail(GameTableVersions versions, string table, int fileVersion)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(table);

        if (versions.VersionOf(table) is not { } gameVersion || gameVersion == fileVersion)
        {
            return null;
        }

        return "the table file has version " + fileVersion.ToString(CultureInfo.InvariantCulture)
            + ", the game uses version " + gameVersion.ToString(CultureInfo.InvariantCulture)
            + "; the game crashes on this file. Delete the file or create the table again.";
    }
}
