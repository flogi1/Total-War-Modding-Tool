using System.Windows;
using System.Windows.Media;

namespace ParadoxModTool.Shell.Tests;

/// <summary>Durchläuft den logischen und visuellen Baum eines Elements (ohne Layout-Durchlauf sind nur logische Kinder sicher vorhanden).</summary>
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
