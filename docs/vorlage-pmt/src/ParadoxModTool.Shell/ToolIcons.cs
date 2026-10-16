using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using ParadoxModTool.Settings;

namespace ParadoxModTool.Shell;

/// <summary>
/// Vektor-Icons für Kacheln (<see cref="TileIds"/>) und Einträge der kleinen Werkzeugleiste (<see cref="ToolbarItemIds"/>): je Kennung ein
/// Strich-Pfad im 24×24-Raster (Pfad-Minisprache), gezeichnet in der Farbe eines Theme-Schlüssels. Gleichnamige Kennungen beider Kataloge
/// (Scope Finder, GUI-Datenhelfer, Template-Generator, Bugtracker) teilen einen Eintrag.
/// </summary>
public static class ToolIcons
{
    public const double DefaultSize = 24;

    private const string Fallback = "M4 4h16v16H4z";

    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        // Kacheln
        [TileIds.Project] = "M3 7h6l2 2h10v11H3z M9 14h6 M13 12l2 2-2 2",
        [TileIds.CuTool] = "M4 4h7v16H4z M13 4h7v16h-7z M7 9v6 M16 9v6",
        [TileIds.Compatch] = "M4 4h11v11H4z M9 9h11v11H9z",
        [TileIds.ModCompat] = "M12 3l8 3v6c0 5-4 8-8 9-4-1-8-4-8-9V6z M8 12l3 3 5-6",
        [TileIds.Playsets] = "M3 6h12 M3 12h12 M3 18h8 M18 14v6 M15 17h6",
        [TileIds.PatchRecipes] = "M4 5h16v14H4z M7 9h6 M7 12h10 M7 15h4 M15 15l2 2 3-3",
        [TileIds.SaveExplorer] = "M5 3h9l4 4v5 M5 3v18h6 M18 15a3 3 0 1 1-6 0a3 3 0 1 1 6 0 M17.2 17.2l3.8 3.8",
        [TileIds.ErrorLog] = "M12 4l9 16H3z M12 10v4 M12 17v1",
        [TileIds.ScriptDocs] = "M4 4h7a2 2 0 0 1 2 2v14a2 2 0 0 0-2-2H4z M20 4h-7a2 2 0 0 0-2 2v14a2 2 0 0 1 2-2h7z",
        [TileIds.Gui] = "M3 4h18v16H3z M3 8h18 M9 8v12",
        [TileIds.GeneratorPackages] = "M3 8l9-4 9 4v9l-9 4-9-4z M3 8l9 4 9-4 M12 12v9",
        [TileIds.PackageEditor] = "M3 8l9-4 9 4v4 M3 8l9 4 9-4 M3 8v9l9 4 M12 12v9 M14 20l6-6 2 2-6 6h-2z",
        [TileIds.Desync] = "M3 5h8v10H3z M13 5h8v10h-8z M7 15v4 M17 15v4 M4 19h16",

        // Kleine Werkzeugleiste
        [ToolbarItemIds.New] = "M6 3h8l5 5v13H6z M14 3v5h5 M12 11v6 M9 14h6",
        [ToolbarItemIds.Open] = "M3 6h6l2 2h10v4 M3 6v13h16l2-8H6l-3 8",
        [ToolbarItemIds.Save] = "M4 4h13l3 3v13H4z M8 4v5h8V4 M7 20v-6h10v6",
        [ToolbarItemIds.SaveAll] = "M7 3h10l3 3v10H7z M10 3v4h6V3 M4 8v13h13",
        [ToolbarItemIds.Close] = "M6 3h8l5 5v13H6z M14 3v5h5 M9 12l6 6 M15 12l-6 6",
        [ToolbarItemIds.Move] = "M3 9h14l-4-4 M21 15H7l4 4",
        [ToolbarItemIds.Undo] = "M9 4L4 9l5 5 M4 9h10a6 6 0 0 1 0 12h-4",
        [ToolbarItemIds.Redo] = "M15 4l5 5-5 5 M20 9H10a6 6 0 0 0 0 12h4",
        [ToolbarItemIds.Search] = "M15 15l5 5 M16 10a6 6 0 1 1-12 0a6 6 0 1 1 12 0",
        [ToolbarItemIds.GoToLine] = "M4 6h16 M4 12h8 M4 18h16 M16 10l3 2-3 2",
        [ToolbarItemIds.GoToBracket] = "M9 4H5v16h4 M15 4h4v16h-4 M9 12h6",
        [ToolbarItemIds.ScopeFinder] = "M5 5h4v4H5z M15 15h4v4h-4z M9 7h4v6h2",
        [ToolbarItemIds.GuiDataHelper] = "M8 4H4v16h4 M16 4h4v16h-4 M10 10h4 M10 14h4",
        [ToolbarItemIds.Templates] = "M4 4h7v7H4z M13 4h7v7h-7z M4 13h7v7H4z M16.5 14v6 M13.5 17h6",
        [ToolbarItemIds.Format] = "M4 6h16 M4 10h10 M4 14h16 M4 18h10",
        [ToolbarItemIds.Notes] = "M5 3h14v18H5z M8 8h8 M8 12h8 M8 16h5",
        [ToolbarItemIds.Bookmark] = "M6 3h12v18l-6-4-6 4z",
        [ToolbarItemIds.Bug] = "M9 8a3 3 0 0 1 6 0 M8 11h8v5a4 4 0 0 1-8 0z M4 12h4 M16 12h4 M5 19l3-2 M19 19l-3-2 M12 11v9",
        [ToolbarItemIds.BugTracker] = "M7 4h10v17H7z M10 3h4v3h-4z M10 11a2 2 0 0 1 4 0v3a2 2 0 0 1-4 0z M8 13h2 M14 13h2",
        [ToolbarItemIds.SplitView] = "M3 4h18v16H3z M12 4v16",
        [ToolbarItemIds.Explorer] = "M5 4v16 M5 8h6 M5 14h6 M13 6h7 M13 12h7 M13 18h7",
        [ToolbarItemIds.NotesPanel] = "M3 4h18v16H3z M15 4v16 M17 8h2 M17 11h2",
        [ToolbarItemIds.AutoComplete] = "M4 18l4-12 4 12 M5.5 14h5 M15 10a3 3 0 1 1 0 6 M18 10v8",
        [ToolbarItemIds.Hover] = "M4 4h16v11h-8l-4 4v-4H4z M8 8h8 M8 11h5",
        [ToolbarItemIds.ModifierReload] = "M19 12a7 7 0 1 1-2-5 M17 3v4h-4",
        [ToolbarItemIds.LaunchOption] = "M4 7h16 M4 12h16 M4 17h16 M8 5v4 M15 10v4 M10 15v4",
        [ToolbarItemIds.Playset] = "M12 4l9 5-9 5-9-5z M3 13l9 5 9-5 M3 17l9 4 9-4",
        [ToolbarItemIds.LaunchGame] = "M7 4l13 8-13 8z",
    };

    public static bool Has(string id) => Paths.ContainsKey(id);

    /// <summary>Pfad-Minisprache des Icons (24×24), <see langword="null"/> für unbekannte Kennungen.</summary>
    public static string? PathData(string id) => Paths.GetValueOrDefault(id);

    /// <summary>
    /// Ein skaliertes Icon: <see cref="Viewbox"/> der Kantenlänge <paramref name="size"/> mit einem Strich-<see cref="Path"/>, dessen Farbe dem
    /// Theme-Schlüssel <paramref name="brushKey"/> folgt (z. B. <c>Theme.Accent</c>). Unbekannte Kennungen zeigen ein Quadrat.
    /// </summary>
    public static FrameworkElement Create(string id, double size, string brushKey) =>
        Build(id, size, path => path.SetResourceReference(Shape.StrokeProperty, brushKey));

    /// <summary>
    /// Wie <see cref="Create(string, double, string)"/>, aber in der Farbe <paramref name="disabledBrushKey"/>, sobald das Icon (mit seinem
    /// Knopf) deaktiviert ist - z. B. Kacheln: Akzentfarbe, deaktiviert grau wie die Beschriftung.
    /// </summary>
    public static FrameworkElement Create(string id, double size, string brushKey, string disabledBrushKey) =>
        Build(id, size, path =>
        {
            // Über einen Stil statt eines lokalen Werts, sonst käme der Auslöser nicht gegen ihn an.
            var style = new Style(typeof(Path));
            style.Setters.Add(new Setter(Shape.StrokeProperty, new DynamicResourceExtension(brushKey)));
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Shape.StrokeProperty, new DynamicResourceExtension(disabledBrushKey)));
            style.Triggers.Add(disabled);
            path.Style = style;
        });

    /// <summary>
    /// Ein Icon in der Vordergrundfarbe seines Knopfs (geerbtes <see cref="TextElement.ForegroundProperty"/>): so wirken die Theme-Stile für
    /// deaktiviert und eingerastet (Controls.xaml) auf Icon und Beschriftung gleich.
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
