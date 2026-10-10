using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>
/// The dialog of the new building (subculture, template with a filter, key, chain). Shown modally by the object dialogs; the answer is
/// <see cref="Result"/> once <c>ShowDialog</c> returns. The code-behind is view wiring only, as in <see cref="NewTableWindow"/>.
/// </summary>
public sealed partial class NewBuildingWindow : Window
{
    private readonly NewBuildingViewModel _viewModel;

    public NewBuildingWindow(NewBuildingViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>The answer when the user clicked OK; null for Cancel, Esc and the close box.</summary>
    public NewBuildingAnswer? Result { get; private set; }

    // The filter box has the focus (the XAML says so); the suggestion in the key box is selected for the moment the focus gets there.
    private void OnContentRendered(object sender, EventArgs e) => KeyBox.SelectAll();

    // Arrow down leaves the filter box for the list, with the focus on the chosen template.
    private void OnFilterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down)
        {
            return;
        }

        e.Handled = true;
        if (TemplateList.SelectedItem is { } selected)
        {
            TemplateList.ScrollIntoView(selected);
            TemplateList.UpdateLayout();
            if (TemplateList.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem item)
            {
                item.Focus();
                return;
            }
        }

        TemplateList.Focus();
    }

    // A template stays chosen: when the list lost its selection (a Ctrl+click) and the view model still has one that is listed, it is selected again.
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateList.SelectedItem is null && _viewModel.SelectedTemplate is { } chosen && TemplateList.Items.Contains(chosen))
        {
            TemplateList.SelectedItem = chosen;
        }
    }

    // Enter in the list takes the chosen template and goes on to the key, instead of pressing OK.
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        KeyBox.Focus();
        KeyBox.SelectAll();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Result = _viewModel.Result;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
