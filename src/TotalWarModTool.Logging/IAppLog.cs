namespace TotalWarModTool.Logging;

/// <summary>Application log. Implementations never throw: a log must not be able to take the application down.</summary>
public interface IAppLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}
