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

    // The selected tab decides whether it may be left (a cell with a value that cannot be committed says no). Asked before the tab control
    // moves, so that it never shows another tab for a moment: a click on the header of another tab, and the keys that change the tab.
    private void OnTabControlPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindTabItem(e.OriginalSource as DependencyObject) is { DataContext: TabViewModel tab }
            && !ReferenceEquals(tab, ViewModel.SelectedTab) && !ViewModel.MayLeave())
        {
            e.Handled = true;
        }
    }

    private void OnTabControlPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ChangesTab(e.Key, Keyboard.Modifiers, inContent: e.OriginalSource is not TabItem) && !ViewModel.MayLeave())
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Whether a key moves the selection of the tab control: Ctrl+Tab, Ctrl+PageUp and Ctrl+PageDown (also with Shift) from anywhere, the
    /// arrow keys, Home and End only while the header of a tab has the focus (<paramref name="inContent"/> false).
    /// </summary>
    internal static bool ChangesTab(Key key, ModifierKeys modifiers, bool inContent) =>
        (modifiers & ModifierKeys.Control) != 0
            ? key is Key.Tab or Key.PageUp or Key.PageDown
            : !inContent && key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End;

    // The tab list can refuse a change of the selection (a programmatic one, or one that got past the checks above); the tab control then
    // goes back to the tab that is selected. Later, not inside its own change of the selection. Changes of inner grids bubble up here too.
    private void OnTabControlSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, TabHost))
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(TabHost.SelectedItem, ViewModel.SelectedTab))
            {
                TabHost.SelectedItem = ViewModel.SelectedTab;
            }
        });
    }

    private TabsViewModel ViewModel => (TabsViewModel)DataContext;

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
