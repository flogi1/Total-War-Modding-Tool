using System.Windows;
using System.Windows.Media;

namespace TotalWarModTool.App.Tests;

/// <summary>
/// Tree walkers shared by the view tests. <see cref="Descendants{T}"/> prefers the logical tree and falls back to the visual one (without a
/// layout pass only logical children are guaranteed to exist); <see cref="VisualDescendants{T}"/> walks the visual tree only, which reaches
/// the parts of applied templates in a shown window.
/// </summary>
internal static class Tree
{
    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var node in Nodes(root))
        {
            if (node is T match)
            {
                yield return match;
            }
        }
    }

    public static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in VisualDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    {
        yield return root;
        var children = LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().ToList();
        if (children.Count == 0 && root is Visual)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                children.Add(VisualTreeHelper.GetChild(root, i));
            }
        }

        foreach (var child in children)
        {
            foreach (var nested in Nodes(child))
            {
                yield return nested;
            }
        }
    }
}
