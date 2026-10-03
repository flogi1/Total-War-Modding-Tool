using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TotalWarModTool.App;

/// <summary>The open tabs. Pure view wiring: what a click means is decided by the commands of the view model.</summary>
public sealed partial class TabsView : UserControl
{
    public TabsView(TabsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // The middle button closes the tab under the mouse, wherever on its header the click lands. A click in the content has no tab item above it.
    private void OnTabControlMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        if (FindTabItem(e.OriginalSource as DependencyObject) is { DataContext: TabViewModel tab })
        {
            tab.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static TabItem? FindTabItem(DependencyObject? element)
    {
        while (element is not null and not TabItem)
        {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return element as TabItem;
    }
}
