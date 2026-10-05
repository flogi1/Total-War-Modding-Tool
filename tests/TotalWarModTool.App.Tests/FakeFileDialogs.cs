namespace TotalWarModTool.App.Tests;

/// <summary>
/// Stand-in for the file dialogs: records every call with its arguments and returns the prepared answers. An empty queue means the user
/// cancels (a name or table question) or says no (the confirmation), so a command that asks again can never loop for ever.
/// </summary>
internal sealed class FakeFileDialogs : IFileDialogs
{
    /// <summary>The answers of <see cref="AskName"/>, one per call; null is a cancel.</summary>
    public Queue<string?> NameAnswers { get; } = new();

    /// <summary>The answers of <see cref="AskNewTable"/>, one per call; null is a cancel.</summary>
    public Queue<NewTableInput?> TableAnswers { get; } = new();

    /// <summary>The answers of <see cref="ConfirmSameName"/>, one per call.</summary>
    public Queue<bool> ConfirmAnswers { get; } = new();

    /// <summary>Runs inside every question before the answer is returned: lets a test change the world while the dialog is open.</summary>
    public Action? WhileAsking { get; set; }

    public List<NameAsk> NameAsks { get; } = [];

    public List<TableAsk> TableAsks { get; } = [];

    /// <summary>The file of every <see cref="ConfirmSameName"/> call.</summary>
    public List<string> Confirms { get; } = [];

    public List<(string Title, string Message)> Messages { get; } = [];

    public string? AskName(string title, string label, string suggestion, Func<string, string?> validate)
    {
        NameAsks.Add(new NameAsk(title, label, suggestion, validate));
        WhileAsking?.Invoke();
        return NameAnswers.Count > 0 ? NameAnswers.Dequeue() : null;
    }

    public NewTableInput? AskNewTable(IReadOnlyList<string> tables, string suggestion, Func<NewTableInput, string?> validate)
    {
        TableAsks.Add(new TableAsk(tables, suggestion, validate));
        WhileAsking?.Invoke();
        return TableAnswers.Count > 0 ? TableAnswers.Dequeue() : null;
    }

    public bool ConfirmSameName(string file)
    {
        Confirms.Add(file);
        WhileAsking?.Invoke();
        return ConfirmAnswers.Count > 0 && ConfirmAnswers.Dequeue();
    }

    public void ShowMessage(string title, string message) => Messages.Add((title, message));

    public sealed record NameAsk(string Title, string Label, string Suggestion, Func<string, string?> Validate);

    public sealed record TableAsk(IReadOnlyList<string> Tables, string Suggestion, Func<NewTableInput, string?> Validate);
}
