using System.Text;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Projects;

/// <summary>Bytes is the content of the file in the project. Note says why a table or loc file stayed binary (null otherwise).</summary>
public sealed record ProjectFileContent(ProjectFileKind Kind, byte[] Bytes, string? Note);

/// <summary>Bytes is the pack entry, or null with a problem. Line is 1-based, 0 when the problem has no line.</summary>
public sealed record PackEntryContent(byte[]? Bytes, int Line, string? Detail);

/// <summary>
/// Converts the bytes of a pack entry to the bytes of a project file and back. A table or loc entry becomes text only when the
/// text builds back to exactly the same bytes; otherwise it stays binary with a note. Never throws for the data.
/// </summary>
public static class EntryConversion
{
    private const string NoSchema = "no schema";
    private const string RoundTripDiffers = "round trip differs";
    private const string NotUtf8 = "the file is not valid UTF-8";
    private const string LocExtension = ".loc";
    private const int MaxNameLength = 50;

    /// <summary>Pack entry -> project file. Never throws for the data. <paramref name="packPath"/> must have passed <see cref="ProjectLayout.UnsafePathReason"/>.</summary>
    public static ProjectFileContent ToProjectFile(string packPath, byte[] entryBytes, GameSchema? schema)
    {
        ArgumentNullException.ThrowIfNull(packPath);
        ArgumentNullException.ThrowIfNull(entryBytes);

        if (DbTablePath.TryGetTableName(packPath, out var tableName))
        {
            return TableToProjectFile(packPath, tableName, entryBytes, schema);
        }

        if (packPath.EndsWith(LocExtension, StringComparison.OrdinalIgnoreCase))
        {
            return LocToProjectFile(packPath, entryBytes);
        }

        return Raw(entryBytes, null);
    }

    /// <summary>
    /// Project file in text form -> pack entry. <paramref name="kind"/> is Table or Loc. <paramref name="newGuid"/> gives the GUID text of a table header.
    /// The path in the meta line is not checked: the place of the file decides the pack path.
    /// </summary>
    public static PackEntryContent ToPackEntry(ProjectFileKind kind, string packPath, byte[] fileBytes, GameSchema? schema, Func<string> newGuid)
    {
        ArgumentNullException.ThrowIfNull(packPath);
        ArgumentNullException.ThrowIfNull(fileBytes);
        ArgumentNullException.ThrowIfNull(newGuid);
        if (kind == ProjectFileKind.Raw)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only a table or loc file has a text form.");
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(fileBytes);
        }
        catch (DecoderFallbackException)
        {
            return Problem(0, NotUtf8);
        }

        return kind == ProjectFileKind.Table
            ? TableToPackEntry(packPath, text, schema, newGuid)
            : LocToPackEntry(text);
    }

    private static ProjectFileContent TableToProjectFile(string packPath, string tableName, byte[] entryBytes, GameSchema? schema)
    {
        if (schema is null)
        {
            return Raw(entryBytes, NoSchema);
        }

        if (tableName.Contains(';'))
        {
            return Raw(entryBytes, "the table name contains ';'");
        }

        var decoded = DbTableDecoder.Decode(entryBytes, tableName, schema);
        if (decoded.Status != TableDecodeStatus.Ok || decoded.Definition is null)
        {
            return Raw(entryBytes, Describe(decoded.Status.ToString(), decoded.Detail));
        }

        string text;
        try
        {
            var written = TableTsv.Write(tableName, decoded.Version, packPath, decoded.Definition, decoded.Rows);
            if (written.Text is null)
            {
                return Raw(entryBytes, Describe("not written", written.Detail));
            }

            text = written.Text;
        }
        catch (ArgumentException ex)
        {
            return Raw(entryBytes, Describe("not written", ex.Message));
        }

        var read = TableTsv.Read(text, schema);
        if (read.Table is null)
        {
            return Raw(entryBytes, RoundTripDiffers);
        }

        byte[] rebuilt;
        try
        {
            rebuilt = DbTableEncoder.Encode(read.Table.Definition, read.Table.Version, DbTableDecoder.ReadGuid(entryBytes), read.Table.Rows);
        }
        catch (ArgumentException)
        {
            return Raw(entryBytes, RoundTripDiffers);
        }

        return rebuilt.AsSpan().SequenceEqual(entryBytes)
            ? new ProjectFileContent(ProjectFileKind.Table, Utf8(text), null)
            : Raw(entryBytes, RoundTripDiffers);
    }

    private static ProjectFileContent LocToProjectFile(string packPath, byte[] entryBytes)
    {
        var decoded = LocDecoder.Decode(entryBytes);
        if (decoded.Status != LocDecodeStatus.Ok)
        {
            return Raw(entryBytes, Describe(decoded.Status.ToString(), decoded.Detail));
        }

        string text;
        try
        {
            var written = TableTsv.WriteLoc(packPath, decoded.Rows);
            if (written.Text is null)
            {
                return Raw(entryBytes, Describe("not written", written.Detail));
            }

            text = written.Text;
        }
        catch (ArgumentException ex)
        {
            return Raw(entryBytes, Describe("not written", ex.Message));
        }

        var read = TableTsv.ReadLoc(text);
        if (read.Loc is null)
        {
            return Raw(entryBytes, RoundTripDiffers);
        }

        byte[] rebuilt;
        try
        {
            rebuilt = LocEncoder.Encode(read.Loc.Rows);
        }
        catch (ArgumentException)
        {
            return Raw(entryBytes, RoundTripDiffers);
        }

        return rebuilt.AsSpan().SequenceEqual(entryBytes)
            ? new ProjectFileContent(ProjectFileKind.Loc, Utf8(text), null)
            : Raw(entryBytes, RoundTripDiffers);
    }

    private static PackEntryContent TableToPackEntry(string packPath, string text, GameSchema? schema, Func<string> newGuid)
    {
        if (schema is null)
        {
            return Problem(0, NoSchema);
        }

        if (!DbTablePath.TryGetTableName(packPath, out var tableName))
        {
            throw new ArgumentException("The pack path is not the path of a table: " + packPath, nameof(packPath));
        }

        var read = TableTsv.Read(text, schema);
        if (read.Table is null)
        {
            var diagnosis = read.Problem!;
            return Problem(diagnosis.Line, diagnosis.Detail);
        }

        var table = read.Table;
        if (!string.Equals(table.TableName, tableName, StringComparison.OrdinalIgnoreCase))
        {
            return Problem(2, $"the meta line names the table '{Shorten(table.TableName)}' but the file belongs to '{Shorten(tableName)}'");
        }

        try
        {
            return new PackEntryContent(DbTableEncoder.Encode(table.Definition, table.Version, newGuid(), table.Rows), 0, null);
        }
        catch (ArgumentException ex)
        {
            return Problem(0, ex.Message);
        }
    }

    private static PackEntryContent LocToPackEntry(string text)
    {
        var read = TableTsv.ReadLoc(text);
        if (read.Loc is null)
        {
            var diagnosis = read.Problem!;
            return Problem(diagnosis.Line, diagnosis.Detail);
        }

        try
        {
            return new PackEntryContent(LocEncoder.Encode(read.Loc.Rows), 0, null);
        }
        catch (ArgumentException ex)
        {
            return Problem(0, ex.Message);
        }
    }

    private static ProjectFileContent Raw(byte[] bytes, string? note) => new(ProjectFileKind.Raw, bytes, note);

    private static PackEntryContent Problem(int line, string detail) => new(null, line, detail);

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    private static string Describe(string status, string? detail) => string.IsNullOrWhiteSpace(detail) ? status : status + ": " + detail;

    private static string Shorten(string name) => name.Length <= MaxNameLength ? name : name[..MaxNameLength] + "...";
}
