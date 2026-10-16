namespace TotalWarModTool.Settings;

/// <summary>Ids of the tiles in the tile bar. Stored in settings.json, so never rename them.</summary>
public static class TileIds
{
    public const string NewBuilding = "newBuilding";
    public const string NewTrait = "newTrait";
}

/// <summary>
/// One tile. <paramref name="DefaultVisible"/> applies without stored settings; <paramref name="HasMenu"/> makes a click open a
/// submenu instead of running a command.
/// </summary>
public sealed record TileItem(string Id, bool DefaultVisible, bool HasMenu);

/// <summary>All tiles in their default order.</summary>
public static class TileCatalog
{
    public static IReadOnlyList<TileItem> All { get; } = new[]
    {
        new TileItem(TileIds.NewBuilding, true, false),
        new TileItem(TileIds.NewTrait, true, false),
    };

    public static IReadOnlyList<string> DefaultOrder { get; } = All.Select(t => t.Id).ToList();

    private static readonly Dictionary<string, TileItem> ById = All.ToDictionary(t => t.Id, StringComparer.Ordinal);

    public static bool IsKnown(string id) => ById.ContainsKey(id);

    public static TileItem? Find(string id) => ById.GetValueOrDefault(id);
}
