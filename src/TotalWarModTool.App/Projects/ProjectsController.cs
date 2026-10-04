using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TotalWarModTool.Logging;
using TotalWarModTool.Operations;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Storage;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// The mod projects of the current game: the list, the state of every project file, and the changes to the list. Lives on the UI thread;
/// the project files are small, so they are read there. The list is written only through the <see cref="ProjectListStore"/>, the project
/// folders only through the library. Failures come back as ready UI texts, never as exceptions.
/// </summary>
public sealed class ProjectsController
{
    private readonly ProjectListStore? _store;
    private readonly WorkspaceController _workspace;
    private readonly IOperationRunner _runner;
    private readonly IAppLog _log;
    private readonly AppTexts _texts;
    private readonly TimeProvider _time;
    private readonly string? _gameId;

    // False for a list that was loaded as Unreadable or NewerVersion: the list in memory is empty or repaired, and saving it would
    // destroy what the file holds.
    private readonly bool _writable;

    private ProjectList _list;
    private IReadOnlyList<ProjectState> _states;
    private IReadOnlySet<string> _vanilla;

    /// <param name="store">The store of the list of this game; null when there is no game profile.</param>
    /// <param name="loaded">What <paramref name="store"/> returned on load; its status decides whether the list may be written back.</param>
    /// <param name="runner">Runs the operations the user starts (import, build, install, listing and opening files).</param>
    /// <param name="time">The build time that is written into the built pack.</param>
    public ProjectsController(ProjectListStore? store, ProjectListLoadResult loaded, WorkspaceController workspace,
        IOperationRunner runner, IAppLog log, AppTexts texts, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(time);

        _store = store;
        _workspace = workspace;
        _runner = runner;
        _log = log;
        _texts = texts;
        _time = time;
        _gameId = workspace.Profile?.Id;
        _vanilla = ComputeVanillaNames(workspace);

        if (!IsAvailable)
        {
            _list = ProjectList.Empty;
            _states = [];
            _writable = false;
        }
        else
        {
            _list = loaded.List;
            // A corrupt file is normally copied to .bad before the empty list replaces it; without that copy it is the only copy of the
            // user's list and stays as it is.
            var corruptWithoutCopy = loaded.Status == LoadStatus.Corrupt && loaded.BadCopyPath is null;
            _writable = loaded.Status is not (LoadStatus.NewerVersion or LoadStatus.Unreadable) && !corruptWithoutCopy;
            if (!_writable)
            {
                var reason = loaded.Status switch
                {
                    LoadStatus.NewerVersion => "was written by a newer version of the app and would lose what this version does not know",
                    LoadStatus.Corrupt => "is damaged and could not be copied to a .bad file, so it is the only copy and is not overwritten",
                    _ => "could not be read and would be overwritten with an empty list",
                };
                log.Warn($"The project list will not be saved in this session: {store!.FilePath} {reason}.");
            }
            else if (loaded.Status == LoadStatus.Corrupt)
            {
                log.Warn($"The project list {store!.FilePath} is damaged; the list starts empty. A copy of the damaged file was kept as {loaded.BadCopyPath}.");
            }

            _states = [.. _list.Projects.Select(LoadState)];
            foreach (var state in _states.Where(s => !s.IsUsable))
            {
                log.Warn($"The project \"{state.DisplayName}\" in \"{state.Folder}\" cannot be used ({state.Problem}): {state.Detail}");
            }
        }

        workspace.Changed += (_, _) => OnWorkspaceChanged();
    }

    /// <summary>True when a game profile exists, so projects can be listed and changed. Without one the list is empty and every action fails.</summary>
    public bool IsAvailable => _store is not null && _gameId is not null;

    /// <summary>The projects in list order.</summary>
    public IReadOnlyList<ProjectState> Projects => _states;

    public Guid? ActiveId => _list.ActiveId;

    public ProjectState? Active => _states.FirstOrDefault(state => state.Id == _list.ActiveId);

    /// <summary>The names of the vanilla packs of the catalog, ignoring case. Empty while there is no catalog.</summary>
    public IReadOnlySet<string> VanillaPackNames => _vanilla;

    /// <summary>The list, a project state or <see cref="VanillaPackNames"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Creates a project in a new or empty folder and adds it to the list. Nothing is created when the pack name, the folder or the list
    /// refuse it.
    /// </summary>
    public ProjectActionResult Create(string folder, string name, string packName)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(packName);

        if (!IsAvailable)
        {
            return Fail(_texts.ProjectUnknown);
        }

        if (CheckPackName(packName) is { } packNameText)
        {
            return Fail(packNameText);
        }

        if (ProjectListEdits.NormalizePath(folder) is not { } fullFolder)
        {
            return Fail(_texts.ProjectFolderNotAbsolute);
        }

        // Checked before anything is created: a project must not appear on disk without ending up in the list.
        if (_list.Projects.FirstOrDefault(p => string.Equals(p.Path, fullFolder, StringComparison.OrdinalIgnoreCase)) is { } listed)
        {
            return Fail(_texts.ProjectAlreadyInList(listed.Path));
        }

        var created = ProjectFile.CreateNew(fullFolder, name, _gameId!, packName, []);
        return created.Project is { } project ? AddToList(project, fullFolder) : Fail(ProblemMessage(created));
    }

    /// <summary>Adds the project that already lives in <paramref name="folder"/> to the list. The folder is only read.</summary>
    public ProjectActionResult AddExisting(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (!IsAvailable)
        {
            return Fail(_texts.ProjectUnknown);
        }

        if (ProjectListEdits.NormalizePath(folder) is not { } fullFolder)
        {
            return Fail(_texts.ProjectFolderNotAbsolute);
        }

        var loaded = ProjectFile.Load(fullFolder);
        if (loaded.Project is not { } project)
        {
            return Fail(ProblemMessage(loaded));
        }

        return IsOwnGame(project) ? AddToList(project, fullFolder) : Fail(_texts.ProjectOtherGame);
    }

    /// <summary>Takes the project out of the list. The folder and its files stay untouched.</summary>
    public ProjectActionResult Remove(Guid id)
    {
        if (!IsAvailable)
        {
            return Fail(_texts.ProjectUnknown);
        }

        var edit = ProjectListEdits.Remove(_list, id);
        if (edit.Problem != ProjectListProblem.None)
        {
            return Fail(_texts.ProjectUnknown);
        }

        _list = edit.List;
        _states = [.. _states.Where(state => state.Id != id)];
        return Commit();
    }

    public ProjectActionResult SetActive(Guid id)
    {
        if (!IsAvailable)
        {
            return Fail(_texts.ProjectUnknown);
        }

        var edit = ProjectListEdits.SetActive(_list, id);
        if (edit.Problem != ProjectListProblem.None)
        {
            return Fail(_texts.ProjectUnknown);
        }

        if (_list.ActiveId == id)
        {
            return new ProjectActionResult(true, null);
        }

        _list = edit.List;
        return Commit();
    }

    /// <summary>Reads every project file again (F5). <see cref="Changed"/> is raised once when something observable differs.</summary>
    public void Reload()
    {
        if (!IsAvailable)
        {
            return;
        }

        var fresh = _list.Projects.Select(LoadState).ToList();
        if (_states.Count == fresh.Count && _states.Zip(fresh).All(pair => SameState(pair.First, pair.Second)))
        {
            return;
        }

        // One warning per project that became unusable with this reload; a project that stays unusable was reported before.
        foreach (var (before, after) in _states.Zip(fresh).Where(pair => pair.First.IsUsable && !pair.Second.IsUsable))
        {
            _log.Warn($"The project \"{before.DisplayName}\" in \"{after.Folder}\" can no longer be used ({after.Problem}): {after.Detail}");
        }

        _states = fresh;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- operations (through the runner) -----------------------------------------------------------------------------------------

    /// <summary>True when no other operation runs and the workspace is not busy; every operation below returns null otherwise.</summary>
    public bool CanStartOperation => _workspace.CanStartOperation;

    /// <summary>The UI text that says why packs cannot be installed (no game folder); null when installing is possible.</summary>
    public string? InstallDisabledReason => _workspace.Location is null ? _texts.InstallNoGameFolder : null;

    /// <summary>
    /// Reads the index of a pack and counts what an import would do. Null when nothing can be started right now or the run was cancelled
    /// or failed. A pack that cannot be read comes back as a result with a message.
    /// </summary>
    public async Task<ImportPreviewResult?> PreviewImportAsync(string sourcePack)
    {
        ArgumentNullException.ThrowIfNull(sourcePack);

        if (!Path.IsPathFullyQualified(sourcePack))
        {
            return new ImportPreviewResult(null, _texts.ProjectPackPathNotAbsolute);
        }

        if (!CanStartOperation)
        {
            return null;
        }

        var outcome = await _runner.RunAsync(_texts.OperationReadPack(Path.GetFileName(sourcePack)), (_, _) =>
        {
            var read = PackReader.ReadIndex(sourcePack);
            return new PreviewRead(read.Index is { } index ? PackImporter.Preview(index) : null, read.Problem, read.Detail);
        });
        if (outcome.Value is not { } preview)
        {
            return null;
        }

        return preview.Preview is not null
            ? new ImportPreviewResult(preview.Preview, null)
            : new ImportPreviewResult(null, MessageText.WithDetail(_texts.PackProblemText(preview.Problem, _workspace.Profile?.PackFormat), preview.Detail));
    }

    /// <summary>
    /// Imports a pack into a new project folder and adds the project to the list. A problem or a cancel leaves the list unchanged.
    /// Null when nothing can be started right now, or the run failed.
    /// </summary>
    public async Task<ImportOutcome?> ImportAsync(string sourcePack, string targetFolder, string name, string packName)
    {
        ArgumentNullException.ThrowIfNull(sourcePack);
        ArgumentNullException.ThrowIfNull(targetFolder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(packName);

        // What the library would throw on is answered here, so that user input never reaches the runner as an exception.
        if (!IsAvailable)
        {
            return ImportFailed(_texts.ProjectUnknown);
        }

        if (string.IsNullOrWhiteSpace(name) || HasUnpairedSurrogate(name))
        {
            return ImportFailed(_texts.ProjectNameEmpty);
        }

        if (!Path.IsPathFullyQualified(sourcePack))
        {
            return ImportFailed(MessageText.WithDetail(_texts.ImportProblemText(ImportProblem.SourceUnreadable), _texts.ProjectPackPathNotAbsolute));
        }

        if (ProjectListEdits.NormalizePath(targetFolder) is not { } fullTarget)
        {
            return ImportFailed(_texts.ProjectFolderNotAbsolute);
        }

        // Checked before the import writes anything: a project must not appear on disk without ending up in the list.
        if (_list.Projects.FirstOrDefault(p => string.Equals(p.Path, fullTarget, StringComparison.OrdinalIgnoreCase)) is { } listed)
        {
            return ImportFailed(_texts.ProjectAlreadyInList(listed.Path));
        }

        if (!CanStartOperation)
        {
            return null;
        }

        var request = new ImportRequest(sourcePack, fullTarget, name, _gameId!, packName, _vanilla);
        var schema = _workspace.Schema?.Schema;
        var outcome = await _runner.RunAsync(_texts.OperationImportPack(Path.GetFileName(sourcePack)),
            (progress, ct) => PackImporter.Import(request, schema, progress, ct));

        // A value means the import really ran to its end, whatever the status says.
        if (outcome.Value is not { } report)
        {
            return outcome.Status == OperationStatus.Cancelled ? ImportFailed(CancelledImportMessage(fullTarget)) : null;
        }

        if (report.Project is not { } project)
        {
            return new ImportOutcome(false, MessageText.WithDetail(_texts.ImportProblemText(report.Problem), report.Detail), report, null);
        }

        var added = AddToList(project, fullTarget);
        if (!added.Ok)
        {
            return new ImportOutcome(false, added.Message, report, null);
        }

        return new ImportOutcome(true, Join(_texts.ImportSummary(report.Tables, report.LocFiles, report.OtherFiles),
            report.StayedBinary.Count > 0 ? _texts.ImportStayedBinary(report.StayedBinary.Count) : null,
            report.Skipped.Count > 0 ? _texts.ImportSkipped(report.Skipped.Count) : null,
            report.DroppedDependencies.Count > 0 ? _texts.ImportDroppedDependencies(report.DroppedDependencies.Count) : null,
            added.Message), report, project.Id);
    }

    /// <summary>
    /// Builds the pack of a project and, with <paramref name="install"/>, copies it into the data folder, all in one run. The list and
    /// <see cref="Changed"/> are updated after the run. Null when nothing can be started right now, the run was cancelled or it failed.
    /// </summary>
    public async Task<BuildReport?> BuildAsync(Guid id, bool install)
    {
        if (StateOf(id) is not { } state)
        {
            return BuildFailed(_texts.ProjectUnknown);
        }

        if (state.Project is not { } project)
        {
            return BuildFailed(MessageText.WithDetail(_texts.ProjectProblemText(state.Problem), state.Detail));
        }

        if (install && InstallDisabledReason is { } reason)
        {
            return BuildFailed(reason);
        }

        if (!CanStartOperation)
        {
            return null;
        }

        var folder = state.Folder;
        var schema = _workspace.Schema?.Schema;
        var buildTime = _time.GetUtcNow();
        var dataFolder = install ? _workspace.Location!.DataPath : null;
        var recorded = state.Installed;
        var vanilla = _vanilla;
        var title = install ? _texts.OperationBuildAndInstall(state.DisplayName) : _texts.OperationBuild(state.DisplayName);
        var outcome = await _runner.RunAsync(title, (progress, ct) =>
        {
            var plan = ProjectBuilder.ComputeBuild(folder, project, schema, () => Guid.NewGuid().ToString(), progress, ct);
            if (plan.Problems.Count > 0)
            {
                return new BuildRun(plan, null, null, null);
            }

            var written = ProjectBuilder.WriteBuild(folder, project, plan, buildTime, progress, ct);
            if (written.Outcome != BuildOutcome.Built || dataFolder is null)
            {
                return new BuildRun(plan, written, null, null);
            }

            // The pack name changed since the last install: the old pack goes first, so that no stray file of ours stays behind.
            var sameName = recorded is null || string.Equals(recorded.FileName, project.PackName, StringComparison.OrdinalIgnoreCase);
            InstallResult? uninstalled = null;
            if (recorded is not null && !sameName)
            {
                uninstalled = PackInstaller.Uninstall(new InstallRequest(dataFolder, recorded.FileName, recorded, vanilla));
                if (uninstalled.Problem != InstallProblem.None)
                {
                    return new BuildRun(plan, written, uninstalled, null);
                }
            }

            var installed = PackInstaller.Install(written.PackPath!, new InstallRequest(dataFolder, project.PackName, sameName ? recorded : null, vanilla), ct);
            return new BuildRun(plan, written, uninstalled, installed);
        });

        // A value means the work really happened (the file system changed), whatever the status says.
        return outcome.Value is { } run ? ApplyBuildRun(state, project, run, install) : null;
    }

    /// <summary>
    /// Deletes the pack the tool installed for the project from the data folder and forgets the record. Null when nothing can be started
    /// right now, the run was cancelled or it failed.
    /// </summary>
    public async Task<ProjectActionResult?> UninstallAsync(Guid id)
    {
        if (StateOf(id) is not { } state)
        {
            return Fail(_texts.ProjectUnknown);
        }

        if (InstallDisabledReason is { } reason)
        {
            return Fail(reason);
        }

        if (state.Installed is not { } recorded)
        {
            return Fail(_texts.UninstallNotInstalled);
        }

        if (!CanStartOperation)
        {
            return null;
        }

        var request = new InstallRequest(_workspace.Location!.DataPath, recorded.FileName, recorded, _vanilla);
        var outcome = await _runner.RunAsync(_texts.OperationUninstall(state.DisplayName), (_, _) => PackInstaller.Uninstall(request));
        if (outcome.Value is not { } result)
        {
            return null;
        }

        if (result.Problem != InstallProblem.None)
        {
            return Fail(MessageText.WithDetail(_texts.InstallProblemText(result.Problem), result.Detail));
        }

        SetInstalled(id, null);
        return new ProjectActionResult(true, CommitInstalledChange());
    }

    /// <summary>Lists the files of the pack folder of a project, through the runner. Null for an unknown project, when nothing can be started or the run did not finish.</summary>
    public async Task<ProjectFileList?> ListFilesAsync(Guid id)
    {
        if (StateOf(id) is not { } state || !CanStartOperation)
        {
            return null;
        }

        var folder = state.Folder;
        var outcome = await _runner.RunAsync(_texts.OperationOpenProject(state.DisplayName), (_, ct) => ProjectFiles.List(folder, ct));
        return outcome.Value;
    }

    /// <summary>Lists the files of a project on a pool thread without the runner, for the automatic refresh. Null for an unknown project or when the folder cannot be read.</summary>
    public async Task<ProjectFileList?> ListFilesSilentlyAsync(Guid id)
    {
        if (StateOf(id) is not { } state)
        {
            return null;
        }

        var folder = state.Folder;
        try
        {
            return await Task.Run(() => ProjectFiles.List(folder, CancellationToken.None));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"The files of the project \"{state.DisplayName}\" cannot be listed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Opens one file of the pack folder of a project with the current schema, through the runner. Null for an unknown project, when nothing can be started or the run did not finish.</summary>
    public async Task<OpenedEntry?> OpenFileAsync(Guid id, string relativeFile)
    {
        ArgumentNullException.ThrowIfNull(relativeFile);

        if (StateOf(id) is not { } state || !CanStartOperation)
        {
            return null;
        }

        var folder = state.Folder;
        var schema = _workspace.Schema?.Schema;
        var fileName = relativeFile[(relativeFile.LastIndexOfAny(['/', '\\']) + 1)..];
        var outcome = await _runner.RunAsync(_texts.OperationOpenFile(fileName), (_, ct) => ProjectFileOpener.Open(folder, relativeFile, schema, ct));
        return outcome.Value;
    }

    private sealed record PreviewRead(ImportPreview? Preview, PackProblem Problem, string? Detail);

    // What one build run did. Written is null when the plan had problems; Uninstalled and Installed are null when that step did not run.
    private sealed record BuildRun(BuildPlan Plan, BuildResult? Written, InstallResult? Uninstalled, InstallResult? Installed);

    private ProjectState? StateOf(Guid id) => _states.FirstOrDefault(state => state.Id == id);

    private static ImportOutcome ImportFailed(string message) => new(false, message, null, null);

    private static BuildReport BuildFailed(string message) => new(false, false, null, [], [], message);

    // Joins the non-empty parts on separate lines; null when there is nothing to say.
    private static string? Join(params string?[] parts)
    {
        var present = parts.Where(part => !string.IsNullOrEmpty(part)).ToList();
        return present.Count == 0 ? null : string.Join("\n", present);
    }

    // A cancelled import can leave its staging folder behind (a cancel cannot report it): it is named so that the user can delete it.
    private string CancelledImportMessage(string targetFolder)
    {
        var parent = Path.GetDirectoryName(targetFolder);
        if (parent is not null && Directory.Exists(parent))
        {
            var prefix = Path.GetFileName(targetFolder) + ".import-";
            try
            {
                var leftover = Directory.EnumerateDirectories(parent, prefix + "*")
                    .FirstOrDefault(path => Path.GetFileName(path) is { } leaf && leaf.Length == prefix.Length + 8
                        && leaf[prefix.Length..].All(char.IsAsciiHexDigit));
                if (leftover is not null)
                {
                    return _texts.ImportLeftover(leftover);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"The folder \"{parent}\" cannot be searched for the leftovers of a cancelled import: {ex.Message}");
            }
        }

        return _texts.ImportCancelled;
    }

    private static bool HasUnpairedSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    // Turns what a build run did into the report, and brings the list in line with what happened in the data folder.
    private BuildReport ApplyBuildRun(ProjectState state, ModProject project, BuildRun run, bool install)
    {
        var ignored = run.Plan.Ignored;
        if (run.Written is not { } written)
        {
            return new BuildReport(false, false, null, run.Plan.Problems, ignored, null);
        }

        if (written.Outcome != BuildOutcome.Built)
        {
            return new BuildReport(false, false, null, [], ignored, MessageText.WithDetail(_texts.BuildWriteFailed, written.Detail));
        }

        var listChanged = false;
        if (run.Uninstalled is { Problem: InstallProblem.None })
        {
            // The old pack is gone, whatever happens to the new one.
            SetInstalled(state.Id, null);
            listChanged = true;
        }

        string? message;
        var installed = false;
        if (run.Uninstalled is { Problem: not InstallProblem.None } stuck)
        {
            message = Join(_texts.BuildOldPackNotRemoved(state.Installed?.FileName ?? project.PackName),
                MessageText.WithDetail(_texts.InstallProblemText(stuck.Problem), stuck.Detail));
        }
        else if (run.Installed is not { } result)
        {
            message = Join(_texts.BuildDone(project.PackName, run.Plan.Entries.Count), IgnoredNote(ignored));
        }
        else if (result.Problem == InstallProblem.None)
        {
            SetInstalled(state.Id, result.Installed);
            listChanged = installed = true;
            message = Join(_texts.BuildInstalled(project.PackName), IgnoredNote(ignored));
        }
        else
        {
            if (result.Problem == InstallProblem.InstalledStateUnknown)
            {
                // The file is in place but nothing says it is ours: forget the record, the file counts as foreign from now on.
                SetInstalled(state.Id, null);
                listChanged = true;
            }

            message = MessageText.WithDetail(_texts.InstallProblemText(result.Problem), result.Detail);
        }

        if (listChanged)
        {
            message = Join(message, CommitInstalledChange());
        }

        return new BuildReport(true, installed, written.PackPath, [], ignored, message);
    }

    private string? IgnoredNote(IReadOnlyList<string> ignored) => ignored.Count > 0 ? _texts.BuildIgnored(ignored.Count) : null;

    // Records what is installed for a project, in the list and in the state. Saving and Changed follow in CommitInstalledChange.
    private void SetInstalled(Guid id, InstalledPack? installed)
    {
        _list = ProjectListEdits.SetInstalled(_list, id, installed).List;
        _states = [.. _states.Select(state => state.Id == id ? state with { Installed = installed } : state)];
    }

    // Saves the list and announces the change; returns the note when the list could not be saved.
    private string? CommitInstalledChange() => Commit().Message;

    /// <summary>Null when <paramref name="packName"/> may be used for a project of this game; otherwise the UI text that says why not.</summary>
    public string? CheckPackName(string packName)
    {
        ArgumentNullException.ThrowIfNull(packName);
        return PackNameRules.Check(packName, _vanilla) is { } reason ? _texts.ProjectPackNameInvalid(reason) : null;
    }

    private static ProjectActionResult Fail(string message) => new(false, message);

    // The problem text and, when the library gave one, its technical detail on a second line.
    private string ProblemMessage(ProjectResult result) => MessageText.WithDetail(_texts.ProjectProblemText(result.Problem), result.Detail);

    private ProjectActionResult AddToList(ModProject project, string folder)
    {
        var edit = ProjectListEdits.Add(_list, project.Id, folder);
        if (edit.Problem != ProjectListProblem.None)
        {
            return Fail(AddProblemText(edit.Problem, project.Id, folder));
        }

        _list = edit.List;
        var entry = _list.Projects[^1];
        _states = [.. _states, new ProjectState(entry.Id, entry.Path, project, ProjectProblem.None, null, entry.Installed)];
        return Commit();
    }

    private string AddProblemText(ProjectListProblem problem, Guid id, string folder)
    {
        switch (problem)
        {
            case ProjectListProblem.DuplicateId:
                return _texts.ProjectAlreadyInList(_list.Projects.FirstOrDefault(p => p.Id == id)?.Path ?? folder);
            case ProjectListProblem.DuplicatePath:
                var samePath = _list.Projects.FirstOrDefault(p => string.Equals(p.Path, folder, StringComparison.OrdinalIgnoreCase));
                return _texts.ProjectAlreadyInList(samePath?.Path ?? folder);
            case ProjectListProblem.InvalidPath:
                return _texts.ProjectFolderNotAbsolute;
            default:
                return _texts.ProjectUnknown;
        }
    }

    // Saves the list in memory, then announces it. A list that cannot be saved stays valid in memory; the result carries the note.
    private ProjectActionResult Commit()
    {
        var note = Persist();
        Changed?.Invoke(this, EventArgs.Empty);
        return new ProjectActionResult(true, note);
    }

    private string? Persist()
    {
        if (!_writable)
        {
            // The start-up warning said why; the user is told on every change.
            return _texts.ProjectListNotSaved;
        }

        try
        {
            _store!.Save(_list);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"The project list could not be saved: {ex.Message}");
            return _texts.ProjectListNotSaved;
        }
    }

    private ProjectState LoadState(ProjectListEntry entry)
    {
        var loaded = ProjectFile.Load(entry.Path);
        if (loaded.Project is not { } project)
        {
            return new ProjectState(entry.Id, entry.Path, null, loaded.Problem, loaded.Detail, entry.Installed);
        }

        if (!IsOwnGame(project))
        {
            return new ProjectState(entry.Id, entry.Path, null, ProjectProblem.Invalid, $"the project belongs to the game '{MessageText.EscapeControlCharacters(project.GameId)}'", entry.Installed);
        }

        // The list identifies the project by its id; a file with another id is not the project the list entry stands for.
        return project.Id == entry.Id
            ? new ProjectState(entry.Id, entry.Path, project, ProjectProblem.None, null, entry.Installed)
            : new ProjectState(entry.Id, entry.Path, null, ProjectProblem.Invalid,
                $"the project file has the id '{project.Id}', the list expects '{entry.Id}'", entry.Installed);
    }

    private bool IsOwnGame(ModProject project) => string.Equals(project.GameId, _gameId, StringComparison.OrdinalIgnoreCase);

    private void OnWorkspaceChanged()
    {
        var names = ComputeVanillaNames(_workspace);
        if (!names.SetEquals(_vanilla))
        {
            _vanilla = names;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static HashSet<string> ComputeVanillaNames(WorkspaceController workspace) =>
        new((workspace.Catalog?.Packs ?? []).Where(pack => pack.Origin == PackOrigin.Vanilla).Select(pack => pack.Name),
            StringComparer.OrdinalIgnoreCase);

    // Compared by content: the record equality of ModProject compares its dependency list by reference.
    private static bool SameState(ProjectState a, ProjectState b) =>
        a.Id == b.Id && a.Folder == b.Folder && a.Problem == b.Problem && a.Detail == b.Detail && Equals(a.Installed, b.Installed)
        && (a.Project is null
            ? b.Project is null
            : b.Project is not null && a.Project.Id == b.Project.Id && a.Project.Name == b.Project.Name && a.Project.GameId == b.Project.GameId
              && a.Project.PackName == b.Project.PackName && a.Project.Dependencies.SequenceEqual(b.Project.Dependencies));
}
