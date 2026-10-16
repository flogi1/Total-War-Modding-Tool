namespace ParadoxModTool.Settings;

/// <summary>Kennungen der Einträge der allgemeinen (kleinen) Werkzeugleiste (gespeichert in der Sitzung, daher nie umbenennen).</summary>
public static class ToolbarItemIds
{
    public const string New = "new";
    public const string Open = "open";
    public const string Save = "save";
    public const string SaveAll = "saveAll";
    public const string Close = "close";
    public const string Move = "move";
    public const string Undo = "undo";
    public const string Redo = "redo";
    public const string Search = "search";
    public const string GoToLine = "goToLine";
    public const string GoToBracket = "goToBracket";
    public const string ScopeFinder = "scopeFinder";
    public const string GuiDataHelper = "guiDataHelper";
    public const string Templates = "templates";
    public const string Notes = "notes";
    public const string Bookmark = "bookmark";
    public const string Bug = "bug";
    public const string BugTracker = "bugTracker";
    public const string Format = "format";
    public const string SplitView = "splitView";
    public const string Explorer = "explorer";
    public const string NotesPanel = "notesPanel";
    public const string AutoComplete = "autoComplete";
    public const string Hover = "hover";
    public const string ModifierReload = "modifierReload";
    public const string LaunchOption = "launchOption";
    public const string Playset = "playset";
    public const string LaunchGame = "launchGame";
}

/// <summary>Gruppen der allgemeinen Werkzeugleiste (klassisches Layout); zwischen zwei Gruppen steht eine Trennlinie.</summary>
public enum ToolbarGroup
{
    Files,
    History,
    Tools,
    Toggles,
    Launch,
}

public sealed record ToolbarItem(string Id, ToolbarGroup Group);

/// <summary>
/// Alle Einträge der allgemeinen Werkzeugleiste in der Reihenfolge der Spec (UI-Reorganisation, erweitert im Interface-Rework 2 um die
/// Dateiknöpfe der Arbeitsbereiche). Im neuen Layout bestimmt der Nutzer die Reihenfolge; Trennlinien sind dort eigene Einträge
/// <c>sep:&lt;n&gt;</c>.
/// </summary>
public static class ToolbarCatalog
{
    public const string SeparatorPrefix = "sep:";

    public static IReadOnlyList<ToolbarItem> All { get; } = new[]
    {
        new ToolbarItem(ToolbarItemIds.New, ToolbarGroup.Files),
        new ToolbarItem(ToolbarItemIds.Open, ToolbarGroup.Files),
        new ToolbarItem(ToolbarItemIds.Save, ToolbarGroup.Files),
        new ToolbarItem(ToolbarItemIds.SaveAll, ToolbarGroup.Files),
        new ToolbarItem(ToolbarItemIds.Close, ToolbarGroup.Files),
        new ToolbarItem(ToolbarItemIds.Move, ToolbarGroup.Files),
        new ToolbarItem(ToolbarItemIds.Undo, ToolbarGroup.History),
        new ToolbarItem(ToolbarItemIds.Redo, ToolbarGroup.History),
        new ToolbarItem(ToolbarItemIds.Search, ToolbarGroup.Tools),
        new ToolbarItem(ToolbarItemIds.GoToLine, ToolbarGroup.Tools),
        new ToolbarItem(ToolbarItemIds.GoToBracket, ToolbarGroup.Tools),
        new ToolbarItem(ToolbarItemIds.ScopeFinder, ToolbarGroup.Tools),
        new ToolbarItem(ToolbarItemIds.GuiDataHelper, ToolbarGroup.Tools),
        new ToolbarItem(ToolbarItemIds.Templates, ToolbarGroup.Tools),
        new ToolbarItem(ToolbarItemIds.Format, ToolbarGroup.Tools),
        new ToolbarItem(ToolbarItemIds.Notes, ToolbarGroup.Tools),
        new ToolbarItem(ToolbarItemIds.Bookmark, ToolbarGroup.Tools),
        new ToolbarItem(ToolbarItemIds.Bug, ToolbarGroup.Tools),
        new ToolbarItem(ToolbarItemIds.BugTracker, ToolbarGroup.Tools),
        new ToolbarItem(ToolbarItemIds.SplitView, ToolbarGroup.Toggles),
        new ToolbarItem(ToolbarItemIds.Explorer, ToolbarGroup.Toggles),
        new ToolbarItem(ToolbarItemIds.NotesPanel, ToolbarGroup.Toggles),
        new ToolbarItem(ToolbarItemIds.AutoComplete, ToolbarGroup.Toggles),
        new ToolbarItem(ToolbarItemIds.Hover, ToolbarGroup.Toggles),
        new ToolbarItem(ToolbarItemIds.ModifierReload, ToolbarGroup.Toggles),
        new ToolbarItem(ToolbarItemIds.LaunchOption, ToolbarGroup.Launch),
        new ToolbarItem(ToolbarItemIds.Playset, ToolbarGroup.Launch),
        new ToolbarItem(ToolbarItemIds.LaunchGame, ToolbarGroup.Launch),
    };

    /// <summary>Standardreihenfolge der kleinen Werkzeugleiste im neuen Layout, mit fünf Trennlinien.</summary>
    public static IReadOnlyList<string> DefaultOrder { get; } = new[]
    {
        ToolbarItemIds.New, ToolbarItemIds.Open, ToolbarItemIds.Save, ToolbarItemIds.SaveAll, ToolbarItemIds.Close, ToolbarItemIds.Move,
        Separator(1),
        ToolbarItemIds.Undo, ToolbarItemIds.Redo,
        Separator(2),
        ToolbarItemIds.Search, ToolbarItemIds.GoToLine, ToolbarItemIds.GoToBracket, ToolbarItemIds.ScopeFinder, ToolbarItemIds.GuiDataHelper, ToolbarItemIds.Templates, ToolbarItemIds.Format,
        Separator(3),
        ToolbarItemIds.Notes, ToolbarItemIds.Bookmark, ToolbarItemIds.Bug, ToolbarItemIds.BugTracker,
        Separator(4),
        ToolbarItemIds.SplitView, ToolbarItemIds.Explorer, ToolbarItemIds.NotesPanel, ToolbarItemIds.AutoComplete, ToolbarItemIds.Hover, ToolbarItemIds.ModifierReload,
        Separator(5),
        ToolbarItemIds.LaunchOption, ToolbarItemIds.Playset, ToolbarItemIds.LaunchGame,
    };

    /// <summary>Einträge, die im neuen Layout als Kacheln stehen und deshalb in der kleinen Leiste zunächst ausgeblendet sind.</summary>
    public static IReadOnlyList<string> DefaultHiddenInTilesLayout { get; } = new[]
    {
        ToolbarItemIds.ScopeFinder, ToolbarItemIds.GuiDataHelper, ToolbarItemIds.Templates, ToolbarItemIds.BugTracker,
    };

    private static readonly HashSet<string> Known = All.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string id) => Known.Contains(id);

    /// <summary>Trennlinien-Kennung <c>sep:&lt;n&gt;</c> mit n ≥ 1; alles andere (auch <c>sep:</c>, <c>sep:0</c>, <c>sep:x</c>) ist keine.</summary>
    public static bool IsSeparator(string? id) =>
        id is not null
        && id.StartsWith(SeparatorPrefix, StringComparison.Ordinal)
        && int.TryParse(id.AsSpan(SeparatorPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
        && n >= 1;

    public static string Separator(int n) => SeparatorPrefix + n;
}
