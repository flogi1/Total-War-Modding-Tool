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

    public string OriginName(PackOrigin origin) => origin switch
    {
        PackOrigin.Vanilla => OriginVanilla,
        PackOrigin.DataFolderMod => OriginDataFolderMod,
        PackOrigin.Workshop => OriginWorkshop,
        _ => origin.ToString(),
    };
}
