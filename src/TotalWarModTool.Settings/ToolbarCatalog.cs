using System.Globalization;

namespace TotalWarModTool.Settings;

/// <summary>Ids of the entries in the small toolbar. Stored in settings.json, so never rename them.</summary>
public static class ToolbarItemIds
{
    public const string NewProject = "newProject";
    public const string OpenProject = "openProject";
    public const string ImportPack = "importPack";
    public const string NewTable = "newTable";
    public const string NewLoc = "newLoc";
    public const string Save = "save";
    public const string SaveAll = "saveAll";
    public const string Undo = "undo";
    public const string Redo = "redo";
    public const string RefreshPacks = "refreshPacks";
    public const string Build = "build";
    public const string BuildAndInstall = "buildAndInstall";
}

/// <summary>
/// All entries of the small toolbar. The user chooses the order; separators are entries of their own with the id
/// <c>sep:&lt;n&gt;</c> and are not part of <see cref="All"/>.
/// </summary>
public static class ToolbarCatalog
{
    public const string SeparatorPrefix = "sep:";

    /// <summary>Every real entry (no separators) in catalog order.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        ToolbarItemIds.NewProject, ToolbarItemIds.OpenProject, ToolbarItemIds.ImportPack, ToolbarItemIds.NewTable, ToolbarItemIds.NewLoc,
        ToolbarItemIds.Save, ToolbarItemIds.SaveAll,
        ToolbarItemIds.Undo, ToolbarItemIds.Redo,
        ToolbarItemIds.RefreshPacks,
        ToolbarItemIds.Build, ToolbarItemIds.BuildAndInstall,
    };

    /// <summary>Default order of the toolbar, with four separators.</summary>
    public static IReadOnlyList<string> DefaultOrder { get; } = new[]
    {
        ToolbarItemIds.NewProject, ToolbarItemIds.OpenProject, ToolbarItemIds.ImportPack, ToolbarItemIds.NewTable, ToolbarItemIds.NewLoc,
        Separator(1),
        ToolbarItemIds.Save, ToolbarItemIds.SaveAll,
        Separator(2),
        ToolbarItemIds.Undo, ToolbarItemIds.Redo,
        Separator(3),
        ToolbarItemIds.RefreshPacks,
        Separator(4),
        ToolbarItemIds.Build, ToolbarItemIds.BuildAndInstall,
    };

    private static readonly HashSet<string> Known = All.ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string id) => Known.Contains(id);

    /// <summary>Separator id <c>sep:&lt;n&gt;</c> with n ≥ 1; anything else (also <c>sep:</c>, <c>sep:0</c>, <c>sep:x</c>) is not one.</summary>
    public static bool IsSeparator(string? id) =>
        id is not null
        && id.StartsWith(SeparatorPrefix, StringComparison.Ordinal)
        && int.TryParse(id.AsSpan(SeparatorPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
        && n >= 1;

    public static string Separator(int n) => SeparatorPrefix + n.ToString(CultureInfo.InvariantCulture);
}
