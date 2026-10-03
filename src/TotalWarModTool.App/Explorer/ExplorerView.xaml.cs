using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>The pack explorer: a tree of the packs, or a message instead. Pure view wiring; opening an entry goes to the node's command.</summary>
public sealed partial class ExplorerView : UserControl
{
    public ExplorerView(ExplorerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ApplyThemedItemStyle();
    }

    /// <summary>
    /// Bases the container style of the tree on the TreeViewItem style that is in scope (the themed one of the app): a style set on the tree
    /// would replace it. Done in code because a resource reference in XAML cannot be resolved where the app resources are missing.
    /// </summary>
    public void ApplyThemedItemStyle()
    {
        if (TryFindResource(typeof(TreeViewItem)) is Style themed && Resources["NodeContainerStyle"] is Style container)
        {
            container.BasedOn = themed;
        }
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Tree.SelectedItem is EntryNode entry)
        {
            entry.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Raised on every item along the way of the click, each with its own sender; only the entry opens (it has no children, so only once).
    private void OnItemMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && sender is FrameworkElement { DataContext: EntryNode entry })
        {
            entry.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }
}
