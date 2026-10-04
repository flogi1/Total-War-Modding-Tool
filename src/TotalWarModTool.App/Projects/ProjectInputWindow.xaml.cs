using System.Windows;

namespace TotalWarModTool.App;

/// <summary>
/// Asks for the name, folder and pack name of a project. Shown modally by the project dialogs; the caller reads the answer from the view
/// model when <c>ShowDialog</c> returns true.
/// </summary>
public sealed partial class ProjectInputWindow : Window
{
    /// <param name="title">The window title, e.g. "New project"; given by the caller because it depends on what is asked.</param>
    public ProjectInputWindow(ProjectInputViewModel viewModel, string title)
    {
        InitializeComponent();
        Title = title;
        DataContext = viewModel;
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
