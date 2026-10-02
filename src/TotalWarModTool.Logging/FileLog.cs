using System.Globalization;
using System.Text;

namespace TotalWarModTool.Logging;

/// <summary>
/// Rotating plain-text log file: <c>twmt.log</c> is the current file, <c>twmt.1.log</c> and up are the archives, newest first.
/// All writes are serialized by one lock. The log never throws; if the folder or file is unusable the line is dropped,
/// because a log cannot report its own failure.
/// </summary>
public sealed class FileLog : IAppLog
{
    public const string FileName = "twmt.log";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly string? _userProfilePath;
    private readonly long _maxFileBytes;
    private readonly int _maxFiles;

    /// <param name="directory">Folder of the log files. It is created on the first write.</param>
    /// <param name="time">Source of the timestamps (local time).</param>
    /// <param name="userProfilePath">Replaced by <c>%USERPROFILE%</c> in every message and exception text. Null disables the redaction.</param>
    /// <param name="maxFileBytes">A file is rotated before a line would push it above this size.</param>
    /// <param name="maxFiles">Number of files kept: the current one plus the archives.</param>
    public FileLog(string directory, TimeProvider time, string? userProfilePath, long maxFileBytes = 1_048_576, int maxFiles = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);

        Directory = directory;
        _time = time;
        _userProfilePath = userProfilePath;
        _maxFileBytes = maxFileBytes;
        _maxFiles = maxFiles;
    }

    public string Directory { get; }

    public string CurrentFilePath => Path.Combine(Directory, FileName);

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var timestamp = _time.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var builder = new StringBuilder()
            .Append(timestamp).Append(" [").Append(level).Append("] ")
            .Append(PathRedactor.Redact(message, _userProfilePath));
        if (exception is not null)
        {
            builder.Append(Environment.NewLine).Append(PathRedactor.Redact(exception.ToString(), _userProfilePath));
        }

        builder.Append(Environment.NewLine);
        var line = builder.ToString();

        lock (_gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                RotateIfNeeded(Utf8NoBom.GetByteCount(line));
                File.AppendAllText(CurrentFilePath, line, Utf8NoBom);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Dropping the line is the only option: the log cannot log its own failure.
            }
        }
    }

    /// <summary>
    /// Rotates when the current file is not empty and would exceed the limit with the new line. A single line longer than the limit
    /// therefore still ends up in a file of its own instead of rotating forever.
    /// </summary>
    private void RotateIfNeeded(int incomingBytes)
    {
        var current = new FileInfo(CurrentFilePath);
        if (current.Exists && current.Length > 0 && current.Length + incomingBytes > _maxFileBytes)
        {
            Rotate();
        }
    }

    /// <summary>
    /// Shifts every file one index up (<c>twmt.log</c> becomes <c>twmt.1.log</c>, <c>twmt.1.log</c> becomes <c>twmt.2.log</c>, ...),
    /// starting at the top so nothing is overwritten. The file in the last slot falls out. Index 0 is the current file.
    /// </summary>
    private void Rotate()
    {
        var lastIndex = _maxFiles - 1;
        File.Delete(PathOf(lastIndex));
        for (var index = lastIndex - 1; index >= 0; index--)
        {
            var source = PathOf(index);
            if (File.Exists(source))
            {
                File.Move(source, PathOf(index + 1), overwrite: true);
            }
        }
    }

    private string PathOf(int index) =>
        index == 0 ? CurrentFilePath : Path.Combine(Directory, "twmt." + index.ToString(CultureInfo.InvariantCulture) + ".log");
}
