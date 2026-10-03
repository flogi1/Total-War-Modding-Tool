using System.Globalization;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

// Diagnosis texts of the explorer and the tabs. Libraries return enums; these texts turn them into words for the user.
// Every enum value has its own property so the locale check sees each key; the methods with an enum parameter only choose among them.
public sealed partial class AppTexts
{
    public string PackProblemNotFound => T("pack.problem.notFound", "The pack file no longer exists. Refresh the pack list.");

    public string PackProblemUnreadable => T("pack.problem.unreadable",
        "The pack file cannot be read. It may be locked by the game or another program. Close that program and try again.");

    public string PackProblemTruncated => T("pack.problem.truncated", "The pack file is incomplete.");

    /// <param name="expectedFormat">The pack format of the game's profile, e.g. "PFH5".</param>
    public string PackProblemUnknownFormat(string expectedFormat) =>
        F("pack.problem.unknownFormat", "Unknown pack format. This game uses {0}.", expectedFormat);

    public string PackProblemEncryptedIndex => T("pack.problem.encryptedIndex", "Encrypted pack, not supported.");

    public string PackProblemInconsistentIndex => T("pack.problem.inconsistentIndex", "The file list of the pack is damaged.");

    public string EntryProblemPackChanged => T("entry.problem.packChanged", "The pack was changed or removed on disk. Refresh the pack list and open the entry again.");

    public string EntryProblemUnreadable => T("entry.problem.unreadable",
        "The entry cannot be read. The pack may be locked by the game or another program.");

    public string EntryProblemEncrypted => T("entry.problem.encrypted", "Encrypted pack, not supported.");

    public string EntryProblemUnsupportedCompression => T("entry.problem.unsupportedCompression", "This compression format is not supported yet.");

    public string EntryProblemCorrupt => T("entry.problem.corrupt", "The entry is damaged.");

    public string EntryProblemTooLarge => T("entry.problem.tooLarge", "The entry is too large to open.");

    public string TableStatusTableNotInSchema => T("table.status.tableNotInSchema", "The schema does not know this table.");

    public string TableStatusVersionNotInSchema => T("table.status.versionNotInSchema", "The schema does not know this version of the table.");

    public string TableStatusUnsupportedFieldType => T("table.status.unsupportedFieldType",
        "The table uses a field type that is not supported yet.");

    public string TableStatusFailed => T("table.status.failed", "The table does not match the schema. It may come from another game version.");

    public string SchemaProblemNotFound => T("schema.problem.notFound", "file not found");

    public string SchemaProblemUnreadable => T("schema.problem.unreadable", "file cannot be read");

    public string SchemaProblemInvalidSyntax => T("schema.problem.invalidSyntax", "file is not valid RON");

    public string SchemaProblemInvalidStructure => T("schema.problem.invalidStructure", "file has an unexpected structure");

    public string OriginVanilla => T("origin.vanilla", "Game");

    public string OriginDataFolderMod => T("origin.dataFolderMod", "Mods in the data folder");

    public string OriginWorkshop => T("origin.workshop", "Workshop");

    public string OperationRefreshPacks => T("operation.refreshPacks", "Refresh pack list");

    public string OperationLoadSchema => T("operation.loadSchema", "Load schema");

    /// <param name="packName">File name of the pack. Data, not a UI text.</param>
    public string OperationOpenPack(string packName) => F("operation.openPack", "Open {0}", packName);

    /// <param name="entryName">File name of the entry. Data, not a UI text.</param>
    public string OperationOpenEntry(string entryName) => F("operation.openEntry", "Open {0}", entryName);

    public string ExplorerLoading => T("explorer.loading", "Loading the pack list");

    public string ExplorerNoProfile => T("explorer.noProfile", "No game profile was found. The program folder is incomplete.");

    public string ExplorerGameNotFound => T("explorer.gameNotFound", "The game was not found. Choose the game folder in the settings.");

    public string ExplorerManualPathInvalid => T("explorer.manualPathInvalid",
        "The game folder from the settings has no data folder. Choose the game folder in the settings.");

    public string ExplorerNoPacks => T("explorer.noPacks", "No packs were found in the game folder.");

    public string ExplorerOpenSettings => T("explorer.openSettings", "Open settings");

    public string ExplorerEncrypted => T("explorer.encrypted", "Encrypted pack, not supported.");

    /// <param name="originName">Name of the pack origin, see <see cref="OriginName"/>.</param>
    /// <param name="count">Number of packs in the group.</param>
    public string ExplorerGroup(string originName, int count) => F("explorer.group", "{0} ({1})", originName, count);

    /// <summary>The text for a pack problem; empty for <see cref="PackProblem.None"/>.</summary>
    /// <param name="expectedFormat">The pack format of the game's profile; names the format in the unknown-format text.</param>
    public string PackProblemText(PackProblem problem, string? expectedFormat) => problem switch
    {
        PackProblem.NotFound => PackProblemNotFound,
        PackProblem.Unreadable => PackProblemUnreadable,
        PackProblem.Truncated => PackProblemTruncated,
        PackProblem.UnknownFormat => PackProblemUnknownFormat(expectedFormat ?? "?"),
        PackProblem.EncryptedIndex => PackProblemEncryptedIndex,
        PackProblem.InconsistentIndex => PackProblemInconsistentIndex,
        _ => string.Empty,
    };

    /// <summary>The text for an entry problem; empty for <see cref="EntryProblem.None"/>.</summary>
    public string EntryProblemText(EntryProblem problem) => problem switch
    {
        EntryProblem.PackChanged => EntryProblemPackChanged,
        EntryProblem.Unreadable => EntryProblemUnreadable,
        EntryProblem.Encrypted => EntryProblemEncrypted,
        EntryProblem.UnsupportedCompression => EntryProblemUnsupportedCompression,
        EntryProblem.Corrupt => EntryProblemCorrupt,
        EntryProblem.TooLarge => EntryProblemTooLarge,
        _ => string.Empty,
    };

    /// <summary>The text for a table decode status; empty for <see cref="TableDecodeStatus.Ok"/>.</summary>
    public string TableStatusText(TableDecodeStatus status) => status switch
    {
        TableDecodeStatus.TableNotInSchema => TableStatusTableNotInSchema,
        TableDecodeStatus.VersionNotInSchema => TableStatusVersionNotInSchema,
        TableDecodeStatus.UnsupportedFieldType => TableStatusUnsupportedFieldType,
        TableDecodeStatus.Failed => TableStatusFailed,
        _ => string.Empty,
    };

    /// <summary>The text for a schema problem; empty for <see cref="SchemaProblem.None"/>.</summary>
    public string SchemaProblemText(SchemaProblem problem) => problem switch
    {
        SchemaProblem.NotFound => SchemaProblemNotFound,
        SchemaProblem.Unreadable => SchemaProblemUnreadable,
        SchemaProblem.InvalidSyntax => SchemaProblemInvalidSyntax,
        SchemaProblem.InvalidStructure => SchemaProblemInvalidStructure,
        _ => string.Empty,
    };

    public string TabClose => T("tab.close", "Close");

    public string TabStale => T("tab.stale", "The pack was changed on disk. This tab shows the old content.");

    /// <param name="tableName">Name of the table. Data, not a UI text.</param>
    /// <param name="version">Version of the table.</param>
    /// <param name="rowCount">Number of rows.</param>
    public string TabTableHeader(string tableName, int version, int rowCount) =>
        F("tab.table.header", "{0}, version {1}, {2} row(s)", tableName, version, rowCount);

    public string TabLocHeader(int rowCount) => F("tab.loc.header", "{0} row(s)", rowCount);

    public string TabLocKey => T("tab.loc.key", "Key");

    public string TabLocText => T("tab.loc.text", "Text");

    public string TabLocTooltip => T("tab.loc.tooltip", "Tooltip");

    /// <param name="encodingName">Name of the text encoding. Data, not a UI text.</param>
    /// <param name="size">Size of the text, e.g. "2.0 KB".</param>
    public string TabTextHeader(string encodingName, string size) => F("tab.text.header", "{0}, {1}", encodingName, size);

    /// <param name="shown">Size of the part that is shown, e.g. "5.0 MB".</param>
    /// <param name="total">Size of the whole text.</param>
    public string TabTextTruncated(string shown, string total) => F("tab.text.truncated", "Only the first {0} of {1} are shown.", shown, total);

    public string TabInfoNotDecodable => T("tab.info.notDecodable", "This table cannot be decoded");

    public string TabInfoNotSupported => T("tab.info.notSupported", "This file type is not supported");

    public string TabInfoNotReadable => T("tab.info.notReadable", "This entry cannot be opened");

    public string TabInfoNoSchema => T("tab.info.noSchema",
        "No schema is loaded, so tables cannot be decoded. Check the schema folder in the settings.");

    public string TabInfoType => T("tab.info.type", "Type");

    public string TabInfoTypeNone => T("tab.info.type.none", "(no extension)");

    public string TabInfoStoredSize => T("tab.info.storedSize", "Size in the pack");

    public string TabInfoSize => T("tab.info.size", "Size");

    public string TabInfoCompression => T("tab.info.compression", "Compression");

    public string TabInfoCompressionNone => T("tab.info.compression.none", "none");

    public string TabInfoCompressionUnknown => T("tab.info.compression.unknown", "unknown");

    public string TabInfoTable => T("tab.info.table", "Table");

    public string TabInfoVersion => T("tab.info.version", "Version");

    public string TabInfoSchema => T("tab.info.schema", "Schema");

    public string TabInfoDetail => T("tab.info.detail", "Technical detail");

    /// <param name="date">Date of the schema file, <c>yyyy-MM-dd</c>.</param>
    public string SchemaSourceShipped(string date) => F("schema.source.shipped", "shipped with the app, {0}", date);

    /// <param name="date">Date of the schema file, <c>yyyy-MM-dd</c>.</param>
    public string SchemaSourceOverride(string date) => F("schema.source.override", "from the schema folder, {0}", date);

    public string SchemaSourceNone => T("schema.source.none", "none");

    /// <summary>Where the schema comes from, with the date of its file in local time; "none" without a schema.</summary>
    public string SchemaSourceText(ResolvedSchema? schema)
    {
        var date = schema?.FileDateUtc?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        return schema?.Origin switch
        {
            SchemaOrigin.Shipped => SchemaSourceShipped(date),
            SchemaOrigin.Override => SchemaSourceOverride(date),
            _ => SchemaSourceNone,
        };
    }

    public string OriginName(PackOrigin origin) => origin switch
    {
        PackOrigin.Vanilla => OriginVanilla,
        PackOrigin.DataFolderMod => OriginDataFolderMod,
        PackOrigin.Workshop => OriginWorkshop,
        _ => origin.ToString(),
    };
}
