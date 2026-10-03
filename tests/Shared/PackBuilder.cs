// Root namespace on purpose: resolves unqualified from every TotalWarModTool.<Module>.Tests namespace.
// BCL only: this file is compiled into every test project, including those that do not reference PackFiles.
using System.Globalization;
using System.Text;

namespace TotalWarModTool;

/// <summary>Builds PFH5 pack bytes for tests. Fields can be corrupted afterwards by patching the returned array.</summary>
public sealed class PackBuilder
{
    private const uint TimestampsFlag = 0x40;

    private readonly string _magic;
    private readonly uint _packType;
    private readonly List<string> _dependencies = [];
    private readonly List<(string Path, byte[] Bytes, bool Compressed)> _entries = [];
    private uint _flags;

    public PackBuilder(string magic = "PFH5", uint packType = 3)
    {
        _magic = magic;
        _packType = packType;
    }

    /// <summary>OR-ed into the bitmask (for example 0x40 timestamps, 0x80 encrypted index, 0x100 extended header).</summary>
    public PackBuilder WithFlags(uint flags)
    {
        _flags |= flags;
        return this;
    }

    public PackBuilder AddDependency(string packName)
    {
        _dependencies.Add(packName);
        return this;
    }

    /// <summary>The path is written exactly as given, including backslashes.</summary>
    public PackBuilder AddEntry(string path, byte[] storedBytes, bool compressed = false)
    {
        _entries.Add((path, storedBytes, compressed));
        return this;
    }

    public byte[] Build()
    {
        var withTimestamps = (_flags & TimestampsFlag) != 0;

        var dependencyIndex = new MemoryStream();
        foreach (var name in _dependencies)
        {
            WriteZeroTerminated(dependencyIndex, name);
        }

        var entryIndex = new MemoryStream();
        foreach (var (path, bytes, compressed) in _entries)
        {
            WriteUInt32(entryIndex, (uint)bytes.Length);
            if (withTimestamps)
            {
                WriteUInt32(entryIndex, 0);
            }

            entryIndex.WriteByte(compressed ? (byte)1 : (byte)0);
            WriteZeroTerminated(entryIndex, path);
        }

        var result = new MemoryStream();
        result.Write(Encoding.ASCII.GetBytes(_magic));
        WriteUInt32(result, (_packType & 0xF) | _flags);
        WriteUInt32(result, (uint)_dependencies.Count);
        WriteUInt32(result, (uint)dependencyIndex.Length);
        WriteUInt32(result, (uint)_entries.Count);
        WriteUInt32(result, (uint)entryIndex.Length);
        WriteUInt32(result, 0);
        result.Write(dependencyIndex.ToArray());
        result.Write(entryIndex.ToArray());
        foreach (var (_, bytes, _) in _entries)
        {
            result.Write(bytes);
        }

        return result.ToArray();
    }

    public string WriteTo(string filePath)
    {
        File.WriteAllBytes(filePath, Build());
        return filePath;
    }

    /// <summary>The stored form of a compressed entry: 4-byte little-endian uncompressed size followed by the frame.</summary>
    public static byte[] Stored(byte[] frame, int uncompressedSize)
    {
        var result = new byte[4 + frame.Length];
        BitConverter.GetBytes(uncompressedSize).CopyTo(result, 0);
        frame.CopyTo(result, 4);
        return result;
    }

    /// <summary>Parses hex like "04 22 4D 18"; spaces and '|' are ignored.</summary>
    public static byte[] Hex(string hex)
    {
        var digits = hex.Where(c => c is not (' ' or '|')).ToArray();
        if (digits.Length % 2 != 0)
        {
            throw new ArgumentException("Odd number of hex digits.", nameof(hex));
        }

        var result = new byte[digits.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = byte.Parse(new string(digits, i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static void WriteUInt32(Stream stream, uint value) => stream.Write(BitConverter.GetBytes(value));

    private static void WriteZeroTerminated(Stream stream, string text)
    {
        stream.Write(Encoding.UTF8.GetBytes(text));
        stream.WriteByte(0);
    }
}
