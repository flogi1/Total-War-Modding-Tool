namespace TotalWarModTool.App.Tests;

/// <summary>Stand-in for the edit dialogs: records every call with its arguments.</summary>
internal sealed class FakeEditDialogs : IEditDialogs
{
    public List<(string Title, string Message)> Messages { get; } = [];

    public void ShowMessage(string title, string message) => Messages.Add((title, message));
}
