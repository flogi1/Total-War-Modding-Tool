using System.Globalization;
using System.Windows.Data;

namespace TotalWarModTool.App;

/// <summary>
/// The entries of the context menu of a node, read from the node once for every opening of the menu. The menu binds the node and whether it is
/// open: a plain binding to the entries is evaluated once per node, so a menu that is shown again for the same node would show an old list,
/// which matters for the nodes that build their entries on demand (an entry of a pack, a file of a project). While the menu is closed the
/// converter changes nothing: nothing is built when the menu closes, and the items are never replaced while a click on one of them is being
/// processed. Wiring only.
/// </summary>
public sealed class NodeActionsConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [ExplorerNode node, true] ? node.Actions : Binding.DoNothing;

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
