using System.Windows;

namespace TotalWarModTool.App;

/// <summary>
/// Asks for one name (rename, new loc file, add a pack entry). Shown modally by the file dialogs; the answer is <see cref="Result"/> once
/// <c>ShowDialog</c> returns.
/// </summary>
public sealed partial class NameInputWindow : Window
{
    private readonly NameInputViewModel _viewModel;

    /// <param name="title">The window title, e.g. "Rename"; given by the caller because it depends on what is asked.</param>
    public NameInputWindow(NameInputViewModel viewModel, string title)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(title);

        InitializeComponent();
        Title = title;
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>The name when the user clicked OK; null for Cancel, Esc and the close box.</summary>
    public string? Result { get; private set; }

    // The box has the focus with the suggestion selected, so that typing replaces it.
    private void OnContentRendered(object sender, EventArgs e)
    {
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
