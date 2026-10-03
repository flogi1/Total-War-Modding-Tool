using System.Text;

namespace TotalWarModTool.PackFiles;

/// <summary>
/// Writes PFH5 mod packs as a stream: header, dependency index, entry index, then the data of every entry in index order.
/// Entries are never loaded completely; one copy buffer is the most that is held in memory. Problems are reported as a
/// <see cref="PackWriteResult"/>; only <see cref="OperationCanceledException"/> and argument errors are thrown.
/// </summary>
public static class PackWriter
{
    // Same cap as the reader applies to one path.
    private const int MaxPathBytes = 65535;
    private const int CopyBufferSize = 80 * 1024;
    private const int FileBufferSize = 64 * 1024;

    private const string Magic = "PFH5";
    private const uint PackTypeMod = 3;
    private const byte NotCompressed = 0;

    /// <summary>
    /// Pure: checks paths, dependencies and the limits of the format and opens nothing. The first problem wins, checked in this
    /// order: dependencies, path form, path length, duplicate paths, entry sizes, index sizes. A negative
    /// <see cref="PackWriteEntry.Length"/> is an argument error.
    /// </summary>
    public static PackWriteResult Validate(IReadOnlyList<string> dependencies, IReadOnlyList<PackWriteEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(entry.Path);
            ArgumentNullException.ThrowIfNull(entry.Open);
            if (entry.Length < 0)
            {
                throw new ArgumentException($"The length of entry '{entry.Path}' is negative.", nameof(entries));
            }
        }

        foreach (var name in dependencies)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (name.Length == 0 || name.Contains('\0'))
            {
                return Fail(PackWriteProblem.InvalidDependency, $"The dependency name '{Printable(name)}' is empty or contains a zero character.");
            }
        }

        foreach (var entry in entries)
        {
            if (!HasValidForm(entry.Path))
            {
                return Fail(PackWriteProblem.InvalidPath, $"The path '{Printable(entry.Path)}' is empty, contains a zero character, or has a leading, trailing or empty segment.");
            }
        }

        foreach (var entry in entries)
        {
            if (Encoding.UTF8.GetByteCount(entry.Path) > MaxPathBytes)
            {
                return Fail(PackWriteProblem.PathTooLong, $"The path of an entry is longer than {MaxPathBytes} UTF-8 bytes: '{entry.Path[..Math.Min(32, entry.Path.Length)]}' (shortened).");
            }
        }

        var seen = new Dictionary<string, string>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!seen.TryAdd(ToPackPath(entry.Path), entry.Path))
            {
                return Fail(PackWriteProblem.DuplicatePath, $"The paths '{seen[ToPackPath(entry.Path)]}' and '{entry.Path}' differ only in case or separator.");
            }
        }

        foreach (var entry in entries)
        {
            if (entry.Length > uint.MaxValue)
            {
                return Fail(PackWriteProblem.EntryTooLarge, $"The entry '{entry.Path}' is larger than {uint.MaxValue} bytes.");
            }
        }

        var (dependencyIndexSize, entryIndexSize) = ComputeIndexSizes(dependencies, entries);
        if (dependencyIndexSize > uint.MaxValue || entryIndexSize > uint.MaxValue)
        {
            return Fail(PackWriteProblem.IndexTooLarge, $"An index would be larger than {uint.MaxValue} bytes (dependencies {dependencyIndexSize}, entries {entryIndexSize}).");
        }

        return new PackWriteResult(PackWriteProblem.None, null);
    }

    /// <summary>
    /// Validates, then writes a PFH5 mod pack to <paramref name="targetPath"/> (created or replaced). <paramref name="progress"/>
    /// gets the number of entries written so far. After a problem or a cancel <paramref name="targetPath"/> does not exist; a failed
    /// validation never touches it. Throws only <see cref="OperationCanceledException"/> and argument errors.
    /// </summary>
    public static PackWriteResult Write(string targetPath, IReadOnlyList<string> dependencies, IReadOnlyList<PackWriteEntry> entries,
        DateTimeOffset buildTime, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetPath);
        var unixSeconds = buildTime.ToUnixTimeSeconds();
        ArgumentOutOfRangeException.ThrowIfNegative(unixSeconds, nameof(buildTime));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(unixSeconds, uint.MaxValue, nameof(buildTime));

        var validation = Validate(dependencies, entries);
        if (validation.Problem != PackWriteProblem.None)
        {
            return validation;
        }

        cancellationToken.ThrowIfCancellationRequested();

        FileStream stream;
        try
        {
            stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, FileBufferSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(PackWriteProblem.TargetUnwritable, ex.Message);
        }

        var succeeded = false;
        try
        {
            PackWriteResult result;
            try
            {
                using (stream)
                {
                    result = WriteAll(stream, dependencies, entries, (uint)unixSeconds, progress, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Source failures are handled where the source is touched, so what reaches this point came from the target.
                result = Fail(PackWriteProblem.TargetUnwritable, ex.Message);
            }

            succeeded = result.Problem == PackWriteProblem.None;
            return result;
        }
        finally
        {
            if (!succeeded)
            {
                DeleteQuietly(targetPath);
            }
        }
    }

    private static PackWriteResult WriteAll(Stream target, IReadOnlyList<string> dependencies, IReadOnlyList<PackWriteEntry> entries,
        uint buildTimeSeconds, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var sorted = entries
            .Select(entry => (PackPath: ToPackPath(entry.Path), Entry: entry))
            .OrderBy(item => item.PackPath, StringComparer.Ordinal)
            .ToList();
        var (dependencyIndexSize, entryIndexSize) = ComputeIndexSizes(dependencies, entries);

        target.Write(Encoding.ASCII.GetBytes(Magic));
        WriteUInt32(target, PackTypeMod);
        WriteUInt32(target, (uint)dependencies.Count);
        WriteUInt32(target, (uint)dependencyIndexSize);
        WriteUInt32(target, (uint)entries.Count);
        WriteUInt32(target, (uint)entryIndexSize);
        WriteUInt32(target, buildTimeSeconds);

        foreach (var name in dependencies)
        {
            WriteZeroTerminated(target, name);
        }

        foreach (var (packPath, entry) in sorted)
        {
            WriteUInt32(target, (uint)entry.Length);
            target.WriteByte(NotCompressed);
            WriteZeroTerminated(target, packPath);
        }

        var buffer = new byte[CopyBufferSize];
        var written = 0;
        foreach (var (_, entry) in sorted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = CopyEntry(target, entry, buffer, cancellationToken);
            if (result.Problem != PackWriteProblem.None)
            {
                return result;
            }

            progress?.Report(++written);
        }

        return new PackWriteResult(PackWriteProblem.None, null);
    }

    private static PackWriteResult CopyEntry(Stream target, PackWriteEntry entry, byte[] buffer, CancellationToken cancellationToken)
    {
        Stream source;
        try
        {
            source = entry.Open() ?? throw new ArgumentException($"Open of entry '{entry.Path}' returned no stream.", nameof(entry));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(PackWriteProblem.SourceUnreadable, $"Entry '{entry.Path}' could not be opened: {ex.Message}");
        }

        try
        {
            var remaining = entry.Length;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryRead(source, buffer, (int)Math.Min(buffer.Length, remaining), out var read, out var error))
                {
                    return Fail(PackWriteProblem.SourceUnreadable, $"Entry '{entry.Path}' could not be read: {error}");
                }

                if (read == 0)
                {
                    return Fail(PackWriteProblem.SourceChanged, $"Entry '{entry.Path}' gave {entry.Length - remaining} bytes, but {entry.Length} were announced.");
                }

                target.Write(buffer, 0, read);
                remaining -= read;
            }

            // One more byte means the source grew after its length was taken.
            if (!TryRead(source, buffer, 1, out var extra, out var probeError))
            {
                return Fail(PackWriteProblem.SourceUnreadable, $"Entry '{entry.Path}' could not be read: {probeError}");
            }

            return extra == 0
                ? new PackWriteResult(PackWriteProblem.None, null)
                : Fail(PackWriteProblem.SourceChanged, $"Entry '{entry.Path}' gave more than the {entry.Length} announced bytes.");
        }
        finally
        {
            CloseQuietly(source);
        }
    }

    private static bool TryRead(Stream source, byte[] buffer, int count, out int read, out string? error)
    {
        try
        {
            read = source.Read(buffer, 0, count);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            read = 0;
            error = ex.Message;
            return false;
        }
    }

    private static (long DependencyIndexSize, long EntryIndexSize) ComputeIndexSizes(IReadOnlyList<string> dependencies, IReadOnlyList<PackWriteEntry> entries)
    {
        long dependencyIndexSize = 0;
        foreach (var name in dependencies)
        {
            dependencyIndexSize += Encoding.UTF8.GetByteCount(name) + 1;
        }

        // Per entry: u32 length, one compression byte, the path and its terminating zero byte.
        long entryIndexSize = 0;
        foreach (var entry in entries)
        {
            entryIndexSize += sizeof(uint) + 1 + Encoding.UTF8.GetByteCount(entry.Path) + 1;
        }

        return (dependencyIndexSize, entryIndexSize);
    }

    private static bool HasValidForm(string path) =>
        path.Length > 0 && !path.Contains('\0') && !ToPackPath(path).Split('\\').Contains(string.Empty);

    // The pack stores '\'; this form is also what the entries are sorted and compared by.
    private static string ToPackPath(string path) => path.Replace('/', '\\');

    private static string Printable(string text) => text.Replace("\0", "\\0");

    private static PackWriteResult Fail(PackWriteProblem problem, string detail) => new(problem, detail);

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BitConverter.TryWriteBytes(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteZeroTerminated(Stream stream, string text)
    {
        stream.Write(Encoding.UTF8.GetBytes(text));
        stream.WriteByte(0);
    }

    // The caller already has its result; a file that cannot be removed is left for the caller to find.
    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void CloseQuietly(Stream stream)
    {
        try
        {
            stream.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
