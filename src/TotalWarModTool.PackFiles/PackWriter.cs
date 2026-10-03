using System.Text;

namespace TotalWarModTool.PackFiles;

/// <summary>
/// Writes PFH5 mod packs as a stream: header, dependency index, entry index, then the data of every entry in index order.
/// Entries are never loaded completely; one copy buffer is the most that is held in memory. Problems are reported as a
/// <see cref="PackWriteResult"/>; only <see cref="OperationCanceledException"/> and argument errors are thrown, plus any other
/// exception that a source stream raises (see <see cref="Write"/>).
/// </summary>
public static class PackWriter
{
    // Same cap as the reader applies to one path or dependency name.
    private const int MaxNameBytes = 65535;
    private const int CopyBufferSize = 80 * 1024;
    private const int FileBufferSize = 64 * 1024;
    private const int MaxDetailChars = 200;
    private const int DeleteAttempts = 3;
    private const int DeleteRetryDelayMilliseconds = 100;

    private const string Magic = "PFH5";
    private const uint PackTypeMod = 3;
    private const byte NotCompressed = 0;

    /// <summary>
    /// Pure: checks paths, dependencies and the limits of the format and opens nothing. The first problem wins, checked in this
    /// order: dependencies, path form, path length, duplicate paths, entry sizes, index sizes. A path segment must not be empty,
    /// '.' or '..', and names must not contain a zero character or an unpaired UTF-16 surrogate (it has no UTF-8 form). A negative
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
                throw new ArgumentException($"The length of entry {Quote(entry.Path)} is negative.", nameof(entries));
            }
        }

        foreach (var name in dependencies)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (name.Length == 0 || name.Contains('\0') || HasUnpairedSurrogate(name))
            {
                return Fail(PackWriteProblem.InvalidDependency, $"The dependency name {Quote(name)} is empty or contains a zero character or an unpaired surrogate.");
            }

            var bytes = Encoding.UTF8.GetByteCount(name);
            if (bytes > MaxNameBytes)
            {
                return Fail(PackWriteProblem.InvalidDependency, $"The dependency name {Quote(name)} has {bytes} UTF-8 bytes, more than {MaxNameBytes}.");
            }
        }

        foreach (var entry in entries)
        {
            if (!HasValidForm(entry.Path))
            {
                return Fail(PackWriteProblem.InvalidPath, $"The path {Quote(entry.Path)} is empty, contains a zero character or an unpaired surrogate, or has an empty, '.' or '..' segment.");
            }
        }

        foreach (var entry in entries)
        {
            var bytes = Encoding.UTF8.GetByteCount(entry.Path);
            if (bytes > MaxNameBytes)
            {
                return Fail(PackWriteProblem.PathTooLong, $"The path {Quote(entry.Path)} has {bytes} UTF-8 bytes, more than {MaxNameBytes}.");
            }
        }

        var seen = new Dictionary<string, string>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var packPath = ToPackPath(entry.Path);
            if (!seen.TryAdd(packPath, entry.Path))
            {
                return Fail(PackWriteProblem.DuplicatePath, $"The paths {Quote(seen[packPath])} and {Quote(entry.Path)} differ only in case or separator.");
            }
        }

        foreach (var entry in entries)
        {
            if (entry.Length > uint.MaxValue)
            {
                return Fail(PackWriteProblem.EntryTooLarge, $"The entry {Quote(entry.Path)} is larger than {uint.MaxValue} bytes.");
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
    /// gets the number of entries written so far. Throws only <see cref="OperationCanceledException"/> and argument errors;
    /// an exception of another type that <see cref="PackWriteEntry.Open"/> or a source stream raises leaves as well, after the
    /// cleanup. A failed validation never touches <paramref name="targetPath"/>.
    /// After a problem, a cancel or such an exception the incomplete target is removed (up to three tries, 100 ms apart). On a
    /// problem result the <see cref="PackWriteResult.Detail"/> says if that failed; after an exception the caller must not
    /// assume the file is gone, because the delete can fail (it is removed in the normal case). Never point this at a live pack:
    /// a failed write deletes its target.
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

        var keepTarget = false;
        var cleanupDone = false;
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

            if (result.Problem == PackWriteProblem.None)
            {
                keepTarget = true;
                return result;
            }

            cleanupDone = true;
            return DeleteWithRetry(targetPath)
                ? result
                : result with { Detail = $"{result.Detail} The incomplete file {Quote(targetPath)} could not be removed." };
        }
        finally
        {
            if (!keepTarget && !cleanupDone)
            {
                DeleteWithRetry(targetPath);
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
            cancellationToken.ThrowIfCancellationRequested();
            WriteZeroTerminated(target, name);
        }

        foreach (var (packPath, entry) in sorted)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            source = entry.Open() ?? throw new ArgumentException($"Open of entry {Quote(entry.Path)} returned no stream.", nameof(entry));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(PackWriteProblem.SourceUnreadable, $"Entry {Quote(entry.Path)} could not be opened: {ex.Message}");
        }

        try
        {
            var remaining = entry.Length;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryRead(source, buffer, (int)Math.Min(buffer.Length, remaining), out var read, out var error))
                {
                    return Fail(PackWriteProblem.SourceUnreadable, $"Entry {Quote(entry.Path)} could not be read: {error}");
                }

                if (read == 0)
                {
                    return Fail(PackWriteProblem.SourceChanged, $"Entry {Quote(entry.Path)} gave {entry.Length - remaining} bytes, but {entry.Length} were announced.");
                }

                target.Write(buffer, 0, read);
                remaining -= read;
            }

            // One more byte means the source grew after its length was taken.
            if (!TryRead(source, buffer, 1, out var extra, out var probeError))
            {
                return Fail(PackWriteProblem.SourceUnreadable, $"Entry {Quote(entry.Path)} could not be read: {probeError}");
            }

            return extra == 0
                ? new PackWriteResult(PackWriteProblem.None, null)
                : Fail(PackWriteProblem.SourceChanged, $"Entry {Quote(entry.Path)} gave more than the {entry.Length} announced bytes.");
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

    // The sizes are computed from the same normalised form that is written, and Validate has already rejected every text
    // without a UTF-8 form, so the byte counts here are exactly the bytes of the file.
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
            entryIndexSize += sizeof(uint) + 1 + Encoding.UTF8.GetByteCount(ToPackPath(entry.Path)) + 1;
        }

        return (dependencyIndexSize, entryIndexSize);
    }

    private static bool HasValidForm(string path)
    {
        if (path.Length == 0 || path.Contains('\0') || HasUnpairedSurrogate(path))
        {
            return false;
        }

        foreach (var segment in ToPackPath(path).Split('\\'))
        {
            if (segment is "" or "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasUnpairedSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    // The pack stores '\'; this form is also what the entries are sorted and compared by.
    private static string ToPackPath(string path) => path.Replace('/', '\\');

    /// <summary>
    /// Text for a Detail: at most <see cref="MaxDetailChars"/> characters, never cut inside a surrogate pair, zero characters and
    /// unpaired surrogates written as escapes, in single quotes.
    /// </summary>
    private static string Quote(string text)
    {
        var length = Math.Min(text.Length, MaxDetailChars);
        if (length < text.Length && char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]))
        {
            length--;
        }

        var builder = new StringBuilder("'");
        for (var i = 0; i < length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c).Append(text[++i]);
            }
            else if (c == '\0')
            {
                builder.Append("\\0");
            }
            else if (char.IsSurrogate(c))
            {
                builder.Append("\\u").Append(((int)c).ToString("X4"));
            }
            else
            {
                builder.Append(c);
            }
        }

        builder.Append('\'');
        if (length < text.Length)
        {
            builder.Append(" (shortened)");
        }

        return builder.ToString();
    }

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

    // Returns whether the file is gone. The wait happens only after a failed attempt, so the normal case does not wait.
    private static bool DeleteWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == DeleteAttempts)
                {
                    return false;
                }

                Thread.Sleep(DeleteRetryDelayMilliseconds);
            }
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
