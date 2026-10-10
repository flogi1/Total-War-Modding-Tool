namespace TotalWarModTool.App;

/// <summary>One colour of the theme with its light and dark value (<c>#RRGGBB</c>). The resource key is <see cref="ThemePalette.ResourcePrefix"/> plus <paramref name="Key"/>.</summary>
public sealed record ThemeToken(string Key, string Light, string Dark);

/// <summary>The colours of the built-in light and dark theme. The only place in the app where colour values are written down.</summary>
public static class ThemePalette
{
    public const string ResourcePrefix = "Theme.";

    public static IReadOnlyList<ThemeToken> All { get; } =
    [
        new("WindowBackground", "#F0F0F0", "#1E1F22"),
        new("PanelBackground", "#FFFFFF", "#2B2D30"),
        new("ControlBackground", "#E1E1E1", "#3C3F41"),
        new("ControlHover", "#E5F1FB", "#4A4E52"),
        new("ControlPressed", "#CCE4F7", "#5A5F63"),
        new("ControlBorder", "#ADADAD", "#5C6064"),
        new("Text", "#000000", "#DFE1E5"),
        new("TextMuted", "#808080", "#9AA0A6"),
        new("TextDisabled", "#8A8A8A", "#6F747A"),
        new("Accent", "#0078D7", "#4B9CE2"),
        new("AccentText", "#FFFFFF", "#FFFFFF"),
        new("Selection", "#CCE8FF", "#2F4F73"),
        new("SelectionText", "#000000", "#FFFFFF"),
        new("InactiveSelection", "#E0E0E0", "#3A3D41"),
        new("Separator", "#A0A0A0", "#4A4D50"),
        new("Warning", "#B45309", "#E0A040"),
        new("ObjectVanilla", "#2E7D32", "#7CC47F"),
        new("ObjectMod", "#1565C0", "#6FA8DC"),
        new("ObjectProject", "#AD1457", "#E57FB3"),
        new("ObjectLocText", "#5D4037", "#C9A27E"),
        new("ObjectTrailing", "#6B7280", "#8B949E"),
        new("ObjectError", "#C62828", "#F28B82"),
    ];
}
