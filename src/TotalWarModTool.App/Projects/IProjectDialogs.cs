namespace TotalWarModTool.App;

/// <summary>What the user entered into the project dialog. The values are as typed; nothing is trimmed or normalised.</summary>
public sealed record ProjectInput(string Name, string Folder, string PackName);

/// <summary>One block of a <see cref="Report"/>: a heading and the lines under it. The lines are ready to display (escaped).</summary>
public sealed record ReportSection(string Heading, IReadOnlyList<string> Lines);

/// <summary>A report with a summary and sections, for results that are too long for a message box.</summary>
public sealed record Report(string Title, string Summary, IReadOnlyList<ReportSection> Sections);

/// <summary>Everything the project commands ask the user. The fake in the tests records the calls and returns prepared answers.</summary>
public interface IProjectDialogs
{
    /// <summary>Asks for a folder; null when the user cancels.</summary>
    string? PickFolder(string title);

    /// <summary>Asks for a pack file to import; null when the user cancels.</summary>
    string? PickPackFile();

    /// <param name="info">Lines shown above the fields (the import preview); empty for a new project.</param>
    /// <param name="validate">Called on every change; the text it returns is shown and blocks OK, null accepts the input.</param>
    /// <returns>The input, or null when the user cancels.</returns>
    ProjectInput? AskProject(string title, ProjectInput defaults, IReadOnlyList<string> info, Func<ProjectInput, string?> validate);

    void ShowReport(Report report);

    void ShowMessage(string title, string message);
}
