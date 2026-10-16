namespace ParadoxModTool.Settings;

/// <summary>Kennungen der Kacheln in der Kachelleiste (gespeichert in der Sitzung, daher nie umbenennen).</summary>
public static class TileIds
{
    public const string Project = "project";
    public const string CuTool = "cuTool";
    public const string Compatch = "compatch";
    public const string ModCompat = "modCompat";
    public const string Playsets = "playsets";
    public const string PatchRecipes = "patchRecipes";
    public const string SaveExplorer = "saveExplorer";
    public const string ErrorLog = "errorLog";
    public const string BugTracker = "bugTracker";
    public const string ScriptDocs = "scriptDocs";
    public const string ScopeFinder = "scopeFinder";
    public const string Gui = "gui";
    public const string GuiDataHelper = "guiDataHelper";
    public const string Templates = "templates";
    public const string GeneratorPackages = "generatorPackages";
    public const string PackageEditor = "packageEditor";
    public const string Desync = "desync";
}

/// <summary>Welche Zahl eine Kachel als Abzeichen zeigt.</summary>
public enum TileBadge
{
    None,
    ErrorLog,
    BugTracker,
}

/// <summary>
/// Eine Kachel: <paramref name="DefaultVisible"/> gilt ohne gespeicherte Sitzung, <paramref name="HasMenu"/> öffnet beim Klick ein
/// Untermenü statt eines Fensters, <paramref name="RequiresGuiTools"/> blendet die Kachel aus, wenn die GUI-Werkzeuge aus sind.
/// </summary>
public sealed record TileItem(string Id, bool DefaultVisible, bool HasMenu, TileBadge Badge, bool RequiresGuiTools);

/// <summary>Alle Kacheln in der Standardreihenfolge der Spec "Interface-Rework 2".</summary>
public static class TileCatalog
{
    public static IReadOnlyList<TileItem> All { get; } = new[]
    {
        new TileItem(TileIds.Project, true, true, TileBadge.None, false),
        new TileItem(TileIds.CuTool, true, false, TileBadge.None, false),
        new TileItem(TileIds.Compatch, true, false, TileBadge.None, false),
        new TileItem(TileIds.ModCompat, true, false, TileBadge.None, false),
        new TileItem(TileIds.Playsets, true, false, TileBadge.None, false),
        new TileItem(TileIds.PatchRecipes, true, false, TileBadge.None, false),
        new TileItem(TileIds.SaveExplorer, true, false, TileBadge.None, false),
        new TileItem(TileIds.ErrorLog, true, false, TileBadge.ErrorLog, false),
        new TileItem(TileIds.BugTracker, true, false, TileBadge.BugTracker, false),
        new TileItem(TileIds.ScriptDocs, true, false, TileBadge.None, false),
        new TileItem(TileIds.ScopeFinder, true, false, TileBadge.None, false),
        new TileItem(TileIds.Gui, true, true, TileBadge.None, true),
        new TileItem(TileIds.GuiDataHelper, true, false, TileBadge.None, true),
        new TileItem(TileIds.Templates, true, false, TileBadge.None, false),
        new TileItem(TileIds.GeneratorPackages, false, false, TileBadge.None, false),
        new TileItem(TileIds.PackageEditor, false, false, TileBadge.None, false),
        new TileItem(TileIds.Desync, false, false, TileBadge.None, false),
    };

    public static IReadOnlyList<string> DefaultOrder { get; } = All.Select(t => t.Id).ToList();

    private static readonly Dictionary<string, TileItem> ById = All.ToDictionary(t => t.Id, StringComparer.Ordinal);

    public static bool IsKnown(string id) => ById.ContainsKey(id);

    public static TileItem? Find(string id) => ById.GetValueOrDefault(id);
}
