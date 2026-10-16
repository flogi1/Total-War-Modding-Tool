namespace TotalWarModTool.PackFiles;

/// <summary>Reading logic behind <see cref="PackReader.ReadEntry"/>: reads one entry and decompresses it.</summary>
internal static class EntryReader
{
    // Stored bytes of a compressed entry: u32 uncompressed size, then the frame. The first 4 frame bytes name the format.
    private const int SizePrefixLength = 4;
    private const int FormatIdLength = 4;
    private const int ProbeLength = SizePrefixLength + FormatIdLength;
    private const int FrameOverheadAllowance = 65536;

    private const uint ZstdMagic = 0xFD2FB528;
    private const uint Lz4Magic = 0x184D2204;
    private const byte LzmaFirstByte = 0x5D;

    public static EntryContent Read(PackIndex index, PackEntry entry, CancellationToken cancellationToken, long maxBytes)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = ReadCore(index, entry, cancellationToken, maxBytes);
        cancellationToken.ThrowIfCancellationRequested();
        return content;
    }

    public static EntryContent ReadPrefix(PackIndex index, PackEntry entry, int length, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = ReadPrefixCore(index, entry, length, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return content;
    }

    private static EntryContent ReadPrefixCore(PackIndex index, PackEntry entry, int length, CancellationToken cancellationToken)
    {
        if (CheckAccess(index, entry) is { } failure)
        {
            return failure;
        }

        if (!entry.IsCompressed)
        {
            return WithStream(index, entry, stream =>
            {
                var bytes = new byte[(int)Math.Min(length, entry.StoredSize)];
                stream.ReadExactly(bytes);
                return ChangedDuringRead(index) ? ChangedAfterRead() : new EntryContent(bytes, CompressionFormat.None, entry.StoredSize, EntryProblem.None, null);
            });
        }

        if (entry.StoredSize == 0)
        {
            return new EntryContent([], CompressionFormat.None, 0, EntryProblem.None, null);
        }

        if (entry.StoredSize <= ProbeLength)
        {
            return Fail(EntryProblem.Corrupt, $"A compressed entry needs more than {ProbeLength} bytes but has {entry.StoredSize}.");
        }

        var zstd = WithStream(index, entry, stream =>
        {
            Span<byte> probe = stackalloc byte[ProbeLength];
            stream.ReadExactly(probe);
            var size = BitConverter.ToUInt32(probe[..SizePrefixLength]);
            var format = DetectFormat(probe[SizePrefixLength..]);
            if (format != CompressionFormat.Zstd)
            {
                // Not streamed: the caller falls back to the full read.
                return new EntryContent(null, format, size, EntryProblem.None, null);
            }

            var wanted = (int)Math.Min(length, size);
            var bytes = new byte[wanted];
            try
            {
                stream.Seek(entry.Offset + SizePrefixLength, SeekOrigin.Begin);
                using var decompression = new ZstdSharp.DecompressionStream(new BoundedStream(stream, entry.StoredSize - SizePrefixLength), leaveOpen: false);
                var read = decompression.ReadAtLeast(bytes, wanted, throwOnEndOfStream: false);
                if (read < wanted)
                {
                    return Fail(EntryProblem.Corrupt, $"The zstd frame produced {read} bytes but at least {wanted} were expected.", CompressionFormat.Zstd, size);
                }
            }
            catch (ZstdSharp.ZstdException ex)
            {
                return Fail(EntryProblem.Corrupt, $"The zstd frame is damaged: {ex.Message}", CompressionFormat.Zstd, size);
            }
            catch (EndOfStreamException)
            {
                // The bounded stream ends at the entry end, so this is a cut-off frame, not a changed pack.
                return Fail(EntryProblem.Corrupt, "The zstd frame ends before its content is complete.", CompressionFormat.Zstd, size);
            }

            return ChangedDuringRead(index) ? ChangedAfterRead() : new EntryContent(bytes, CompressionFormat.Zstd, size, EntryProblem.None, null);
        });

        if (zstd.Problem != EntryProblem.None || zstd.Format == CompressionFormat.Zstd)
        {
            return zstd;
        }

        var full = ReadCore(index, entry, cancellationToken, PackReader.DefaultMaxEntryBytes);
        return full.Bytes is { } data && data.Length > length ? full with { Bytes = data[..length] } : full;
    }

    /// <summary>Reads the size prefix and the format id of an entry (at most 8 bytes) and nothing else.</summary>
    public static EntryInfo ReadInfo(PackIndex index, PackEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = ReadInfoCore(index, entry);
        cancellationToken.ThrowIfCancellationRequested();
        return info;
    }

    private static EntryInfo ReadInfoCore(PackIndex index, PackEntry entry)
    {
        if (CheckAccess(index, entry) is { } failure)
        {
            return ToInfo(failure);
        }

        if (!entry.IsCompressed)
        {
            return new EntryInfo(CompressionFormat.None, entry.StoredSize, EntryProblem.None, null);
        }

        if (entry.StoredSize == 0)
        {
            return new EntryInfo(CompressionFormat.None, 0, EntryProblem.None, null);
        }

        if (entry.StoredSize <= ProbeLength)
        {
            return ToInfo(Fail(EntryProblem.Corrupt, $"A compressed entry needs more than {ProbeLength} bytes but has {entry.StoredSize}."));
        }

        return ToInfo(WithStream(index, entry, stream =>
        {
            Span<byte> probe = stackalloc byte[ProbeLength];
            stream.ReadExactly(probe);
            return new EntryContent(null, DetectFormat(probe[SizePrefixLength..]), BitConverter.ToUInt32(probe[..SizePrefixLength]), EntryProblem.None, null);
        }));
    }

    private static EntryInfo ToInfo(EntryContent content) => new(content.Format, content.UncompressedSize, content.Problem, content.Detail);

    // The checks both readers start with: the pack is unchanged, not encrypted, and the entry lies inside it.
    private static EntryContent? CheckAccess(PackIndex index, PackEntry entry)
    {
        if (FileFingerprint.TryRead(index.Fingerprint.Path) != index.Fingerprint)
        {
            return Fail(EntryProblem.PackChanged, "The pack file changed or disappeared since its index was read.");
        }

        if (index.Header.HasEncryptedData)
        {
            return Fail(EntryProblem.Encrypted, "The data section is encrypted (flag 0x10).");
        }

        if (entry.Offset < 0 || entry.StoredSize < 0 || entry.Offset > index.Fingerprint.Length - entry.StoredSize)
        {
            return Fail(EntryProblem.Corrupt, "The entry does not lie inside the pack file.");
        }

        return null;
    }

    private static EntryContent ReadCore(PackIndex index, PackEntry entry, CancellationToken cancellationToken, long maxBytes)
    {
        if (CheckAccess(index, entry) is { } failure)
        {
            return failure;
        }

        var limit = Math.Min(maxBytes, Array.MaxLength);
        if (!entry.IsCompressed)
        {
            return entry.StoredSize > limit
                ? Fail(EntryProblem.TooLarge, $"The entry has {entry.StoredSize} bytes; the limit is {limit}.", size: entry.StoredSize)
                : ReadPlain(index, entry);
        }

        if (entry.StoredSize == 0)
        {
            return new EntryContent([], CompressionFormat.None, 0, EntryProblem.None, null);
        }

        if (entry.StoredSize <= ProbeLength)
        {
            return Fail(EntryProblem.Corrupt, $"A compressed entry needs more than {ProbeLength} bytes but has {entry.StoredSize}.");
        }

        if (entry.StoredSize > Array.MaxLength)
        {
            return Fail(EntryProblem.TooLarge, $"The entry has {entry.StoredSize} bytes stored.");
        }

        return ReadCompressed(index, entry, cancellationToken, limit);
    }

    private static EntryContent ReadPlain(PackIndex index, PackEntry entry) =>
        WithStream(index, entry, stream =>
        {
            var bytes = new byte[entry.StoredSize];
            stream.ReadExactly(bytes);
            return ChangedDuringRead(index) ? ChangedAfterRead() : new EntryContent(bytes, CompressionFormat.None, bytes.Length, EntryProblem.None, null);
        });

    private static EntryContent ReadCompressed(PackIndex index, PackEntry entry, CancellationToken cancellationToken, long limit) =>
        WithStream(index, entry, stream =>
        {
            Span<byte> probe = stackalloc byte[ProbeLength];
            stream.ReadExactly(probe);
            var size = BitConverter.ToUInt32(probe[..SizePrefixLength]);
            var format = DetectFormat(probe[SizePrefixLength..]);
            if (size > limit)
            {
                return Fail(EntryProblem.TooLarge, $"The entry claims {size} bytes uncompressed; the limit is {limit}.", format, size);
            }

            if (format is CompressionFormat.Lzma or CompressionFormat.Unknown)
            {
                return Fail(EntryProblem.UnsupportedCompression, $"The compression format is {format}, which is not supported.", format, size);
            }

            // A compressed frame is never much larger than its content (worst case: stored blocks plus headers).
            var maxFrameLength = limit + limit / 255 + FrameOverheadAllowance;
            if (entry.StoredSize - SizePrefixLength > maxFrameLength)
            {
                return Fail(EntryProblem.TooLarge, $"The compressed frame has {entry.StoredSize - SizePrefixLength} bytes; the limit is {maxFrameLength}.", format, size);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var stored = new byte[entry.StoredSize];
            probe.CopyTo(stored);
            stream.ReadExactly(stored.AsSpan(ProbeLength));
            if (ChangedDuringRead(index))
            {
                return ChangedAfterRead();
            }

            var frame = stored.AsSpan(SizePrefixLength);
            return format == CompressionFormat.Zstd
                ? DecodeZstd(frame, (int)size)
                : DecodeLz4(frame, (int)size, cancellationToken);
        });

    // The first fingerprint check happens before the read; a change between that check and the end of the read is caught here.
    private static bool ChangedDuringRead(PackIndex index) => FileFingerprint.TryRead(index.Fingerprint.Path) != index.Fingerprint;

    private static EntryContent ChangedAfterRead() =>
        Fail(EntryProblem.PackChanged, "The pack file changed or disappeared while the entry was read.");

    private static EntryContent DecodeZstd(ReadOnlySpan<byte> frame, int size)
    {
        var buffer = new byte[size];
        try
        {
            using var decompressor = new ZstdSharp.Decompressor();
            var length = decompressor.Unwrap(frame, buffer);
            return length == size
                ? new EntryContent(buffer, CompressionFormat.Zstd, size, EntryProblem.None, null)
                : Fail(EntryProblem.Corrupt, $"The zstd frame produced {length} bytes but the entry claims {size}.", CompressionFormat.Zstd, size);
        }
        catch (ZstdSharp.ZstdException ex)
        {
            return Fail(EntryProblem.Corrupt, $"The zstd frame is damaged: {ex.Message}", CompressionFormat.Zstd, size);
        }
    }

    private static EntryContent DecodeLz4(ReadOnlySpan<byte> frame, int size, CancellationToken cancellationToken) =>
        Lz4Frame.TryDecode(frame, size, cancellationToken, out var data, out var error)
            ? new EntryContent(data, CompressionFormat.Lz4, size, EntryProblem.None, null)
            : Fail(EntryProblem.Corrupt, $"The LZ4 frame is damaged: {error}", CompressionFormat.Lz4, size);

    private static CompressionFormat DetectFormat(ReadOnlySpan<byte> identifier)
    {
        var magic = BitConverter.ToUInt32(identifier);
        if (magic == ZstdMagic)
        {
            return CompressionFormat.Zstd;
        }

        if (magic == Lz4Magic)
        {
            return CompressionFormat.Lz4;
        }

        return identifier[0] == LzmaFirstByte ? CompressionFormat.Lzma : CompressionFormat.Unknown;
    }

    // Only the exception types the file system raises for a locked, inaccessible or shrinking file are translated.
    private static EntryContent WithStream(PackIndex index, PackEntry entry, Func<FileStream, EntryContent> read)
    {
        try
        {
            using var stream = new FileStream(index.Fingerprint.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
            stream.Seek(entry.Offset, SeekOrigin.Begin);
            return read(stream);
        }
        catch (EndOfStreamException)
        {
            return Fail(EntryProblem.PackChanged, "The pack file ended inside the entry (it changed during the read?).");
        }
        catch (FileNotFoundException)
        {
            return Fail(EntryProblem.PackChanged, "The pack file disappeared during the read.");
        }
        catch (DirectoryNotFoundException)
        {
            return Fail(EntryProblem.PackChanged, "The pack folder disappeared during the read.");
        }
        catch (IOException ex)
        {
            return Fail(EntryProblem.Unreadable, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Fail(EntryProblem.Unreadable, ex.Message);
        }
    }

    private static EntryContent Fail(EntryProblem problem, string detail, CompressionFormat format = CompressionFormat.None, long size = 0) =>
        new(null, format, size, problem, detail);

    /// <summary>Forward-only view of the next <c>length</c> bytes of a stream, so the decoder never reads into the next entry.</summary>
    private sealed class BoundedStream(Stream inner, long length) : Stream
    {
        private long _remaining = length;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_remaining == 0 || buffer.Length == 0)
            {
                return 0;
            }

            var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, _remaining)]);
            _remaining -= read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
