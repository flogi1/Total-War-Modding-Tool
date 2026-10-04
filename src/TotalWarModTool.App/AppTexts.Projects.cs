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

    public string ProjectFolderNotAbsolute => T("project.folderNotAbsolute", "Enter the full path of the folder, including the drive letter.");

    public string ProjectListNotSaved => T("project.listNotSaved",
        "The project list could not be saved. The change stays in effect until the app is closed.");

    public string ProjectUnknown => T("project.unknown", "This project is not in the list. Refresh the list and try again.");

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
