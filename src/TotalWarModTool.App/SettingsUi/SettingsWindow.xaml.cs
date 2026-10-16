using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>The settings window: a list of categories on the left, the page of the chosen category on the right. Pure view wiring.</summary>
public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // The tile bar and toolbar pages are built in code; they listen to the settings, which outlive the window.
        var tiles = BarSettingsPages.Tiles(viewModel);
        var toolbar = BarSettingsPages.Toolbar(viewModel);
        TilesHost.Content = tiles;
        ToolbarHost.Content = toolbar;
        Closed += (_, _) =>
        {
            tiles.Detach();
            toolbar.Detach();
        };
    }

    // A path box commits when it loses the focus (the binding does that) and now also on Enter. The view model decides what the input means.
    private void OnPathBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
    }
}
