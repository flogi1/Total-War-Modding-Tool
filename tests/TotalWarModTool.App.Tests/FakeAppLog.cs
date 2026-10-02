using TotalWarModTool.Logging;

namespace TotalWarModTool.App.Tests;

/// <summary>Collects every log call so a test can assert what was logged.</summary>
internal sealed class FakeAppLog : IAppLog
{
    public List<string> Infos { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    /// <summary>The exception of each <see cref="Error"/> call, in the same order as <see cref="Errors"/> (null if none was passed).</summary>
    public List<Exception?> Exceptions { get; } = [];

    public void Info(string message) => Infos.Add(message);

    public void Warn(string message) => Warnings.Add(message);

    public void Error(string message, Exception? exception = null)
    {
        Errors.Add(message);
        Exceptions.Add(exception);
    }
}
