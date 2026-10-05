using System.Globalization;
using System.IO;
using System.Windows.Input;
using TotalWarModTool.Projects;

namespace TotalWarModTool.App;

/// <summary>
/// The project commands (new, open, import, build, install, uninstall, set active, remove). It asks the user only through
/// <see cref="IProjectDialogs"/> and passes the work to the <see cref="ProjectsController"/>. Every command first clears
/// <see cref="StatusMessage"/> and sets it again when it ends with something to say. A command that could not start (another operation
/// runs, the run was cancelled) says nothing.
/// </summary>
public sealed class ProjectsViewModel : ObservableObject
{
    private const int MaxNameChars = 100;

    // A section of a report shows this many lines; the rest is one line that says how many are missing.
    private const int MaxReportLines = 1000;

    private readonly ProjectsController _projects;
    private readonly IProjectDialogs _dialogs;
    private readonly AppTexts _texts;
    private readonly RelayCommand _newProject;
    private readonly RelayCommand _openProject;
    private readonly RelayCommand _importPack;
    private readonly RelayCommand _buildActive;
    private readonly RelayCommand _buildAndInstallActive;
    private string? _statusMessage;

    public ProjectsViewModel(ProjectsController projects, IProjectDialogs dialogs, AppTexts texts)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(texts);

        _projects = projects;
        _dialogs = dialogs;
        _texts = texts;
        _newProject = new RelayCommand(NewProject, () => _projects.IsAvailable);
        _openProject = new RelayCommand(OpenProject, () => _projects.IsAvailable);
        _importPack = new RelayCommand(async () => await RunAsync(ImportFromPickedPackAsync), () => _projects.IsAvailable && _projects.CanStartOperation);
        _buildActive = new RelayCommand(async () => await BuildActiveAsync(install: false), () => CanRun(BuildDisabledReason));
        _buildAndInstallActive = new RelayCommand(async () => await BuildActiveAsync(install: true), () => CanRun(InstallDisabledReason));
        projects.Changed += (_, _) => RaiseCanExecuteChanged();
        projects.OperationStateChanged += (_, _) => RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Set by the composition root: the text that says why the project cannot leave the list because it has unsaved changes (null when it
    /// can). Added to <see cref="RemoveDisabledReason"/>; <see cref="Remove"/> refuses as well.
    /// </summary>
    public Func<Guid, string?>? UnsavedReason { get; set; }

    /// <summary>
    /// Set by the composition root: awaited at the start of a build for the project. False stops the build without a message (the
    /// question already told the user).
    /// </summary>
    public Func<Guid, Task<bool>>? BeforeBuild { get; set; }

    public ICommand NewProjectCommand => _newProject;

    public ICommand OpenProjectCommand => _openProject;

    /// <summary>Asks for a pack file and imports it into a new project. Disabled without a game profile or while an operation runs.</summary>
    public ICommand ImportPackCommand => _importPack;

    /// <summary>Builds the active project. Disabled with <see cref="BuildDisabledReason"/> or while an operation runs.</summary>
    public ICommand BuildActiveCommand => _buildActive;

    /// <summary>Builds the active project and installs the pack. Disabled with <see cref="InstallDisabledReason"/> or while an operation runs.</summary>
    public ICommand BuildAndInstallActiveCommand => _buildAndInstallActive;

    /// <summary>The text that says why the new, open and import commands are disabled (no game profile); null when projects are available.</summary>
    public string? ProjectsDisabledReason => _projects.IsAvailable ? null : _texts.ProjectNoGameProfile;

    /// <summary>The result of the last command, for the status bar; null when it had nothing to say. It can have several lines.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// Reads the pack, asks what to import it as and imports it. <paramref name="sourceName"/> is the name shown in the dialog. Without a
    /// game profile nothing happens but the status message.
    /// </summary>
    public Task ImportAsync(string sourcePack, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(sourcePack);
        ArgumentNullException.ThrowIfNull(sourceName);
        return RunAsync(() => ImportCoreAsync(sourcePack, sourceName));
    }

    /// <summary>Builds the project, with <paramref name="install"/> also installs the pack. Problems of the project open a report.</summary>
    public Task BuildAsync(Guid id, bool install) => RunAsync(() => BuildCoreAsync(id, install));

    /// <summary>Removes the pack that the tool installed for the project from the data folder.</summary>
    public Task UninstallAsync(Guid id) => RunAsync(() => UninstallCoreAsync(id));

    public void SetActive(Guid id)
    {
        StatusMessage = null;
        Complete(_projects.SetActive(id), _texts.ProjectMessageTitle, null);
    }

    /// <summary>
    /// Takes the project out of the list without asking: the folder stays, and "Open project" brings it back. Refused with the text of
    /// <see cref="RemoveDisabledReason"/> while there is one (an unknown project, an installed pack, unsaved changes).
    /// </summary>
    public void Remove(Guid id)
    {
        StatusMessage = null;
        if (RemoveDisabledReason(id) is { } reason)
        {
            _dialogs.ShowMessage(_texts.ProjectMessageTitle, reason);
            return;
        }

        Complete(_projects.Remove(id), _texts.ProjectMessageTitle, null);
    }

    /// <summary>The text that says why the project cannot be built; null when it can. A running operation gives no text.</summary>
    public string? BuildDisabledReason(Guid? id)
    {
        if (id is not { } value)
        {
            return _texts.ProjectNoneActive;
        }

        return StateOf(value) switch
        {
            null => _texts.ProjectUnknown,
            { IsUsable: false } state => _texts.ProjectProblemText(state.Problem),
            _ => _projects.BuildDisabledReason,
        };
    }

    /// <summary>The text that says why the project cannot be built and installed; null when it can.</summary>
    public string? InstallDisabledReason(Guid? id) => BuildDisabledReason(id) ?? _projects.InstallDisabledReason;

    /// <summary>
    /// The text that says why the installed pack of the project cannot be removed; null when it can. The project file need not be usable:
    /// the pack is removed by the record of the list.
    /// </summary>
    public string? UninstallDisabledReason(Guid id)
    {
        if (StateOf(id) is not { } state)
        {
            return _texts.ProjectUnknown;
        }

        return _projects.InstallDisabledReason ?? (state.Installed is null ? _texts.UninstallNothing : null);
    }

    /// <summary>
    /// The text that says why the project cannot be removed from the list (unknown, a pack the tool installed for it is still recorded:
    /// uninstall first, or <see cref="UnsavedReason"/>: it has unsaved changes); null when it can.
    /// </summary>
    public string? RemoveDisabledReason(Guid id) => StateOf(id) switch
    {
        null => _texts.ProjectUnknown,
        { Installed: not null } => _texts.ProjectRemoveInstalled,
        _ => UnsavedReason?.Invoke(id),
    };

    // ---- commands ----------------------------------------------------------------------------------------------------------------

    // Asks again with the last input as long as the controller refuses it, until the project is created or the user cancels.
    private void NewProject()
    {
        StatusMessage = null;
        if (!_projects.IsAvailable)
        {
            return;
        }

        var defaults = new ProjectInput(string.Empty, string.Empty, string.Empty);
        while (_dialogs.AskProject(_texts.ProjectNewTitle, defaults, [], Validate) is { } input)
        {
            var result = _projects.Create(input.Folder, input.Name, input.PackName);
            if (result.Ok)
            {
                Complete(result, _texts.ProjectMessageTitle, _texts.ProjectCreated(Safe(input.Name, MaxNameChars)));
                return;
            }

            _dialogs.ShowMessage(_texts.ProjectMessageTitle, result.Message ?? string.Empty);
            defaults = input;
        }
    }

    private void OpenProject()
    {
        StatusMessage = null;
        if (!_projects.IsAvailable || _dialogs.PickFolder(_texts.ProjectOpenTitle) is not { } folder)
        {
            return;
        }

        var result = _projects.AddExisting(folder);

        // The controller appends the project to the list, so it is the last one.
        Complete(result, _texts.ProjectMessageTitle,
            result.Ok && _projects.Projects.Count > 0 ? _texts.ProjectAdded(Safe(_projects.Projects[^1].DisplayName, MaxNameChars)) : null);
    }

    private async Task ImportFromPickedPackAsync()
    {
        if (_projects.IsAvailable && _dialogs.PickPackFile() is { } pack)
        {
            await ImportCoreAsync(pack, Path.GetFileName(pack));
        }
    }

    private async Task ImportCoreAsync(string sourcePack, string sourceName)
    {
        if (ProjectsDisabledReason is { } disabled)
        {
            StatusMessage = disabled;
            return;
        }

        var read = await _projects.PreviewImportAsync(sourcePack);
        if (read is null)
        {
            return;
        }

        if (read.Preview is not { } preview)
        {
            _dialogs.ShowMessage(_texts.ImportMessageTitle, read.Message ?? string.Empty);
            return;
        }

        var defaults = new ProjectInput(Path.GetFileNameWithoutExtension(sourcePack), string.Empty, Path.GetFileName(sourcePack));
        var info = new[]
        {
            _texts.ImportPreviewLineTables(preview.Tables),
            _texts.ImportPreviewLineLoc(preview.LocFiles),
            _texts.ImportPreviewLineOther(preview.OtherFiles),
            _texts.ImportPreviewLineSize(TabFactory.FormatSize(preview.StoredBytes)),
            _texts.ImportPreviewLineSkipped(preview.Skipped.Count),
        };

        // Asks again with the last input after a refusal. A run without a report was cancelled (or refused before it started, which the
        // validation of the dialog rules out): that ends the command.
        while (_dialogs.AskProject(_texts.ImportTitle(Safe(sourceName, MaxNameChars)), defaults, info, Validate) is { } input)
        {
            var outcome = await _projects.ImportAsync(sourcePack, input.Folder, input.Name, input.PackName);
            if (outcome is null)
            {
                return;
            }

            if (!outcome.Ok)
            {
                _dialogs.ShowMessage(_texts.ImportMessageTitle, outcome.Message ?? string.Empty);
                if (outcome.Report is null)
                {
                    return;
                }

                defaults = input;
                continue;
            }

            StatusMessage = outcome.Message;
            if (outcome.Report is { } report && ImportSections(report) is { Count: > 0 } sections)
            {
                _dialogs.ShowReport(new Report(_texts.ImportReportTitle, outcome.Message ?? string.Empty, sections));
            }

            return;
        }
    }

    private async Task BuildActiveAsync(bool install)
    {
        if (_projects.ActiveId is { } id)
        {
            await BuildAsync(id, install);
        }
    }

    private async Task BuildCoreAsync(Guid id, bool install)
    {
        // Unsaved changes are not part of a build: the hook asks and saves. No (or a save that failed) stops here, the hook told the user.
        if (BeforeBuild is { } before && !await before(id))
        {
            return;
        }

        var report = await _projects.BuildAsync(id, install);
        if (report is null)
        {
            return;
        }

        if (report.Problems.Count > 0)
        {
            _dialogs.ShowReport(BuildProblemReport(report));
            return;
        }

        if (!report.Built)
        {
            _dialogs.ShowMessage(_texts.BuildMessageTitle, report.Message ?? string.Empty);
            return;
        }

        StatusMessage = report.Message;

        // The pack is built, but the install did not happen, or the data folder changed and the list could not be saved: a status bar
        // line is too easy to miss for that.
        var notInstalled = install && !report.Installed ? report.Message : null;
        var dialog = string.Join("\n\n", new[] { notInstalled, report.Warning }.Where(part => !string.IsNullOrEmpty(part)));
        if (dialog.Length > 0)
        {
            _dialogs.ShowMessage(_texts.BuildMessageTitle, dialog);
        }
    }

    private async Task UninstallCoreAsync(Guid id)
    {
        var name = StateOf(id)?.DisplayName ?? string.Empty;
        var result = await _projects.UninstallAsync(id);
        if (result is null)
        {
            return;
        }

        Complete(result, _texts.UninstallMessageTitle, _texts.UninstallDone(Safe(name, MaxNameChars)));

        // The pack is gone, but the list still records it: the user must not miss that.
        if (result.Warning is { } warning)
        {
            _dialogs.ShowMessage(_texts.UninstallMessageTitle, warning);
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------

    // The one place where foreign text becomes display text: escaped, cut, and marked with the marker of the current language.
    private string Safe(string text, int maxChars = 300) => DisplayText.Safe(text, maxChars, " (" + _texts.TextShortened + ")");

    // Clears the status, runs the command and asks the commands again whether they may run: a command changes what is possible.
    private async Task RunAsync(Func<Task> command)
    {
        StatusMessage = null;
        try
        {
            await command();
        }
        finally
        {
            RaiseCanExecuteChanged();
        }
    }

    // A failure is shown as a message; a success becomes the status, with the note of the controller (e.g. the list could not be saved) after it.
    private void Complete(ProjectActionResult result, string messageTitle, string? status)
    {
        if (!result.Ok)
        {
            _dialogs.ShowMessage(messageTitle, result.Message ?? string.Empty);
            return;
        }

        var lines = new[] { status, result.Message }.Where(line => !string.IsNullOrEmpty(line)).ToList();
        StatusMessage = lines.Count == 0 ? null : string.Join("\n", lines);
    }

    // What the controller would refuse is answered here, so that a refusal after the dialog is rare and the user does not lose the input.
    private string? Validate(ProjectInput input)
    {
        if (_projects.CheckName(input.Name) is { } nameText)
        {
            return nameText;
        }

        if (ProjectListEdits.NormalizePath(input.Folder) is not { } folder)
        {
            return _texts.ProjectFolderNotAbsolute;
        }

        var listed = _projects.Projects.FirstOrDefault(state =>
            string.Equals(Path.TrimEndingDirectorySeparator(state.Folder), folder, StringComparison.OrdinalIgnoreCase));
        if (listed is not null)
        {
            return _texts.ProjectAlreadyInList(Safe(listed.Folder));
        }

        return _projects.CheckPackName(input.PackName);
    }

    private List<ReportSection> ImportSections(ImportReport report)
    {
        var sections = new List<ReportSection>();
        AddSection(sections, _texts.ImportReportBinaryHeading, report.StayedBinary.Select(entry => Entry(entry.PackPath, entry.Reason)));
        AddSection(sections, _texts.ImportReportSkippedHeading, report.Skipped.Select(entry => Entry(entry.PackPath, entry.Reason)));
        AddSection(sections, _texts.ImportReportDroppedHeading, report.DroppedDependencies.Select(name => Safe(name)));
        return sections;

        string Entry(string path, string reason) => Safe(path) + ": " + Safe(reason);
    }

    private Report BuildProblemReport(BuildReport report)
    {
        var sections = new List<ReportSection>();
        AddSection(sections, _texts.BuildProblemsHeading, report.Problems.Select(problem => problem.Line > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Safe(problem.RelativeFile)}({problem.Line}): {Safe(problem.Detail)}")
            : $"{Safe(problem.RelativeFile)}: {Safe(problem.Detail)}"));
        AddSection(sections, _texts.BuildIgnoredHeading, report.Ignored.Select(file => Safe(file)));
        return new Report(_texts.BuildReportTitle, _texts.BuildProblemsSummary(report.Problems.Count), sections);
    }

    // A section with no lines is left out; one with too many shows the first lines and how many more there are.
    private void AddSection(List<ReportSection> sections, string heading, IEnumerable<string> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0)
        {
            return;
        }

        if (list.Count > MaxReportLines)
        {
            var more = list.Count - MaxReportLines;
            list.RemoveRange(MaxReportLines, more);
            list.Add(_texts.ReportMoreLines(more));
        }

        sections.Add(new ReportSection(heading, list));
    }

    private ProjectState? StateOf(Guid id) => _projects.Projects.FirstOrDefault(state => state.Id == id);

    // True when the active project can be handled and no other operation runs. A running operation gives no reason text on purpose.
    private bool CanRun(Func<Guid?, string?> disabledReason) => disabledReason(_projects.ActiveId) is null && _projects.CanStartOperation;

    private void RaiseCanExecuteChanged()
    {
        _newProject.RaiseCanExecuteChanged();
        _openProject.RaiseCanExecuteChanged();
        _importPack.RaiseCanExecuteChanged();
        _buildActive.RaiseCanExecuteChanged();
        _buildAndInstallActive.RaiseCanExecuteChanged();
    }
}
