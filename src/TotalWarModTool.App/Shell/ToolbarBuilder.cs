using System.Windows;
using System.Windows.Controls;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App.Shell;

/// <summary>
/// What the builder needs for one build: the order (ids and <c>sep:n</c>), the visibility per id, the already created elements per id
/// (<see langword="null"/> = skip), the orientation, optionally the grip at the start, and the reorder action (with it every entry becomes
/// a drag source and target).
/// </summary>
public sealed record ToolbarBuild(
    IReadOnlyList<string> Order,
    Func<string, bool> IsVisible,
    Func<string, FrameworkElement?> Element,
    bool Vertical,
    FrameworkElement? Grip = null,
    Action<string, string>? MoveNextTo = null);

/// <summary>
/// Builds the small toolbar from existing elements in the order of the user. The elements belong to the main window and stay the same
/// across all builds; the builder detaches them from the old panel and attaches them to the new one. Every placed child carries its id in
/// <c>Tag</c>, the grip carries <c>"grip"</c>.
/// </summary>
public static class ToolbarBuilder
{
    public const double VerticalWidth = 44;

    /// <summary>Bar id in the drag data; tiles (<see cref="TileBar.ListKey"/>) are never accepted here.</summary>
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

        // Unknown ids (no element) count as hidden, so separators around them collapse.
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
            // Separators bring their own margin that fits the orientation (ToolbarItemView.Separator).
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

    /// <summary>Ids of the placed entries in bar order (without the grip).</summary>
    public static IReadOnlyList<string> PlacedIds(Panel panel) =>
        panel.Children.OfType<FrameworkElement>().Select(child => child.Tag as string).Where(tag => tag is not null && tag != GripTag).Select(tag => tag!).ToList();

    /// <summary>Detaches an element from its current panel; if it sat in a <see cref="ReorderDragHost"/>, the wrapper is detached from its panel too.</summary>
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
