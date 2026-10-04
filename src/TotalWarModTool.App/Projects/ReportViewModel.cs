namespace TotalWarModTool.App;

/// <summary>One section of a report as the window shows it: the lines are one text, so that they can be selected and copied together.</summary>
public sealed record ReportSectionView(string Heading, string Text);

/// <summary>The model of <see cref="ReportWindow"/>.</summary>
public sealed class ReportViewModel
{
    public ReportViewModel(Report report, AppTexts texts)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(texts);

        Texts = texts;
        Title = report.Title;
        Summary = report.Summary;
        Sections = [.. report.Sections.Select(section => new ReportSectionView(section.Heading, string.Join("\n", section.Lines)))];
    }

    public AppTexts Texts { get; }

    public string Title { get; }

    public string Summary { get; }

    public IReadOnlyList<ReportSectionView> Sections { get; }
}
