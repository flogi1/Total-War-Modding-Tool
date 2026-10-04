using System.Windows;

namespace TotalWarModTool.App;

/// <summary>Shows a <see cref="Report"/>: the title, the summary and the sections as selectable text. Shown modally by the project dialogs.</summary>
public sealed partial class ReportWindow : Window
{
    public ReportWindow(Report report, AppTexts texts)
    {
        InitializeComponent();
        DataContext = new ReportViewModel(report, texts);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
