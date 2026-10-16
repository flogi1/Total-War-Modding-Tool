using System.Windows;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

public partial class MainWindow : Window
{
    /// <param name="settings">The global settings: the tile bar and the toolbar are built from them and follow their changes.</param>
    public MainWindow(MainViewModel viewModel, GlobalSettings settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        ExplorerHost.Content = new ExplorerView(viewModel.Explorer);
        TabsHost.Content = new TabsView(viewModel.Tabs);
        InitializeShell(viewModel, settings);
    }
}
