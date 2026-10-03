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
