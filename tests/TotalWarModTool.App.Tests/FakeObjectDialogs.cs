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

    /// <summary>Gives the key for <see cref="AskNewKey"/> from its validation; null cancels. Cancels by default.</summary>
    public Func<Func<string, string?>, string?> NewKeyAnswer { get; set; } = _ => null;

    /// <summary>Runs inside a question before the answer is returned: lets a test change the world while the dialog is open.</summary>
    public Action? WhileAsking { get; set; }

    public List<(string Title, string Message)> Messages { get; } = [];

    /// <summary>The object name of every <see cref="AskUnapplied"/> call.</summary>
    public List<string> UnappliedAsks { get; } = [];

    public List<(string TableOrLoc, bool IsLoc, IReadOnlyList<string> Candidates)> TargetAsks { get; } = [];

    /// <summary>The lines of every <see cref="AskCopies"/> call.</summary>
    public List<IReadOnlyList<string>> CopyAsks { get; } = [];

    /// <summary>The object name and the suggestion of every <see cref="AskNewKey"/> call.</summary>
    public List<(string ObjectName, string Suggestion)> NewKeyAsks { get; } = [];

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

    public string? AskNewKey(string objectName, string suggestion, Func<string, string?> validate)
    {
        NewKeyAsks.Add((objectName, suggestion));
        WhileAsking?.Invoke();
        return NewKeyAnswer(validate);
    }

    /// <summary>Gives the answer of <see cref="AskNewBuilding"/> from its input; null cancels. Cancels by default.</summary>
    public Func<NewBuildingInput, NewBuildingAnswer?> BuildingAnswer { get; set; } = _ => null;

    /// <summary>The input of every <see cref="AskNewBuilding"/> call.</summary>
    public List<NewBuildingInput> BuildingAsks { get; } = [];

    public NewBuildingAnswer? AskNewBuilding(NewBuildingInput input)
    {
        BuildingAsks.Add(input);
        WhileAsking?.Invoke();
        return BuildingAnswer(input);
    }

    /// <summary>Gives the answer of <see cref="AskNewTrait"/> from its input; null cancels. Cancels by default.</summary>
    public Func<NewTraitInput, NewTraitAnswer?> TraitAnswer { get; set; } = _ => null;

    /// <summary>The input of every <see cref="AskNewTrait"/> call.</summary>
    public List<NewTraitInput> TraitAsks { get; } = [];

    public NewTraitAnswer? AskNewTrait(NewTraitInput input)
    {
        TraitAsks.Add(input);
        WhileAsking?.Invoke();
        return TraitAnswer(input);
    }
}
