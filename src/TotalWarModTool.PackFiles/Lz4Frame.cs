namespace TotalWarModTool.PackFiles;

/// <summary>
/// Decoder for one LZ4 frame. Checksums are not verified. Every malformed input yields <c>false</c> with an error text;
/// all reads and writes are bounds-checked against the input and against the expected output size.
/// </summary>
public static class Lz4Frame
{
    private const uint Magic = 0x184D2204;
    private const int FrameVersion = 1;
    private const byte FlagDictionaryId = 0x01;
    private const byte FlagContentChecksum = 0x04;
    private const byte FlagContentSize = 0x08;
    private const byte FlagBlockChecksum = 0x10;
    private const uint UncompressedBlockBit = 0x80000000;
    private const int MinMatchLength = 4;

    /// <summary>
    /// Decodes <paramref name="frame"/> into a buffer of exactly <paramref name="expectedSize"/> bytes.
    /// The caller bounds <paramref name="expectedSize"/>; it is the only number that sizes an allocation.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> frame, int expectedSize, out byte[] data, out string? error)
    {
        data = [];
        error = null;
        if (expectedSize < 0)
        {
            error = "The expected size is negative.";
            return false;
        }

        // Every input byte yields at most 255 output bytes (a 255 length extension), so more is impossible.
        if (expectedSize > frame.Length * 255L)
        {
            error = $"A frame of {frame.Length} bytes cannot produce {expectedSize} bytes.";
            return false;
        }

        if (frame.Length < 7 || ReadUInt32(frame, 0) != Magic)
        {
            error = "The data does not start with the LZ4 frame magic number.";
            return false;
        }

        var flags = frame[4];
        if (flags >> 6 != FrameVersion)
        {
            error = $"Unsupported LZ4 frame version {flags >> 6}.";
            return false;
        }

        // Magic, FLG and BD are in front; the optional content size and dictionary id follow, then the header checksum.
        var position = 6;
        if ((flags & FlagContentSize) != 0)
        {
            position += 8;
        }

        if ((flags & FlagDictionaryId) != 0)
        {
            position += 4;
        }

        position += 1;
        if (position > frame.Length)
        {
            error = "The frame ends inside the header.";
            return false;
        }

        var output = new byte[expectedSize];
        var written = 0;
        while (true)
        {
            if (frame.Length - position < 4)
            {
                error = "The frame ends before the end mark.";
                return false;
            }

            var sizeField = ReadUInt32(frame, position);
            position += 4;
            if (sizeField == 0)
            {
                break;
            }

            var blockSize = sizeField & ~UncompressedBlockBit;
            if (blockSize > frame.Length - position)
            {
                error = $"A block of {blockSize} bytes lies beyond the end of the frame.";
                return false;
            }

            var block = frame.Slice(position, (int)blockSize);
            position += (int)blockSize;
            if ((sizeField & UncompressedBlockBit) != 0)
            {
                if (block.Length > output.Length - written)
                {
                    error = "The output exceeds the expected size.";
                    return false;
                }

                block.CopyTo(output.AsSpan(written));
                written += block.Length;
            }
            else if (!TryDecodeBlock(block, output, ref written, out error))
            {
                return false;
            }

            if ((flags & FlagBlockChecksum) != 0)
            {
                if (frame.Length - position < 4)
                {
                    error = "The frame ends inside a block checksum.";
                    return false;
                }

                position += 4;
            }
        }

        if ((flags & FlagContentChecksum) != 0)
        {
            if (frame.Length - position < 4)
            {
                error = "The frame ends inside the content checksum.";
                return false;
            }

            position += 4;
        }

        if (position != frame.Length)
        {
            error = $"{frame.Length - position} bytes follow the end of the frame.";
            return false;
        }

        if (written != expectedSize)
        {
            error = $"The frame produced {written} bytes but {expectedSize} were expected.";
            return false;
        }

        data = output;
        return true;
    }

    // Matches may reach back into earlier blocks: the offset is checked against all output produced so far.
    private static bool TryDecodeBlock(ReadOnlySpan<byte> block, byte[] output, ref int written, out string? error)
    {
        error = null;
        var position = 0;
        while (position < block.Length)
        {
            var token = block[position++];
            if (!TryReadLength(block, ref position, token >> 4, out var literalLength))
            {
                error = "The block ends inside a literal length.";
                return false;
            }

            if (literalLength > block.Length - position)
            {
                error = "The block ends inside its literals.";
                return false;
            }

            if (literalLength > output.Length - written)
            {
                error = "The output exceeds the expected size.";
                return false;
            }

            block.Slice(position, (int)literalLength).CopyTo(output.AsSpan(written));
            position += (int)literalLength;
            written += (int)literalLength;
            if (position == block.Length)
            {
                return true;
            }

            if (block.Length - position < 2)
            {
                error = "The block ends inside a match offset.";
                return false;
            }

            var offset = block[position] | (block[position + 1] << 8);
            position += 2;
            if (offset == 0 || offset > written)
            {
                error = $"Invalid match offset {offset} after {written} output bytes.";
                return false;
            }

            if (!TryReadLength(block, ref position, token & 0xF, out var matchLength))
            {
                error = "The block ends inside a match length.";
                return false;
            }

            matchLength += MinMatchLength;
            if (matchLength > output.Length - written)
            {
                error = "The output exceeds the expected size.";
                return false;
            }

            CopyMatch(output, written, offset, (int)matchLength);
            written += (int)matchLength;
        }

        return true;
    }

    // A nibble of 15 is extended by bytes that are added until one is below 255. The sum is capped by the caller's bounds checks.
    private static bool TryReadLength(ReadOnlySpan<byte> block, ref int position, int nibble, out long length)
    {
        length = nibble;
        if (nibble != 15)
        {
            return true;
        }

        byte extension;
        do
        {
            if (position >= block.Length)
            {
                return false;
            }

            extension = block[position++];
            length += extension;
        }
        while (extension == 255);

        return true;
    }

    private static void CopyMatch(byte[] output, int written, int offset, int length)
    {
        var source = written - offset;
        if (offset >= length)
        {
            output.AsSpan(source, length).CopyTo(output.AsSpan(written));
            return;
        }

        // The match overlaps its own output and repeats the last <offset> bytes.
        for (var i = 0; i < length; i++)
        {
            output[written + i] = output[source + i];
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int position) =>
        (uint)(bytes[position] | (bytes[position + 1] << 8) | (bytes[position + 2] << 16) | (bytes[position + 3] << 24));
}
