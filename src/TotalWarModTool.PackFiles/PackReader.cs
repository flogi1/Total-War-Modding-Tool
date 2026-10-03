using System.Text;

namespace TotalWarModTool.PackFiles;

/// <summary>
/// Reads the header and the file index of PFH5 packs. Read only: the data section is never touched.
/// Broken, truncated or hostile files produce a <see cref="PackProblem"/>, never an exception or a partial result.
/// </summary>
public static class PackReader
{
    public const int HeaderSize = 28;

    /// <summary>Default upper bound for the size of one entry (stored or uncompressed): 256 MiB.</summary>
    public const long DefaultMaxEntryBytes = 268_435_456;

    // Real paths are far shorter. The cap keeps a missing terminator from growing a buffer to the size of the index.
    private const int MaxPathBytes = 65535;
    private const int ReadBufferSize = 64 * 1024;

    private const string Magic = "PFH5";
    private const uint PackTypeMask = 0xF;
    private const uint FlagEncryptedData = 0x10;
    private const uint FlagIndexTimestamps = 0x40;
    private const uint FlagEncryptedIndex = 0x80;
    private const uint FlagExtendedHeader = 0x100;

    public static PackHeaderResult ReadHeader(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return WithStream(path, ParseHeader, (problem, detail) => new PackHeaderResult(null, problem, detail));
    }

    public static PackIndexResult ReadIndex(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return WithStream(path, stream => ParseIndex(path, stream), (problem, detail) => new PackIndexResult(null, problem, detail));
    }

    /// <summary>
    /// Reads and, if needed, decompresses one entry of the pack that <paramref name="index"/> describes. Only
    /// <see cref="OperationCanceledException"/> and argument errors are thrown; every other failure is an <see cref="EntryProblem"/>.
    /// </summary>
    public static EntryContent ReadEntry(PackIndex index, PackEntry entry, CancellationToken cancellationToken, long maxBytes = DefaultMaxEntryBytes)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(entry);
        return EntryReader.Read(index, entry, cancellationToken, maxBytes);
    }

    // Only the exception types that the file system raises for a missing, locked, inaccessible or shrinking file are translated.
    private static T WithStream<T>(string path, Func<FileStream, T> read, Func<PackProblem, string, T> fail)
    {
        try
        {
            using var stream = Open(path);
            return read(stream);
        }
        catch (FileNotFoundException ex)
        {
            return fail(PackProblem.NotFound, ex.Message);
        }
        catch (DirectoryNotFoundException ex)
        {
            return fail(PackProblem.NotFound, ex.Message);
        }
        catch (EndOfStreamException)
        {
            return fail(PackProblem.Truncated, "The file ended while the index was read (it changed during the read?).");
        }
        catch (IOException ex)
        {
            return fail(PackProblem.Unreadable, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return fail(PackProblem.Unreadable, ex.Message);
        }
    }
    private static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);

    private static PackHeaderResult ParseHeader(FileStream stream)
    {
        Span<byte> bytes = stackalloc byte[HeaderSize];
        if (stream.ReadAtLeast(bytes, HeaderSize, throwOnEndOfStream: false) < HeaderSize)
        {
            return new PackHeaderResult(null, PackProblem.Truncated, $"The file is shorter than the {HeaderSize}-byte header.");
        }

        var magic = ReadMagic(bytes[..4]);
        var bitmask = BitConverter.ToUInt32(bytes[4..8]);
        if (magic != Magic)
        {
            return new PackHeaderResult(null, PackProblem.UnknownFormat, $"Unsupported pack identifier '{magic}'.");
        }

        if ((bitmask & FlagExtendedHeader) != 0)
        {
            return new PackHeaderResult(null, PackProblem.UnknownFormat, $"Pack '{magic}' has an extended header (flag 0x100), which is not supported.");
        }

        var rawType = bitmask & PackTypeMask;
        var header = new PackHeader(
            magic,
            rawType <= (uint)PackType.Movie ? (PackType)rawType : PackType.Unknown,
            HasIndexTimestamps: (bitmask & FlagIndexTimestamps) != 0,
            HasEncryptedIndex: (bitmask & FlagEncryptedIndex) != 0,
            HasEncryptedData: (bitmask & FlagEncryptedData) != 0,
            DependencyCount: BitConverter.ToUInt32(bytes[8..12]),
            DependencyIndexSize: BitConverter.ToUInt32(bytes[12..16]),
            EntryCount: BitConverter.ToUInt32(bytes[16..20]),
            EntryIndexSize: BitConverter.ToUInt32(bytes[20..24]));
        return new PackHeaderResult(header, PackProblem.None, null);
    }

    private static string ReadMagic(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = bytes[i] is >= 0x20 and < 0x7F ? (char)bytes[i] : '?';
        }

        return new string(chars);
    }

    private static PackIndexResult ParseIndex(string path, FileStream stream)
    {
        var fileLength = stream.Length;
        var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);

        var headerResult = ParseHeader(stream);
        if (headerResult.Header is not { } header)
        {
            return Fail(headerResult.Problem, headerResult.Detail);
        }

        if (header.HasEncryptedIndex)
        {
            return Fail(PackProblem.EncryptedIndex, "The index is encrypted (flag 0x80).");
        }

        var dataStart = HeaderSize + header.DependencyIndexSize + header.EntryIndexSize;
        if (dataStart > fileLength)
        {
            return Fail(PackProblem.Truncated, $"Header and indexes need {dataStart} bytes but the file has {fileLength}.");
        }

        var dependencies = new List<string>();
        var dependencySection = new SectionReader(stream, header.DependencyIndexSize);
        for (long i = 0; i < header.DependencyCount; i++)
        {
            var read = dependencySection.TryReadZeroTerminated(out var name);
            if (read != TextRead.Ok)
            {
                return Fail(PackProblem.InconsistentIndex, DescribeTextFailure(read, $"dependency {i}"));
            }

            dependencies.Add(name);
        }

        if (dependencySection.Remaining != 0)
        {
            return Fail(PackProblem.InconsistentIndex, $"The dependency index has {dependencySection.Remaining} bytes after the last of {header.DependencyCount} names.");
        }

        var entries = new List<PackEntry>();
        var entrySection = new SectionReader(stream, header.EntryIndexSize);
        var offset = dataStart;
        for (long i = 0; i < header.EntryCount; i++)
        {
            if (!entrySection.TryReadUInt32(out var storedSize)
                || (header.HasIndexTimestamps && !entrySection.TryReadUInt32(out _))
                || !entrySection.TryReadByte(out var compressed))
            {
                return Fail(PackProblem.InconsistentIndex, $"The entry index ends inside entry {i} of {header.EntryCount}.");
            }

            if (compressed > 1)
            {
                return Fail(PackProblem.InconsistentIndex, $"Entry {i} has the invalid compression marker {compressed}.");
            }

            var read = entrySection.TryReadZeroTerminated(out var entryPath);
            if (read != TextRead.Ok)
            {
                return Fail(PackProblem.InconsistentIndex, DescribeTextFailure(read, $"the path of entry {i}"));
            }

            entries.Add(new PackEntry(entryPath.Replace('\\', '/'), offset, storedSize, compressed == 1));
            offset += storedSize;
        }

        if (entrySection.Remaining != 0)
        {
            return Fail(PackProblem.InconsistentIndex, $"The entry index has {entrySection.Remaining} bytes after the last of {header.EntryCount} entries.");
        }

        if (offset != fileLength)
        {
            return Fail(PackProblem.InconsistentIndex, $"The entry data ends at {offset} but the file has {fileLength} bytes.");
        }

        var fingerprint = new FileFingerprint(path, fileLength, lastWriteTimeUtc);
        return new PackIndexResult(new PackIndex(fingerprint, header, dependencies, entries), PackProblem.None, null);
    }

    private static PackIndexResult Fail(PackProblem problem, string? detail) => new(null, problem, detail);

    private static string DescribeTextFailure(TextRead read, string what) => read == TextRead.TooLong
        ? $"The text of {what} is longer than {MaxPathBytes} bytes."
        : $"The index ends inside {what} (no terminating zero byte).";

    private enum TextRead
    {
        Ok,
        EndOfSection,
        TooLong,
    }

    /// <summary>Reads at most <c>length</c> bytes from the current position of the stream, buffered, and reports what is left.</summary>
    private sealed class SectionReader(Stream stream, long length)
    {
        private readonly byte[] _buffer = new byte[ReadBufferSize];
        private long _notYetBuffered = length;
        private int _position;
        private int _filled;

        public long Remaining => _notYetBuffered + (_filled - _position);

        public bool TryReadByte(out byte value)
        {
            if (_position == _filled && !Fill())
            {
                value = 0;
                return false;
            }

            value = _buffer[_position++];
            return true;
        }

        public bool TryReadUInt32(out uint value)
        {
            value = 0;
            for (var shift = 0; shift < 32; shift += 8)
            {
                if (!TryReadByte(out var b))
                {
                    return false;
                }

                value |= (uint)b << shift;
            }

            return true;
        }

        public TextRead TryReadZeroTerminated(out string text)
        {
            text = string.Empty;
            List<byte>? collected = null;
            while (true)
            {
                if (_position == _filled && !Fill())
                {
                    return TextRead.EndOfSection;
                }

                var chunk = _buffer.AsSpan(_position, _filled - _position);
                var zero = chunk.IndexOf((byte)0);
                var take = zero < 0 ? chunk.Length : zero;
                if ((collected?.Count ?? 0) + take > MaxPathBytes)
                {
                    return TextRead.TooLong;
                }

                if (zero >= 0 && collected is null)
                {
                    // Common case: the whole text lies in the buffer.
                    text = Encoding.UTF8.GetString(chunk[..take]);
                    _position += take + 1;
                    return TextRead.Ok;
                }

                collected ??= [];
                collected.AddRange(chunk[..take].ToArray());
                _position += take;
                if (zero >= 0)
                {
                    _position++;
                    text = Encoding.UTF8.GetString(collected.ToArray());
                    return TextRead.Ok;
                }
            }
        }
        private bool Fill()
        {
            if (_notYetBuffered == 0)
            {
                return false;
            }

            var read = stream.Read(_buffer, 0, (int)Math.Min(_buffer.Length, _notYetBuffered));
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            _position = 0;
            _filled = read;
            _notYetBuffered -= read;
            return true;
        }
    }
}
