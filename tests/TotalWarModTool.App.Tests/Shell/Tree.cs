using System.Windows;
using System.Windows.Media;

namespace TotalWarModTool.App.Tests.Shell;

/// <summary>Walks the logical and visual tree of an element (without a layout pass only logical children are guaranteed to exist).</summary>
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
