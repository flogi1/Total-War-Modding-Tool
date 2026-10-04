using TotalWarModTool.Projects;

namespace TotalWarModTool.App;

// Texts of the mod projects. The library returns enums; these texts turn them into words for the user, with a hint what to do.
// Every enum value has its own property so the locale check sees each key; ProjectProblemText only chooses among them.
public sealed partial class AppTexts
{
    public string ProjectProblemNotFound => T("project.problem.notFound",
        "The project file was not found in the folder. Check that the folder still exists, or remove the project from the list.");

    public string ProjectProblemUnreadable => T("project.problem.unreadable",
        "The project file cannot be read. It may be locked by another program. Close that program and try again.");

    public string ProjectProblemCorrupt => T("project.problem.corrupt",
        "The project file is damaged and cannot be read. Repair it by hand or restore it from a backup.");

    public string ProjectProblemNewerVersion => T("project.problem.newerVersion",
        "The project file was written by a newer version of this app. Update the app to use this project.");

    public string ProjectProblemInvalid => T("project.problem.invalid",
        "The project data is not valid. The technical detail says what to correct.");

    public string ProjectProblemFolderNotEmpty => T("project.problem.folderNotEmpty",
        "The folder is not empty. Choose an empty or a new folder for a new project.");

    public string ProjectProblemInvalidPackName => T("project.problem.invalidPackName",
        "The pack name cannot be used. Choose a name that ends in .pack and has no special characters.");

    public string ProjectProblemUnwritable => T("project.problem.unwritable",
        "The project folder cannot be written. Check the folder and your permissions, then try again.");

    public string ProjectOtherGame => T("project.otherGame", "This project belongs to another game and cannot be added here.");

    /// <param name="folder">Folder of the project that is in the list already. Data, not a UI text.</param>
    public string ProjectAlreadyInList(string folder) => F("project.alreadyInList", "This project is already in the list: {0}", folder);

    /// <param name="reason">Why the name cannot be used; English text of the library. Data, not a UI text.</param>
    public string ProjectPackNameInvalid(string reason) => F("project.packNameInvalid", "The pack name cannot be used: {0}", reason);

    public string ProjectFolderNotAbsolute => T("project.folderNotAbsolute", "Enter a full, valid folder path.");

    public string ProjectListNotSaved => T("project.listNotSaved",
        "The project list could not be saved. Check that the app can write its settings folder, or update the app if the list comes from a newer version.");

    public string ProjectUnknown => T("project.unknown", "This project is not in the list. Refresh the list and try again.");

    public string ProjectNameEmpty => T("project.nameEmpty", "Enter a name for the project.");

    public string ProjectNameInvalid => T("project.nameInvalid", "The project name contains characters that cannot be stored. Enter a different name.");

    public string ProjectPackPathNotAbsolute => T("project.packPathNotAbsolute", "Enter the full path of the pack file, including the drive letter.");

    public string ImportProblemSourceUnreadable => T("import.problem.sourceUnreadable",
        "The pack cannot be read. It may be damaged, encrypted or locked by another program.");

    public string ImportProblemInvalidPackName => T("import.problem.invalidPackName",
        "The pack name of the project cannot be used. Choose a name that ends in .pack, is not the name of a game pack and has no special characters.");

    public string ImportProblemTargetNotEmpty => T("import.problem.targetNotEmpty",
        "The project folder is not empty. Choose an empty or a new folder.");

    public string ImportProblemTargetUnwritable => T("import.problem.targetUnwritable",
        "The project folder cannot be written. Check the folder and your permissions, then try again.");

    public string InstallProblemInvalidPackName => T("install.problem.invalidPackName",
        "The pack name of the project cannot be used. Change it in the project and build again.");

    public string InstallProblemVanillaName => T("install.problem.vanillaName",
        "The pack name is the name of a game pack, which is never replaced. Change the pack name of the project.");

    public string InstallProblemNoBuiltPack => T("install.problem.noBuiltPack", "The built pack is missing. Build the project again.");

    public string InstallProblemDataFolderMissing => T("install.problem.dataFolderMissing",
        "The data folder of the game does not exist. Check the game folder in the settings.");

    public string InstallProblemForeignFile => T("install.problem.foreignFile",
        "A file with this name is in the data folder, but this tool did not install it, or it was changed since. It was left as it is. Rename or remove that file yourself, or change the pack name of the project.");

    public string InstallProblemTargetUnwritable => T("install.problem.targetUnwritable",
        "The pack cannot be written to the data folder. The game may be running. Close it and try again.");

    public string InstallProblemInstalledStateUnknown => T("install.problem.installedStateUnknown",
        "The pack was copied into the data folder, but its state could not be read afterwards. This tool will treat the file as a foreign file next time. Remove it by hand before installing again.");

    public string InstallNoGameFolder => T("install.noGameFolder", "The game folder was not found, so nothing can be installed. Choose the game folder in the settings.");

    public string UninstallNotInstalled => T("uninstall.notInstalled", "This tool has not installed a pack for this project, so there is nothing to remove.");

    /// <param name="tables">Number of tables written as text.</param>
    /// <param name="locFiles">Number of loc files written as text.</param>
    /// <param name="otherFiles">Number of files written as they are.</param>
    public string ImportSummary(int tables, int locFiles, int otherFiles) =>
        F("import.summary", "Imported {0} table(s), {1} loc file(s) and {2} other file(s).", tables, locFiles, otherFiles);

    public string ImportStayedBinary(int count) =>
        F("import.stayedBinary", "Tables or loc files that stay binary files in the project because they could not be converted to text: {0}.", count);

    public string ImportSkipped(int count) => F("import.skipped", "Entries of the pack that were left out because they cannot be stored safely: {0}.", count);

    public string ImportDroppedDependencies(int count) => F("import.droppedDependencies", "Dependency names of the pack that were not taken over because they are not valid: {0}.", count);

    public string ImportCancelled => T("import.cancelled", "The import was cancelled. Nothing was added to the list.");

    /// <param name="folder">Path of the unfinished staging folder. Data, not a UI text.</param>
    public string ImportLeftover(string folder) => F("import.leftover",
        "The import was cancelled. Nothing was added to the list, but the unfinished folder {0} is left over. You can delete it.", folder);

    /// <param name="packName">File name of the pack that was built. Data, not a UI text.</param>
    /// <param name="entries">Number of files in the pack.</param>
    public string BuildDone(string packName, int entries) => F("build.done", "Built {0} with {1} file(s).", packName, entries);

    /// <param name="packName">File name of the pack that was built and installed. Data, not a UI text.</param>
    public string BuildInstalled(string packName) => F("build.installed", "Built and installed {0}.", packName);

    public string BuildIgnored(int count) => F("build.ignored", "Files left out on purpose (hidden files, lock files and backups): {0}.", count);

    public string BuildWriteFailed => T("build.writeFailed", "The pack could not be written. The previous build is unchanged. Check the project folder and try again.");

    /// <param name="oldName">File name of the pack that was installed before. Data, not a UI text.</param>
    public string BuildOldPackNotRemoved(string oldName) => F("build.oldPackNotRemoved",
        "The pack name changed, but the old pack {0} could not be removed from the data folder. The new pack was built but not installed.", oldName);

    /// <param name="oldName">File name of the pack that was removed. Data, not a UI text.</param>
    /// <param name="newName">File name of the pack that was not installed. Data, not a UI text.</param>
    public string BuildOldRemovedNewNotInstalled(string oldName, string newName) => F("build.oldRemovedNewNotInstalled",
        "The old pack {0} was removed from the data folder, but the new pack {1} was not installed. Build and install again to get a pack into the game.",
        oldName, newName);

    /// <param name="path">Path of the unfinished file in the data folder. Data, not a UI text.</param>
    public string InstallLeftover(string path) => F("install.leftover",
        "The install was cancelled. The unfinished file {0} is left over in the data folder. You can delete it.", path);

    /// <param name="name">Name of the pack or project. Data, not a UI text.</param>
    public string OperationReadPack(string name) => F("operation.readPack", "Read {0}", name);

    /// <param name="name">File name of the pack that is imported. Data, not a UI text.</param>
    public string OperationImportPack(string name) => F("operation.importPack", "Import {0}", name);

    /// <param name="projectName">Name of the project. Data, not a UI text.</param>
    public string OperationBuild(string projectName) => F("operation.build", "Build {0}", projectName);

    /// <param name="projectName">Name of the project. Data, not a UI text.</param>
    public string OperationBuildAndInstall(string projectName) => F("operation.buildAndInstall", "Build and install {0}", projectName);

    /// <param name="projectName">Name of the project. Data, not a UI text.</param>
    public string OperationUninstall(string projectName) => F("operation.uninstall", "Uninstall {0}", projectName);

    /// <param name="projectName">Name of the project. Data, not a UI text.</param>
    public string OperationOpenProject(string projectName) => F("operation.openProject", "Open {0}", projectName);

    /// <param name="fileName">File name. Data, not a UI text.</param>
    public string OperationOpenFile(string fileName) => F("operation.openFile", "Open {0}", fileName);

    // ---- project commands and dialogs --------------------------------------------------------------------------------------------

    public string ProjectNewTitle => T("project.newTitle", "New project");

    public string ProjectOpenTitle => T("project.openTitle", "Choose the project folder");

    public string ProjectMessageTitle => T("project.messageTitle", "Project");

    public string ProjectNoneActive => T("project.noneActive", "No project is active. Choose a project in the explorer first.");

    /// <param name="name">Name of the project. Data, not a UI text.</param>
    public string ProjectCreated(string name) => F("project.created", "Project {0} created.", name);

    /// <param name="name">Name of the project. Data, not a UI text.</param>
    public string ProjectAdded(string name) => F("project.added", "Project {0} added to the list.", name);

    /// <param name="sourceName">Name of the pack that is imported. Data, not a UI text.</param>
    public string ImportTitle(string sourceName) => F("import.title", "Import {0}", sourceName);

    public string ImportPickTitle => T("import.pickTitle", "Choose the pack to import");

    public string ImportMessageTitle => T("import.messageTitle", "Import");

    public string ImportPreviewLineTables(int count) => F("import.preview.tables", "Tables: {0}", count);

    public string ImportPreviewLineLoc(int count) => F("import.preview.loc", "Loc files: {0}", count);

    public string ImportPreviewLineOther(int count) => F("import.preview.other", "Other files: {0}", count);

    /// <param name="size">The size as text, e.g. "1.5 MB". Data, not a UI text.</param>
    public string ImportPreviewLineSize(string size) => F("import.preview.size", "Size in the pack: {0}", size);

    public string ImportPreviewLineSkipped(int count) => F("import.preview.skipped", "Entries that will be left out: {0}", count);

    public string ImportReportTitle => T("import.report.title", "Import report");

    public string ImportReportBinaryHeading => T("import.report.binaryHeading", "Kept as binary files");

    public string ImportReportSkippedHeading => T("import.report.skippedHeading", "Left out");

    public string ImportReportDroppedHeading => T("import.report.droppedHeading", "Dependency names not taken over");

    public string BuildMessageTitle => T("build.messageTitle", "Build");

    public string BuildReportTitle => T("build.report.title", "Build report");

    public string BuildProblemsHeading => T("build.problems.heading", "Problems");

    public string BuildIgnoredHeading => T("build.ignored.heading", "Files left out on purpose");

    public string BuildProblemsSummary(int count) => F("build.problems.summary", "The pack was not built. Problems found: {0}.", count);

    public string UninstallMessageTitle => T("uninstall.messageTitle", "Uninstall");

    public string UninstallNothing => T("uninstall.nothing", "Nothing is installed for this project.");

    /// <param name="name">Name of the project. Data, not a UI text.</param>
    public string UninstallDone(string name) => F("uninstall.done", "Removed the installed pack of {0}.", name);

    public string DialogName => T("dialog.name", "Name");

    public string DialogFolder => T("dialog.folder", "Folder");

    public string DialogPackName => T("dialog.packName", "Pack name");

    public string DialogOk => T("dialog.ok", "OK");

    public string DialogCancel => T("dialog.cancel", "Cancel");

    public string DialogBrowse => T("dialog.browse", "Browse");

    public string DialogClose => T("dialog.close", "Close");

    public string DialogChooseFolder => T("dialog.chooseFolder", "Choose the folder of the project");

    public string DialogPackFilter => T("dialog.packFilter", "Pack files (*.pack)");

    /// <summary>The text for an import problem; empty for <see cref="ImportProblem.None"/>.</summary>
    public string ImportProblemText(ImportProblem problem) => problem switch
    {
        ImportProblem.SourceUnreadable => ImportProblemSourceUnreadable,
        ImportProblem.InvalidPackName => ImportProblemInvalidPackName,
        ImportProblem.TargetNotEmpty => ImportProblemTargetNotEmpty,
        ImportProblem.TargetUnwritable => ImportProblemTargetUnwritable,
        _ => string.Empty,
    };

    /// <summary>The text for an install problem; empty for <see cref="InstallProblem.None"/>.</summary>
    public string InstallProblemText(InstallProblem problem) => problem switch
    {
        InstallProblem.InvalidPackName => InstallProblemInvalidPackName,
        InstallProblem.VanillaName => InstallProblemVanillaName,
        InstallProblem.NoBuiltPack => InstallProblemNoBuiltPack,
        InstallProblem.DataFolderMissing => InstallProblemDataFolderMissing,
        InstallProblem.ForeignFile => InstallProblemForeignFile,
        InstallProblem.TargetUnwritable => InstallProblemTargetUnwritable,
        InstallProblem.InstalledStateUnknown => InstallProblemInstalledStateUnknown,
        _ => string.Empty,
    };

    /// <summary>The text for a project problem; empty for <see cref="ProjectProblem.None"/>.</summary>
    public string ProjectProblemText(ProjectProblem problem) => problem switch
    {
        ProjectProblem.NotFound => ProjectProblemNotFound,
        ProjectProblem.Unreadable => ProjectProblemUnreadable,
        ProjectProblem.Corrupt => ProjectProblemCorrupt,
        ProjectProblem.NewerVersion => ProjectProblemNewerVersion,
        ProjectProblem.Invalid => ProjectProblemInvalid,
        ProjectProblem.FolderNotEmpty => ProjectProblemFolderNotEmpty,
        ProjectProblem.InvalidPackName => ProjectProblemInvalidPackName,
        ProjectProblem.Unwritable => ProjectProblemUnwritable,
        _ => string.Empty,
    };
}
