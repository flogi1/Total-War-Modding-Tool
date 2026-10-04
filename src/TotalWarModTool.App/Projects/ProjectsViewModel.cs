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

    private readonly ProjectsController _projects;
    private readonly IProjectDialogs _dialogs;
    private readonly AppTexts _texts;
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
        NewProjectCommand = new RelayCommand(NewProject);
        OpenProjectCommand = new RelayCommand(OpenProject);
        ImportPackCommand = new RelayCommand(async () => await RunAsync(ImportFromPickedPackAsync));
        _buildActive = new RelayCommand(async () => await BuildAsync(_projects.ActiveId!.Value, install: false), () => CanRun(BuildDisabledReason));
        _buildAndInstallActive = new RelayCommand(async () => await BuildAsync(_projects.ActiveId!.Value, install: true), () => CanRun(InstallDisabledReason));
        projects.Changed += (_, _) => RaiseCanExecuteChanged();
    }

    public ICommand NewProjectCommand { get; }

    public ICommand OpenProjectCommand { get; }

    /// <summary>Asks for a pack file and imports it into a new project.</summary>
    public ICommand ImportPackCommand { get; }

    /// <summary>Builds the active project. Disabled with <see cref="BuildDisabledReason"/> or while an operation runs.</summary>
    public ICommand BuildActiveCommand => _buildActive;

    /// <summary>Builds the active project and installs the pack. Disabled with <see cref="InstallDisabledReason"/> or while an operation runs.</summary>
    public ICommand BuildAndInstallActiveCommand => _buildAndInstallActive;

    /// <summary>The result of the last command, for the status bar; null when it had nothing to say. It can have several lines.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Reads the pack, asks what to import it as and imports it. <paramref name="sourceName"/> is the name shown in the dialog.</summary>
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

    /// <summary>Takes the project out of the list without asking: the folder stays, and "Open project" brings it back.</summary>
    public void Remove(Guid id)
    {
        StatusMessage = null;
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
            _ => null,
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

    // ---- commands ----------------------------------------------------------------------------------------------------------------

    private void NewProject()
    {
        StatusMessage = null;
        var input = _dialogs.AskProject(_texts.ProjectNewTitle, new ProjectInput(string.Empty, string.Empty, string.Empty), [], Validate);
        if (input is null)
        {
            return;
        }

        Complete(_projects.Create(input.Folder, input.Name, input.PackName), _texts.ProjectMessageTitle,
            _texts.ProjectCreated(DisplayText.Safe(input.Name, MaxNameChars)));
    }

    private void OpenProject()
    {
        StatusMessage = null;
        if (_dialogs.PickFolder(_texts.ProjectOpenTitle) is not { } folder)
        {
            return;
        }

        var result = _projects.AddExisting(folder);

        // The controller appends the project to the list, so it is the last one.
        Complete(result, _texts.ProjectMessageTitle,
            result.Ok && _projects.Projects.Count > 0 ? _texts.ProjectAdded(DisplayText.Safe(_projects.Projects[^1].DisplayName, MaxNameChars)) : null);
    }

    private async Task ImportFromPickedPackAsync()
    {
        if (_dialogs.PickPackFile() is { } pack)
        {
            await ImportCoreAsync(pack, Path.GetFileName(pack));
        }
    }

    private async Task ImportCoreAsync(string sourcePack, string sourceName)
    {
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
        if (_dialogs.AskProject(_texts.ImportTitle(DisplayText.Safe(sourceName, MaxNameChars)), defaults, info, Validate) is not { } input)
        {
            return;
        }

        var outcome = await _projects.ImportAsync(sourcePack, input.Folder, input.Name, input.PackName);
        if (outcome is null)
        {
            return;
        }

        if (!outcome.Ok)
        {
            _dialogs.ShowMessage(_texts.ImportMessageTitle, outcome.Message ?? string.Empty);
            return;
        }

        StatusMessage = outcome.Message;
        if (outcome.Report is { } report && ImportSections(report) is { Count: > 0 } sections)
        {
            _dialogs.ShowReport(new Report(_texts.ImportReportTitle, outcome.Message ?? string.Empty, sections));
        }
    }

    private async Task BuildCoreAsync(Guid id, bool install)
    {
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

        // The pack is built, but the install did not happen: a status bar line is too easy to miss for that.
        if (install && !report.Installed && report.Message is not null)
        {
            _dialogs.ShowMessage(_texts.BuildMessageTitle, report.Message);
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

        Complete(result, _texts.UninstallMessageTitle, _texts.UninstallDone(DisplayText.Safe(name, MaxNameChars)));
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------

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

    private string? Validate(ProjectInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return _texts.ProjectNameEmpty;
        }

        if (ProjectListEdits.NormalizePath(input.Folder) is null)
        {
            return _texts.ProjectFolderNotAbsolute;
        }

        return _projects.CheckPackName(input.PackName);
    }

    private List<ReportSection> ImportSections(ImportReport report)
    {
        var sections = new List<ReportSection>();
        AddSection(sections, _texts.ImportReportBinaryHeading, report.StayedBinary.Select(entry => Entry(entry.PackPath, entry.Reason)));
        AddSection(sections, _texts.ImportReportSkippedHeading, report.Skipped.Select(entry => Entry(entry.PackPath, entry.Reason)));
        AddSection(sections, _texts.ImportReportDroppedHeading, report.DroppedDependencies.Select(name => DisplayText.Safe(name)));
        return sections;

        static string Entry(string path, string reason) => DisplayText.Safe(path) + ": " + DisplayText.Safe(reason);
    }

    private Report BuildProblemReport(BuildReport report)
    {
        var sections = new List<ReportSection>();
        AddSection(sections, _texts.BuildProblemsHeading, report.Problems.Select(problem => problem.Line > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{DisplayText.Safe(problem.RelativeFile)}({problem.Line}): {DisplayText.Safe(problem.Detail)}")
            : $"{DisplayText.Safe(problem.RelativeFile)}: {DisplayText.Safe(problem.Detail)}"));
        AddSection(sections, _texts.BuildIgnoredHeading, report.Ignored.Select(file => DisplayText.Safe(file)));
        return new Report(_texts.BuildReportTitle, _texts.BuildProblemsSummary(report.Problems.Count), sections);
    }

    private static void AddSection(List<ReportSection> sections, string heading, IEnumerable<string> lines)
    {
        var list = lines.ToList();
        if (list.Count > 0)
        {
            sections.Add(new ReportSection(heading, list));
        }
    }

    private ProjectState? StateOf(Guid id) => _projects.Projects.FirstOrDefault(state => state.Id == id);

    // True when the active project can be handled and no other operation runs. A running operation gives no reason text on purpose.
    private bool CanRun(Func<Guid?, string?> disabledReason) => disabledReason(_projects.ActiveId) is null && _projects.CanStartOperation;

    private void RaiseCanExecuteChanged()
    {
        _buildActive.RaiseCanExecuteChanged();
        _buildAndInstallActive.RaiseCanExecuteChanged();
    }
}
