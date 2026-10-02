using System.Windows;
using System.Windows.Media;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

/// <summary>
/// Writes the effective theme into a resource dictionary as frozen brushes <c>Theme.&lt;Token&gt;</c> and maps the Windows system
/// colours onto it. Styles and windows reference the tokens with <c>DynamicResource</c>, so applying a theme takes effect at once.
/// Apply the theme before the first window is created.
/// </summary>
public sealed class ThemeManager
{
    private readonly ResourceDictionary _resources;
    private readonly ISystemThemeSource _systemTheme;

    public ThemeManager(ResourceDictionary resources, ISystemThemeSource systemTheme)
    {
        _resources = resources;
        _systemTheme = systemTheme;
    }

    /// <summary>Whether the theme applied last is the dark one. False before the first <see cref="Apply"/>.</summary>
    public bool IsDark { get; private set; }

    /// <summary><see cref="ThemeChoice.System"/> resolves through the system theme source.</summary>
    public void Apply(ThemeChoice choice)
    {
        IsDark = choice switch
        {
            ThemeChoice.Dark => true,
            ThemeChoice.Light => false,
            _ => _systemTheme.IsDark(),
        };

        var colors = ThemePalette.All.ToDictionary(token => token.Key, token => ParseColor(IsDark ? token.Dark : token.Light));
        foreach (var (key, color) in colors)
        {
            _resources[ThemePalette.ResourcePrefix + key] = Freeze(color);
        }

        MapSystemColors(colors);
    }

    /// <summary>
    /// Overrides the Windows system colours that the default control templates fall back to (text selection, the scroll bar corner, ...),
    /// so places without a custom style also match the theme.
    /// </summary>
    private void MapSystemColors(IReadOnlyDictionary<string, Color> colors)
    {
        void Set(ResourceKey brushKey, ResourceKey colorKey, string token)
        {
            _resources[brushKey] = Freeze(colors[token]);
            _resources[colorKey] = colors[token];
        }

        Set(SystemColors.WindowBrushKey, SystemColors.WindowColorKey, "PanelBackground");
        Set(SystemColors.WindowTextBrushKey, SystemColors.WindowTextColorKey, "Text");
        Set(SystemColors.ControlBrushKey, SystemColors.ControlColorKey, "WindowBackground");
        Set(SystemColors.ControlTextBrushKey, SystemColors.ControlTextColorKey, "Text");
        Set(SystemColors.ControlDarkBrushKey, SystemColors.ControlDarkColorKey, "ControlBorder");
        Set(SystemColors.ControlLightBrushKey, SystemColors.ControlLightColorKey, "ControlBackground");
        Set(SystemColors.HighlightBrushKey, SystemColors.HighlightColorKey, "Accent");
        Set(SystemColors.HighlightTextBrushKey, SystemColors.HighlightTextColorKey, "AccentText");
        _resources[SystemColors.InactiveSelectionHighlightBrushKey] = Freeze(colors["InactiveSelection"]);
        _resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Freeze(colors["Text"]);
        Set(SystemColors.GrayTextBrushKey, SystemColors.GrayTextColorKey, "TextDisabled");
        Set(SystemColors.MenuBrushKey, SystemColors.MenuColorKey, "PanelBackground");
        Set(SystemColors.MenuTextBrushKey, SystemColors.MenuTextColorKey, "Text");
        Set(SystemColors.InfoBrushKey, SystemColors.InfoColorKey, "ControlBackground");
        Set(SystemColors.InfoTextBrushKey, SystemColors.InfoTextColorKey, "Text");
    }

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
