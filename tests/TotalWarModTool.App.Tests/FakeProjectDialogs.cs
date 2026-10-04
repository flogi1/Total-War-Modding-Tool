namespace TotalWarModTool.App.Tests;

/// <summary>Stand-in for the project dialogs: records every call with its arguments and returns the prepared answers.</summary>
internal sealed class FakeProjectDialogs : IProjectDialogs
{
    /// <summary>The answers of <see cref="PickFolder"/>, one per call; an empty queue means the user cancels.</summary>
    public Queue<string?> FolderAnswers { get; } = new();

    /// <summary>The answers of <see cref="PickPackFile"/>, one per call; an empty queue means the user cancels.</summary>
    public Queue<string?> PackFileAnswers { get; } = new();

    /// <summary>The answer of every <see cref="AskProject"/> call; null means the user cancels.</summary>
    public ProjectInput? ProjectAnswer { get; set; }

    public List<string> FolderTitles { get; } = [];

    public int PackFileCalls { get; private set; }

    public List<AskCall> Asks { get; } = [];

    public List<Report> Reports { get; } = [];

    public List<(string Title, string Message)> Messages { get; } = [];

    public string? PickFolder(string title)
    {
        FolderTitles.Add(title);
        return FolderAnswers.Count > 0 ? FolderAnswers.Dequeue() : null;
    }

    public string? PickPackFile()
    {
        PackFileCalls++;
        return PackFileAnswers.Count > 0 ? PackFileAnswers.Dequeue() : null;
    }

    public ProjectInput? AskProject(string title, ProjectInput defaults, IReadOnlyList<string> info, Func<ProjectInput, string?> validate)
    {
        Asks.Add(new AskCall(title, defaults, info, validate));
        return ProjectAnswer;
    }

    public void ShowReport(Report report) => Reports.Add(report);

    public void ShowMessage(string title, string message) => Messages.Add((title, message));

    public sealed record AskCall(string Title, ProjectInput Defaults, IReadOnlyList<string> Info, Func<ProjectInput, string?> Validate);
}
