namespace TotalWarModTool.App;

/// <summary>Helpers that put a UI text and the technical data of a library together into one message.</summary>
internal static class MessageText
{
    /// <summary>The UI text and, when there is a technical detail, the detail after a blank line.</summary>
    internal static string WithDetail(string text, string? detail) =>
        string.IsNullOrEmpty(detail) ? text : text + "\n\n" + detail;

    /// <summary>
    /// <paramref name="text"/> with every control character, line separator, bidirectional control and unpaired surrogate written as
    /// <c>\uXXXX</c>, for data from foreign files that is shown inside a message. The same rule as <see cref="DisplayText.Safe"/>, without a cut.
    /// </summary>
    internal static string EscapeControlCharacters(string text) => DisplayText.Safe(text, int.MaxValue);
}
