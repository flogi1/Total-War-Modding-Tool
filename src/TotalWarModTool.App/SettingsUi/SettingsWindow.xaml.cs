using System.Windows;

namespace TotalWarModTool.App;

/// <summary>The settings window: a list of categories on the left, the page of the chosen category on the right. Pure view wiring.</summary>
public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
