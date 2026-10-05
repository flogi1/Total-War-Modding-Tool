namespace TotalWarModTool.App.Tests;

/// <summary>Stand-in for the edit dialogs: records every call with its arguments and returns the prepared answers.</summary>
internal sealed class FakeEditDialogs : IEditDialogs
{
    /// <summary>The answers of <see cref="AskUnsaved"/>, one per call; an empty queue gives <see cref="UnsavedAnswer"/>.</summary>
    public Queue<SaveChoice> UnsavedAnswers { get; } = new();

    /// <summary>The answer of every <see cref="AskUnsaved"/> call that the queue does not answer. Cancel by default, so that nothing happens by accident.</summary>
    public SaveChoice UnsavedAnswer { get; set; } = SaveChoice.Cancel;

    /// <summary>The answer of every <see cref="AskSaveAllBeforeBuild"/> call. False (cancel) by default.</summary>
    public bool SaveAllAnswer { get; set; }

    /// <summary>The answers of <see cref="AskConflict"/>, one per call; an empty queue gives <see cref="ConflictAnswer"/>.</summary>
    public Queue<ConflictChoice> ConflictAnswers { get; } = new();

    /// <summary>The answer of every <see cref="AskConflict"/> call that the queue does not answer. Cancel by default, so that nothing is overwritten by accident.</summary>
    public ConflictChoice ConflictAnswer { get; set; } = ConflictChoice.Cancel;

    /// <summary>The answers of <see cref="AskCreateAgain"/>, one per call; an empty queue gives <see cref="CreateAgainAnswer"/>.</summary>
    public Queue<bool> CreateAgainAnswers { get; } = new();

    /// <summary>The answer of every <see cref="AskCreateAgain"/> call that the queue does not answer. False (no) by default.</summary>
    public bool CreateAgainAnswer { get; set; }

    /// <summary>Runs inside a question before the answer is returned: lets a test change the world while the dialog is open, or throw.</summary>
    public Action? WhileAsking { get; set; }

    /// <summary>Thrown by <see cref="ShowMessage"/> after the message was recorded: a window that cannot be shown.</summary>
    public Exception? ShowMessageFailure { get; set; }

    public List<(string Title, string Message)> Messages { get; } = [];

    /// <summary>The file list of every <see cref="AskUnsaved"/> call.</summary>
    public List<IReadOnlyList<string>> UnsavedAsks { get; } = [];

    /// <summary>The file list of every <see cref="AskSaveAllBeforeBuild"/> call.</summary>
    public List<IReadOnlyList<string>> BuildAsks { get; } = [];

    /// <summary>The file name of every <see cref="AskConflict"/> call.</summary>
    public List<string> ConflictAsks { get; } = [];

    /// <summary>The file name of every <see cref="AskCreateAgain"/> call.</summary>
    public List<string> CreateAgainAsks { get; } = [];

    public ConflictChoice AskConflict(string file)
    {
        ConflictAsks.Add(file);
        WhileAsking?.Invoke();
        return ConflictAnswers.Count > 0 ? ConflictAnswers.Dequeue() : ConflictAnswer;
    }

    public bool AskCreateAgain(string file)
    {
        CreateAgainAsks.Add(file);
        WhileAsking?.Invoke();
        return CreateAgainAnswers.Count > 0 ? CreateAgainAnswers.Dequeue() : CreateAgainAnswer;
    }

    public void ShowMessage(string title, string message)
    {
        Messages.Add((title, message));
        if (ShowMessageFailure is { } failure)
        {
            throw failure;
        }
    }

    public SaveChoice AskUnsaved(IReadOnlyList<string> files)
    {
        UnsavedAsks.Add(files);
        WhileAsking?.Invoke();
        return UnsavedAnswers.Count > 0 ? UnsavedAnswers.Dequeue() : UnsavedAnswer;
    }

    public bool AskSaveAllBeforeBuild(IReadOnlyList<string> files)
    {
        BuildAsks.Add(files);
        WhileAsking?.Invoke();
        return SaveAllAnswer;
    }
}
