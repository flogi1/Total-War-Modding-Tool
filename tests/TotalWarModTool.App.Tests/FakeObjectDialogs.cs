namespace TotalWarModTool.App.Tests;

/// <summary>Stand-in for the object dialogs: records every call with its arguments and returns the prepared answers.</summary>
internal sealed class FakeObjectDialogs : IObjectDialogs
{
    /// <summary>The answer of every <see cref="AskUnapplied"/> call. Cancel by default, so that nothing happens by accident.</summary>
    public SaveChoice UnappliedAnswer { get; set; } = SaveChoice.Cancel;

    /// <summary>Picks the file for <see cref="AskTargetFile"/> from the candidates; null cancels. Cancels by default.</summary>
    public Func<IReadOnlyList<string>, string?> TargetAnswer { get; set; } = _ => null;

    /// <summary>The answer of every <see cref="AskCopies"/> call. False (cancel) by default.</summary>
    public bool CopiesAnswer { get; set; }

    /// <summary>Runs inside a question before the answer is returned: lets a test change the world while the dialog is open.</summary>
    public Action? WhileAsking { get; set; }

    public List<(string Title, string Message)> Messages { get; } = [];

    /// <summary>The object name of every <see cref="AskUnapplied"/> call.</summary>
    public List<string> UnappliedAsks { get; } = [];

    public List<(string TableOrLoc, bool IsLoc, IReadOnlyList<string> Candidates)> TargetAsks { get; } = [];

    /// <summary>The lines of every <see cref="AskCopies"/> call.</summary>
    public List<IReadOnlyList<string>> CopyAsks { get; } = [];

    public void ShowMessage(string title, string message) => Messages.Add((title, message));

    public SaveChoice AskUnapplied(string objectName)
    {
        UnappliedAsks.Add(objectName);
        WhileAsking?.Invoke();
        return UnappliedAnswer;
    }

    public string? AskTargetFile(string tableOrLoc, bool isLoc, IReadOnlyList<string> candidates)
    {
        TargetAsks.Add((tableOrLoc, isLoc, candidates));
        WhileAsking?.Invoke();
        return TargetAnswer(candidates);
    }

    public bool AskCopies(IReadOnlyList<string> lines)
    {
        CopyAsks.Add(lines);
        WhileAsking?.Invoke();
        return CopiesAnswer;
    }
}
