using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>
/// Asks for a table of the schema (a filter box over a list) and the name of the new file. Shown modally by the file dialogs; the answer is
/// <see cref="Result"/> once <c>ShowDialog</c> returns. The code-behind is view wiring only: moving the focus with the keys.
/// </summary>
public sealed partial class NewTableWindow : Window
{
    private readonly NewTableViewModel _viewModel;

    /// <param name="title">The window title, e.g. "New table"; given by the caller.</param>
    public NewTableWindow(NewTableViewModel viewModel, string title)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(title);

        InitializeComponent();
        Title = title;
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>The table and name when the user clicked OK; null for Cancel, Esc and the close box.</summary>
    public NewTableInput? Result { get; private set; }

    // The filter box has the focus (the XAML says so); the suggestion in the name box is selected for the moment the focus gets there.
    private void OnContentRendered(object sender, EventArgs e) => NameBox.SelectAll();

    // Arrow down leaves the filter box for the list, with the focus on the chosen table.
    private void OnFilterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down)
        {
            return;
        }

        e.Handled = true;
        if (TableList.SelectedItem is { } selected)
        {
            TableList.ScrollIntoView(selected);
            TableList.UpdateLayout();
            if (TableList.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem item)
            {
                item.Focus();
                return;
            }
        }

        TableList.Focus();
    }

    // Enter in the list takes the chosen table and goes on to the name, instead of pressing OK.
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Result = _viewModel.Result;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
