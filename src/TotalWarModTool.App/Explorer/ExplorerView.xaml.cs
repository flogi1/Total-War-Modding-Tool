using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>The explorer: a tree of the projects and packs, with a message above it when there is one. Pure view wiring; opening a file goes to the node's command.</summary>
public sealed partial class ExplorerView : UserControl
{
    public ExplorerView(ExplorerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ApplyThemedItemStyle();
    }

    /// <summary>
    /// Bases the container styles of the tree and of the context menus on the TreeViewItem and MenuItem styles that are in scope (the themed
    /// ones of the app): a style set by key would replace them. Done in code because a resource reference in XAML cannot be resolved where
    /// the app resources are missing.
    /// </summary>
    public void ApplyThemedItemStyle()
    {
        if (TryFindResource(typeof(TreeViewItem)) is Style themed && Resources["NodeContainerStyle"] is Style container)
        {
            container.BasedOn = themed;
        }

        if (TryFindResource(typeof(MenuItem)) is Style themedMenuItem && Resources["ActionItemStyle"] is Style action)
        {
            action.BasedOn = themedMenuItem;
        }
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OpenCommandOf(Tree.SelectedItem) is { } open)
        {
            open.Execute(null);
            e.Handled = true;
        }
    }

    // Raised on every item along the way of the click, each with its own sender; only a file opens (it has no children, so only once).
    private void OnItemMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && sender is FrameworkElement element && OpenCommandOf(element.DataContext) is { } open)
        {
            open.Execute(null);
            e.Handled = true;
        }
    }

    // The entry of a pack and the file of a project open the same way.
    private static ICommand? OpenCommandOf(object? node) => node switch
    {
        EntryNode entry => entry.OpenCommand,
        ProjectFileNode file => file.OpenCommand,
        _ => null,
    };
}
