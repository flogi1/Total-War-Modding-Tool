using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Workspace;

public enum EntryKind { Table, Loc, Text, Other }

/// <summary>
/// Result of opening one entry. Exactly one of <see cref="Table"/>, <see cref="Loc"/>, <see cref="Text"/> is set when the kind
/// matches and the read worked. <see cref="ReadProblem"/> is the problem of reading or decompressing (<see cref="ReadDetail"/> is
/// short technical English); decode problems live inside <see cref="Table"/> and <see cref="Loc"/>.
/// <see cref="SchemaMissing"/> is true for a table when no schema was given: nothing was read then.
/// </summary>
public sealed record OpenedEntry(string EntryPath, EntryKind Kind, long StoredSize, long UncompressedSize, CompressionFormat Format,
    EntryProblem ReadProblem, string? ReadDetail, bool SchemaMissing, DecodedTable? Table, DecodedLoc? Loc, TextContent? Text);

public static class EntryOpener
{
    private const string LocExtension = ".loc";

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lua", ".xml", ".txt", ".json", ".csv", ".tsv",
    };

    /// <summary>A DB table by its path, a Loc file or a text file by its extension (ignoring case), everything else is other.</summary>
    public static EntryKind KindOf(string entryPath)
    {
        ArgumentNullException.ThrowIfNull(entryPath);
        if (DbTablePath.TryGetTableName(entryPath, out _))
        {
            return EntryKind.Table;
        }

        var extension = Path.GetExtension(entryPath);
        if (string.Equals(extension, LocExtension, StringComparison.OrdinalIgnoreCase))
        {
            return EntryKind.Loc;
        }

        return TextExtensions.Contains(extension) ? EntryKind.Text : EntryKind.Other;
    }

    /// <summary>
    /// Opens one entry. Other files and tables without a schema are not read, only their size and format are asked for.
    /// Only <see cref="OperationCanceledException"/> and argument errors are thrown.
    /// </summary>
    public static OpenedEntry Open(PackIndex index, PackEntry entry, GameSchema? schema, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        var kind = KindOf(entry.Path);
        var tableName = string.Empty;
        var isTable = kind == EntryKind.Table && DbTablePath.TryGetTableName(entry.Path, out tableName);
        if (kind == EntryKind.Other || (isTable && schema is null))
        {
            var info = PackReader.ReadEntryInfo(index, entry, cancellationToken);
            return new OpenedEntry(entry.Path, kind, entry.StoredSize, info.UncompressedSize, info.Format, info.Problem, info.Detail,
                SchemaMissing: isTable, Table: null, Loc: null, Text: null);
        }

        var content = PackReader.ReadEntry(index, entry, cancellationToken);
        if (content.Problem != EntryProblem.None || content.Bytes is not { } bytes)
        {
            return new OpenedEntry(entry.Path, kind, entry.StoredSize, content.UncompressedSize, content.Format, content.Problem, content.Detail,
                SchemaMissing: false, Table: null, Loc: null, Text: null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var opened = new OpenedEntry(entry.Path, kind, entry.StoredSize, content.UncompressedSize, content.Format, EntryProblem.None, null,
            SchemaMissing: false, Table: null, Loc: null, Text: null);
        return kind switch
        {
            EntryKind.Table => opened with { Table = DbTableDecoder.Decode(bytes, tableName, schema!) },
            EntryKind.Loc => opened with { Loc = LocDecoder.Decode(bytes) },
            _ => opened with { Text = EntryText.Decode(bytes) },
        };
    }
}
