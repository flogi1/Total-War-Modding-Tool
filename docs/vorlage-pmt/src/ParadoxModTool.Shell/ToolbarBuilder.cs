using System.Windows;
using System.Windows.Controls;
using ParadoxModTool.Settings;

namespace ParadoxModTool.Shell;

/// <summary>
/// Was der Builder für einen Aufbau braucht: die Reihenfolge (Kennungen und <c>sep:n</c>), die Sichtbarkeit je Kennung, die bereits erzeugten
/// Elemente je Kennung (<see langword="null"/> = überspringen), die Ausrichtung, optional der Griff am Anfang und die Umordnungsaktion
/// (mit ihr wird jeder Eintrag Ziehquelle und -ziel).
/// </summary>
public sealed record ToolbarBuild(
    IReadOnlyList<string> Order,
    Func<string, bool> IsVisible,
    Func<string, FrameworkElement?> Element,
    bool Vertical,
    FrameworkElement? Grip = null,
    Action<string, string>? MoveNextTo = null);

/// <summary>
/// Baut die kleine Werkzeugleiste aus vorhandenen Elementen in der Reihenfolge des Nutzers. Die Elemente gehören dem Hauptfenster und
/// bleiben über alle Aufbauten dieselben; der Builder löst sie aus dem alten Panel und hängt sie ins neue. Jedes platzierte Kind trägt die
/// Kennung in <c>Tag</c>, der Griff <c>"grip"</c>.
/// </summary>
public static class ToolbarBuilder
{
    public const double VerticalWidth = 44;

    /// <summary>Leisten-Kennung in den Ziehdaten; Kacheln (<see cref="TileBar.ListKey"/>) werden hier nie angenommen.</summary>
    public const string ListKey = "toolbar";

    private const string GripTag = "grip";

    public static Panel Build(ToolbarBuild build)
    {
        Panel panel = build.Vertical
            ? new StackPanel { Orientation = Orientation.Vertical, Width = VerticalWidth }
            : new WrapPanel { Orientation = Orientation.Horizontal };

        if (build.Grip is { } grip)
        {
            Detach(grip);
            grip.Tag = GripTag;
            grip.Margin = new Thickness(2);
            panel.Children.Add(grip);
        }

        // Unbekannte Kennungen (kein Element) zählen wie ausgeblendete, damit Trennlinien um sie herum zusammenfallen.
        var elements = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        foreach (var id in build.Order.Where(id => !ToolbarCatalog.IsSeparator(id)))
        {
            if (!elements.ContainsKey(id) && build.Element(id) is { } element)
            {
                elements[id] = element;
            }
        }

        var sequence = ToolbarLayout.VisibleSequence(build.Order, id => build.IsVisible(id) && (ToolbarCatalog.IsSeparator(id) || elements.ContainsKey(id)));
        var placed = new HashSet<string>(sequence, StringComparer.Ordinal);
        foreach (var id in sequence)
        {
            var separator = ToolbarCatalog.IsSeparator(id);
            var element = separator ? ToolbarItemView.Separator(build.Vertical) : elements[id];
            Detach(element);
            // Trennlinien bringen ihren eigenen, zur Ausrichtung passenden Abstand mit (ToolbarItemView.Separator).
            if (!separator)
            {
                element.Margin = new Thickness(2);
            }

            if (build.MoveNextTo is { } move)
            {
                panel.Children.Add(new ReorderDragHost(ListKey, id, element, placed.Contains, move));
            }
            else
            {
                element.Tag = id;
                panel.Children.Add(element);
            }
        }

        return panel;
    }

    /// <summary>Kennungen der platzierten Einträge in Leistenreihenfolge (ohne Griff).</summary>
    public static IReadOnlyList<string> PlacedIds(Panel panel) =>
        panel.Children.OfType<FrameworkElement>().Select(child => child.Tag as string).Where(tag => tag is not null && tag != GripTag).Select(tag => tag!).ToList();

    /// <summary>Löst ein Element aus seinem bisherigen Panel; steckte es in einer <see cref="ReorderDragHost"/>, auch die Hülle aus deren Panel.</summary>
    private static void Detach(FrameworkElement element)
    {
        if (element.Parent is ReorderDragHost host)
        {
            host.Children.Remove(element);
            Detach(host);
            return;
        }

        if (element.Parent is Panel parent)
        {
            parent.Children.Remove(element);
        }
    }
}
