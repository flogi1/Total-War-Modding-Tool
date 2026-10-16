using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Shell;

/// <summary>
/// Vector icons for tiles (<see cref="TileIds"/>) and entries of the small toolbar (<see cref="ToolbarItemIds"/>): one stroke path per id on a
/// 24×24 grid (path mini language), drawn in the colour of a theme key.
/// </summary>
public static class ToolIcons
{
    public const double DefaultSize = 24;

    private const string Fallback = "M4 4h16v16H4z";

    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        // Tiles
        [TileIds.NewBuilding] = "M3 11l9-7 9 7 M5 10v10h14V10 M10 20v-6h4v6",
        [TileIds.NewTrait] = "M12 3l2.8 5.7 6.2.9-4.5 4.4 1.1 6.2L12 17.3 6.4 20.2l1.1-6.2L3 9.6l6.2-.9z",

        // Small toolbar
        [ToolbarItemIds.NewProject] = "M6 3h8l5 5v13H6z M14 3v5h5 M12 11v6 M9 14h6",
        [ToolbarItemIds.OpenProject] = "M3 6h6l2 2h10v4 M3 6v13h16l2-8H6l-3 8",
        [ToolbarItemIds.ImportPack] = "M4 12v8h16v-8 M4 12h4 M16 12h4 M12 3v10 M8.5 9.5L12 13l3.5-3.5",
        [ToolbarItemIds.NewTable] = "M3 4h14v14H3z M3 9h14 M3 13.5h14 M8 4v14 M18.5 15v6 M15.5 18h6",
        [ToolbarItemIds.NewLoc] = "M4 4h16v12h-7l-4 4v-4H4z M12 7v6 M9 10h6",
        [ToolbarItemIds.Save] = "M4 4h13l3 3v13H4z M8 4v5h8V4 M7 20v-6h10v6",
        [ToolbarItemIds.SaveAll] = "M7 3h10l3 3v10H7z M10 3v4h6V3 M4 8v13h13",
        [ToolbarItemIds.Undo] = "M9 4L4 9l5 5 M4 9h10a6 6 0 0 1 0 12h-4",
        [ToolbarItemIds.Redo] = "M15 4l5 5-5 5 M20 9H10a6 6 0 0 0 0 12h4",
        [ToolbarItemIds.RefreshPacks] = "M19 12a7 7 0 1 1-2-5 M17 3v4h-4",
        [ToolbarItemIds.Build] = "M12 4l8 8-3 3-8-8z M10 13l-5 5 M5 18l2 2",
        [ToolbarItemIds.BuildAndInstall] = "M5 3l6 6-2.5 2.5-6-6z M8.5 9.5L4 14 M17 11v9 M13.5 16.5L17 20l3.5-3.5",
    };

    public static bool Has(string id) => Paths.ContainsKey(id);

    /// <summary>Path mini language of the icon (24×24), <see langword="null"/> for unknown ids.</summary>
    public static string? PathData(string id) => Paths.GetValueOrDefault(id);

    /// <summary>
    /// A scaled icon: a <see cref="Viewbox"/> of edge length <paramref name="size"/> holding a stroked <see cref="Path"/> whose colour follows
    /// the theme key <paramref name="brushKey"/> (for example <c>Theme.Accent</c>). With <paramref name="disabledBrushKey"/> the icon switches
    /// to that colour as soon as it (with its button) is disabled, so tiles are accent-coloured normally and grey like their label when disabled.
    /// Unknown ids show a square.
    /// </summary>
    public static FrameworkElement Create(string id, double size, string brushKey, string? disabledBrushKey = null) =>
        Build(id, size, path =>
        {
            if (disabledBrushKey is null)
            {
                path.SetResourceReference(Shape.StrokeProperty, brushKey);
                return;
            }

            // Through a style rather than a local value, otherwise the trigger could not override it.
            var style = new Style(typeof(Path));
            style.Setters.Add(new Setter(Shape.StrokeProperty, new DynamicResourceExtension(brushKey)));
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Shape.StrokeProperty, new DynamicResourceExtension(disabledBrushKey)));
            style.Triggers.Add(disabled);
            path.Style = style;
        });

    /// <summary>
    /// An icon in the foreground colour of its button (inherited <see cref="TextElement.ForegroundProperty"/>), so the theme styles for disabled
    /// and checked (Controls.xaml) act on icon and label alike.
    /// </summary>
    public static FrameworkElement CreateInForeground(string id, double size) =>
        Build(id, size, path => path.SetBinding(Shape.StrokeProperty, new Binding
        {
            RelativeSource = RelativeSource.Self,
            Path = new PropertyPath(TextElement.ForegroundProperty),
        }));

    private static FrameworkElement Build(string id, double size, Action<Path> paint)
    {
        var path = new Path
        {
            Data = Geometry.Parse(PathData(id) ?? Fallback),
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = null,
        };
        paint(path);

        var canvas = new Canvas { Width = DefaultSize, Height = DefaultSize };
        canvas.Children.Add(path);
        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
            Tag = id,
        };
    }
}
