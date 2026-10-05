using System.Globalization;
using System.Windows.Data;

namespace TotalWarModTool.App;

/// <summary>
/// The entries of the context menu of a node, read from the node every time the binding is evaluated. The menu binds the node, whether it is open
/// and its entries: the first value is what the converter reads from, the other two only make the binding evaluate again, when the menu opens or
/// closes and when the node replaces its entries. A menu that is shown again for the same node thus never shows an old list, which matters for
/// the nodes that build their entries on demand (an entry of a pack, a file of a project). Wiring only.
/// </summary>
public sealed class NodeActionsConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is ExplorerNode node ? node.Actions : Array.Empty<NodeAction>();

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
