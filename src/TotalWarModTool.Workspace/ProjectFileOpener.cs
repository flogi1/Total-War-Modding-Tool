using System.Globalization;
using System.Text;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Workspace;

/// <summary>Opens a file of a mod project for display, with the same result type as <see cref="EntryOpener"/> for pack entries.</summary>
public static class ProjectFileOpener
{
    /// <summary>Files above this size are not read for display (<see cref="EntryProblem.TooLarge"/>).</summary>
    public const long MaxFileBytes = 268_435_456;

    private const string NotUtf8 = "the file is not valid UTF-8";

    /// <summary>
    /// Opens one file below pack\ of a project for display. <paramref name="relativeFile"/> is relative to the pack folder, with '\' or '/';
    /// <see cref="OpenedEntry.EntryPath"/> of the result is the same path with '/'. Table and loc files in text form are read with
    /// <see cref="TableTsv"/>; other files are taken for a binary table, a binary loc file, text or something else by their pack path, as
    /// <see cref="EntryOpener.KindOf"/> does. A file that is not read (other files, a table without a schema) only reports its size.
    /// Only <see cref="OperationCanceledException"/> and argument errors (including a path outside the pack folder) are thrown.
    /// </summary>
    public static OpenedEntry Open(string projectFolder, string relativeFile, GameSchema? schema, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFile);
        CheckInsidePackFolder(relativeFile);
        cancellationToken.ThrowIfCancellationRequested();

        var entryPath = relativeFile.Replace('\\', '/');
        var (packPath, fileKind) = ProjectLayout.FromRelativeFile(relativeFile);
        var kind = fileKind switch
        {
            ProjectFileKind.Table => EntryKind.Table,
            ProjectFileKind.Loc => EntryKind.Loc,
            _ => EntryOpener.KindOf(packPath),
        };
        var isTable = DbTablePath.TryGetTableName(packPath, out var tableName) && kind == EntryKind.Table;
        var packFolder = Path.Combine(projectFolder, ProjectFile.PackFolder);
        var fullPath = Path.Combine(packFolder, relativeFile);
        CheckResolvedInsidePackFolder(packFolder, fullPath, relativeFile);

        long length;
        try
        {
            length = new FileInfo(fullPath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Result(entryPath, kind, 0, EntryProblem.Unreadable, ex.Message);
        }

        // Only the size is asked for: a file that is shown as "other" or that cannot be decoded without a schema is never read.
        if (kind == EntryKind.Other || (isTable && schema is null))
        {
            return Result(entryPath, kind, length, EntryProblem.None, null, schemaMissing: isTable);
        }

        if (length > MaxFileBytes)
        {
            return Result(entryPath, kind, length, EntryProblem.TooLarge, $"the file is larger than {MaxFileBytes.ToString(CultureInfo.InvariantCulture)} bytes");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Result(entryPath, kind, length, EntryProblem.Unreadable, ex.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var opened = Result(entryPath, kind, bytes.Length, EntryProblem.None, null);
        return kind switch
        {
            EntryKind.Table => opened with
            {
                Table = fileKind == ProjectFileKind.Table
                    ? ReadTableText(bytes, tableName, schema!)
                    : DbTableDecoder.Decode(bytes, tableName, schema!),
            },
            EntryKind.Loc => opened with
            {
                Loc = fileKind == ProjectFileKind.Loc ? ReadLocText(bytes) : LocDecoder.Decode(bytes),
            },
            _ => opened with { Text = EntryText.Decode(bytes) },
        };
    }

    private static OpenedEntry Result(string entryPath, EntryKind kind, long size, EntryProblem problem, string? detail, bool schemaMissing = false) =>
        new(entryPath, kind, size, size, CompressionFormat.None, problem, detail, schemaMissing, Table: null, Loc: null, Text: null);

    private static DecodedTable ReadTableText(byte[] bytes, string tableName, GameSchema schema)
    {
        if (!TryDecodeStrict(bytes, out var text))
        {
            return FailedTable(tableName, NotUtf8);
        }

        var read = TableTsv.Read(text, schema);
        if (read.Table is { } table)
        {
            // The same rule as the build: a file whose meta line names another table than its folder would not be packed.
            if (EntryConversion.TableNameMismatch(table.TableName, tableName) is { } mismatch)
            {
                return FailedTable(tableName, $"line {EntryConversion.MetaLine.ToString(CultureInfo.InvariantCulture)}: {mismatch}");
            }

            return new DecodedTable(TableDecodeStatus.Ok, table.TableName, table.Version, HasGuid: false, table.Definition, table.Rows, null);
        }

        return FailedTable(tableName, Describe(read.Problem!));
    }

    private static DecodedLoc ReadLocText(byte[] bytes)
    {
        if (!TryDecodeStrict(bytes, out var text))
        {
            return new DecodedLoc(LocDecodeStatus.Failed, [], NotUtf8);
        }

        var read = TableTsv.ReadLoc(text);
        return read.Loc is { } loc
            ? new DecodedLoc(LocDecodeStatus.Ok, loc.Rows, null)
            : new DecodedLoc(LocDecodeStatus.Failed, [], Describe(read.Problem!));
    }

    private static DecodedTable FailedTable(string tableName, string detail) =>
        new(TableDecodeStatus.Failed, tableName, 0, HasGuid: false, Definition: null, [], detail);

    private static string Describe(TsvDiagnosis diagnosis) => $"line {diagnosis.Line.ToString(CultureInfo.InvariantCulture)}: {diagnosis.Detail}";

    private static void CheckInsidePackFolder(string relativeFile)
    {
        if (Path.IsPathRooted(relativeFile) || relativeFile.Split('\\', '/').Contains(".."))
        {
            throw new ArgumentException("The file must be a relative path below the pack folder.", nameof(relativeFile));
        }
    }

    // Second line of defence behind the segment check: whatever the path resolves to, it must lie inside the pack folder.
    private static void CheckResolvedInsidePackFolder(string packFolder, string fullPath, string relativeFile)
    {
        var root = Path.GetFullPath(packFolder);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        if (!Path.GetFullPath(fullPath).StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The file must be a relative path below the pack folder: " + relativeFile, nameof(relativeFile));
        }
    }

    private static bool TryDecodeStrict(byte[] bytes, out string text)
    {
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }
}
