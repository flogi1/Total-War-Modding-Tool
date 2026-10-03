using System.Windows;

namespace TotalWarModTool.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ExplorerHost.Content = new ExplorerView(viewModel.Explorer);
        TabsHost.Content = new TabsView(viewModel.Tabs);
    }
}
