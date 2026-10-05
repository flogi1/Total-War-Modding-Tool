using System.Globalization;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>Turns an opened entry into the tab that shows it, or into a hint tab that names the reason when it cannot be shown.</summary>
public static class TabFactory
{
    private static readonly string[] SizeUnits = ["KB", "MB", "GB"];

    public static TabViewModel Create(TabSource source, OpenedEntry entry, ResolvedSchema? schema, AppTexts texts, Action<TabViewModel> close,
        IClipboard? clipboard = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(close);

        if (entry.ReadProblem != EntryProblem.None)
        {
            var problem = entry.ReadProblem;
            return Info(t => t.TabInfoNotReadable, t => t.EntryProblemText(problem));
        }

        switch (entry.Kind)
        {
            case EntryKind.Table when entry.SchemaMissing:
                return Info(t => t.TabInfoNotDecodable, t => t.TabInfoNoSchema);
            case EntryKind.Table when entry.Table is not { Status: TableDecodeStatus.Ok }:
                var status = entry.Table?.Status ?? TableDecodeStatus.Failed;
                return Info(t => t.TabInfoNotDecodable, t => t.TableStatusText(status));
            case EntryKind.Table:
                return new TableTabViewModel(source, entry.EntryPath, texts, close, entry.Table!, clipboard);
            case EntryKind.Loc when entry.Loc is not { Status: LocDecodeStatus.Ok }:
                return Info(t => t.TabInfoLocNotDecodable, t => t.LocStatusFailed);
            case EntryKind.Loc:
                return new LocTabViewModel(source, entry.EntryPath, texts, close, entry.Loc!.Rows, clipboard);
            case EntryKind.Text when entry.Text is { } text:
                return new TextTabViewModel(source, entry.EntryPath, texts, close, text);
            default:
                return Info(t => t.TabInfoNotSupported, _ => string.Empty);
        }

        InfoTabViewModel Info(Func<AppTexts, string> heading, Func<AppTexts, string> reason) =>
            new(source, entry, schema, texts, close, heading, reason);
    }

    /// <summary>Binary units with one decimal from KB on, e.g. "512 B", "1.5 KB", "3.2 MB", "1.1 GB"; independent of the user's culture.</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(bytes, 0)} B");
        }

        // The unit is chosen after rounding to one decimal, so 1048575 bytes read "1.0 MB" and never "1024.0 KB".
        var value = bytes / 1024.0;
        var unit = 0;
        while (Math.Round(value, 1, MidpointRounding.AwayFromZero) >= 1024 && unit < SizeUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.0} {SizeUnits[unit]}");
    }

    // Only what is known, in the language of the texts, always in this order: type, table, version, sizes, compression, schema, technical detail.
    internal static List<InfoLine> InfoLines(OpenedEntry entry, ResolvedSchema? schema, AppTexts texts)
    {
        var lines = new List<InfoLine>();
        var isTable = entry.Kind == EntryKind.Table;
        if (isTable)
        {
            var tableName = entry.Table?.TableName;
            if (string.IsNullOrEmpty(tableName) && DbTablePath.TryGetTableName(entry.EntryPath, out var fromPath))
            {
                tableName = fromPath;
            }

            if (!string.IsNullOrEmpty(tableName))
            {
                lines.Add(new InfoLine(texts.TabInfoTable, tableName));
            }

            if (entry.Table is { } header)
            {
                lines.Add(new InfoLine(texts.TabInfoVersion, header.Version.ToString(CultureInfo.InvariantCulture)));
            }
        }
        else
        {
            var extension = System.IO.Path.GetExtension(entry.EntryPath).ToLowerInvariant();
            lines.Add(new InfoLine(texts.TabInfoType, extension.Length > 0 ? extension : texts.TabInfoTypeNone));
        }

        lines.Add(new InfoLine(texts.TabInfoStoredSize, FormatSize(entry.StoredSize)));
        if (entry.UncompressedSize != 0 || entry.ReadProblem == EntryProblem.None)
        {
            lines.Add(new InfoLine(texts.TabInfoSize, FormatSize(entry.UncompressedSize)));
        }

        lines.Add(new InfoLine(texts.TabInfoCompression, CompressionName(entry.Format, texts)));
        if (isTable)
        {
            lines.Add(new InfoLine(texts.TabInfoSchema, texts.SchemaSourceText(schema)));
        }

        var detail = FirstNonEmpty(entry.ReadDetail, entry.Table?.Detail, entry.Loc?.Detail);
        if (detail is not null)
        {
            lines.Add(new InfoLine(texts.TabInfoDetail, detail));
        }

        return lines;
    }

    // The names of the formats are the names of the formats, not UI texts.
    private static string CompressionName(CompressionFormat format, AppTexts texts) => format switch
    {
        CompressionFormat.None => texts.TabInfoCompressionNone,
        CompressionFormat.Zstd => "zstd",
        CompressionFormat.Lz4 => "LZ4",
        CompressionFormat.Lzma => "LZMA",
        _ => texts.TabInfoCompressionUnknown,
    };

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
