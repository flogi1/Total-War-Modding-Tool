using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace TotalWarModTool.App;

/// <summary>
/// The dialog of <see cref="CrashHandler"/>: what happened, the details to copy into a bug report, and the buttons Open log, Copy details
/// and Close. It has no "report error" button on purpose (spec 4.6). Failures of the buttons (clipboard held by another program, explorer
/// that does not start) are not caught here: they reach the global handler, which only logs them while this dialog is open.
/// </summary>
public sealed partial class CrashDialog : Window
{
    private readonly string _details;
    private readonly Action _openLogFolder;

    /// <param name="fatal">The app closes after this dialog: it shows <see cref="AppTexts.CrashFatalMessage"/> instead of <see cref="AppTexts.CrashMessage"/>.</param>
    public CrashDialog(AppTexts texts, string details, Action openLogFolder, bool fatal = false)
    {
        _details = details;
        _openLogFolder = openLogFolder;

        InitializeComponent();
        DataContext = texts;
        DetailsBox.Text = details;

        // Another binding path, not a copied string, so that the message still follows a language change while the dialog is open.
        if (fatal)
        {
            MessageBlock.SetBinding(TextBlock.TextProperty, new Binding(nameof(AppTexts.CrashFatalMessage)));
        }
    }

    private void OnOpenLogClick(object sender, RoutedEventArgs e) => _openLogFolder();

    private void OnCopyDetailsClick(object sender, RoutedEventArgs e) => Clipboard.SetText(_details);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
