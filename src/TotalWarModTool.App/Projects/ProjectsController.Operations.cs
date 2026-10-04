using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TotalWarModTool.Operations;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Schema;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

// The operations of the controller: import, build, install, uninstall, listing and opening files. All of them run through the operation
// runner; the list and Changed are updated on the calling (UI) thread after the run.
public sealed partial class ProjectsController
{
    // ---- operations (through the runner) -----------------------------------------------------------------------------------------

    /// <summary>True when no other operation runs and the workspace is not busy; every operation below returns null otherwise.</summary>
    public bool CanStartOperation => _workspace.CanStartOperation;

    /// <summary>
    /// The UI text that says why packs can be neither installed nor uninstalled; null when that is possible. Reasons: no game folder, or a
    /// project list that must not be saved in this session (the install record would be lost).
    /// </summary>
    public string? InstallDisabledReason =>
        _workspace.Location is null ? _texts.InstallNoGameFolder
        : IsAvailable && !_writable ? _texts.InstallListNotWritable
        : null;

    /// <summary>
    /// The UI text that says why no project can be built (the workspace loaded no schema); null when building is possible. Before the
    /// first load nothing is known yet, and the busy workspace stops a build anyway.
    /// </summary>
    public string? BuildDisabledReason => _workspace.Schema is { Schema: null } ? _texts.BuildNoSchema : null;

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
            : new ImportPreviewResult(null, MessageText.WithDetail(_texts.PackProblemText(preview.Problem, _workspace.Profile?.PackFormat), ShownDetail(preview.Detail)));
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

        if (CheckName(name) is { } nameText)
        {
            return ImportFailed(nameText);
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
            return new ImportOutcome(false, MessageText.WithDetail(_texts.ImportProblemText(report.Problem), ShownDetail(report.Detail)), report, null);
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
    /// Refused without a schema (<see cref="BuildDisabledReason"/>) and, with <paramref name="install"/>, for <see cref="InstallDisabledReason"/>.
    /// When the data folder changed but the list cannot be saved afterwards, the report carries a warning.
    /// </summary>
    public async Task<BuildReport?> BuildAsync(Guid id, bool install)
    {
        if (StateOf(id) is not { } state)
        {
            return BuildFailed(_texts.ProjectUnknown);
        }

        if (state.Project is not { } project)
        {
            return BuildFailed(MessageText.WithDetail(_texts.ProjectProblemText(state.Problem), ShownDetail(state.Detail)));
        }

        if (BuildDisabledReason is { } noBuild)
        {
            return BuildFailed(noBuild);
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
        var name = Shown(state.DisplayName, MaxNameChars);
        var title = install ? _texts.OperationBuildAndInstall(name) : _texts.OperationBuild(name);
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
            var request = new InstallRequest(dataFolder, project.PackName, sameName ? recorded : null, vanilla);
            InstallResult? uninstalled = null;
            if (recorded is not null && !sameName)
            {
                // Nothing is removed unless the new pack can be installed afterwards: a file under the new name that is not ours (or any
                // other reason against the new name) stops the run here, with the old pack and its record untouched.
                var ownership = PackInstaller.CheckOwnership(request);
                if (ownership != InstallProblem.None)
                {
                    return new BuildRun(plan, written, null, new InstallResult(ownership, null, null));
                }

                // The last moment a cancel may stop the run without a trace: after the removal the run must end with a value.
                ct.ThrowIfCancellationRequested();
                progress.Report(new OperationProgress(0, null, recorded.FileName));
                uninstalled = PackInstaller.Uninstall(new InstallRequest(dataFolder, recorded.FileName, recorded, vanilla));
                if (uninstalled.Problem != InstallProblem.None)
                {
                    return new BuildRun(plan, written, uninstalled, null);
                }
            }

            try
            {
                progress.Report(new OperationProgress(0, null, project.PackName));
                return new BuildRun(plan, written, uninstalled, PackInstaller.Install(written.PackPath!, request, ct));
            }
            catch (OperationCanceledException) when (uninstalled is not null)
            {
                // The old pack is gone. Ending without a value would leave its record in the list, so the cancel becomes the result.
                return new BuildRun(plan, written, uninstalled, null, InstallCancelled: true);
            }
        });

        // A value means the work really happened (the file system changed), whatever the status says.
        if (outcome.Value is { } run)
        {
            return ApplyBuildRun(state, project, run, dataFolder);
        }

        // A cancelled install can leave its temp file behind (the cancel cannot report it): it is named so that the user can delete it.
        return outcome.Status == OperationStatus.Cancelled && FindInstallLeftover(dataFolder, project.PackName) is { } leftover
            ? BuildFailed(_texts.InstallLeftover(leftover))
            : null;
    }

    /// <summary>
    /// Deletes the pack the tool installed for the project from the data folder and forgets the record. Null when nothing can be started
    /// right now, the run was cancelled or it failed. A record whose file is gone already is forgotten as well. When the list cannot be
    /// saved afterwards, the result carries a warning.
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
            return Fail(_texts.UninstallNothing);
        }

        if (!CanStartOperation)
        {
            return null;
        }

        var request = new InstallRequest(_workspace.Location!.DataPath, recorded.FileName, recorded, _vanilla);
        var outcome = await _runner.RunAsync(_texts.OperationUninstall(Shown(state.DisplayName, MaxNameChars)), (_, _) => PackInstaller.Uninstall(request));
        if (outcome.Value is not { } result)
        {
            return null;
        }

        if (result.Problem != InstallProblem.None)
        {
            return Fail(MessageText.WithDetail(_texts.InstallProblemText(result.Problem), ShownDetail(result.Detail)));
        }

        SetInstalled(id, null);
        var (note, warning) = CommitInstalledChange(_texts.UninstallRecordNotSaved(recorded.FileName));
        return new ProjectActionResult(true, note, warning);
    }

    /// <summary>Lists the files of the pack folder of a project, through the runner. Null for an unknown project, when nothing can be started or the run did not finish.</summary>
    public async Task<ProjectFileList?> ListFilesAsync(Guid id)
    {
        if (StateOf(id) is not { } state || !CanStartOperation)
        {
            return null;
        }

        var folder = state.Folder;
        var outcome = await _runner.RunAsync(_texts.OperationOpenProject(Shown(state.DisplayName, MaxNameChars)), (_, ct) => ProjectFiles.List(folder, ct));
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
            _log.Warn($"The files of the project \"{DisplayText.Safe(state.DisplayName, MaxNameChars)}\" cannot be listed: {DisplayText.Safe(ex.Message)}");
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

    /// <summary>
    /// Brings back the project part of a stored session, silently and on a pool thread: the file lists of the projects that were open and
    /// the project files that were open as tabs. What cannot be brought back is left out and logged (an unknown or unusable project, a file
    /// that is gone or cannot be read, a path that leaves the pack folder); bad input never throws. A file that is read but cannot be decoded
    /// stays, as a tab that shows the problem. Tables are decoded with the schema of the workspace at the time of the call.
    /// </summary>
    public async Task<ProjectRestoreResult> RestoreAsync(IReadOnlyList<Guid> expanded, IReadOnlyList<(Guid ProjectId, string RelativeFile)> tabs)
    {
        ArgumentNullException.ThrowIfNull(expanded);
        ArgumentNullException.ThrowIfNull(tabs);

        var states = _states;
        var schema = _workspace.Schema?.Schema;
        var run = await Task.Run(() => RestoreCore(states, schema, expanded, tabs));

        // Logged here, not on the pool thread: the log is used on the UI thread only.
        foreach (var (isWarning, text, error) in run.Notes)
        {
            if (error is not null)
            {
                _log.Error(text, error);
            }
            else if (isWarning)
            {
                _log.Warn(text);
            }
            else
            {
                _log.Info(text);
            }
        }

        return run.Result;
    }

    private static RestoreRun RestoreCore(IReadOnlyList<ProjectState> states, GameSchema? schema, IReadOnlyList<Guid> expanded,
        IReadOnlyList<(Guid ProjectId, string RelativeFile)> tabs)
    {
        var notes = new List<(bool IsWarning, string Text, Exception? Error)>();
        var byId = states.ToDictionary(state => state.Id);
        var lists = new List<(Guid Id, ProjectFileList Files)>();
        foreach (var id in expanded.Distinct())
        {
            if (!byId.TryGetValue(id, out var state) || !state.IsUsable)
            {
                notes.Add((false, $"Session: project {id} is no longer in the list or cannot be used and stays closed.", null));
                continue;
            }

            try
            {
                lists.Add((id, ProjectFiles.List(state.Folder, CancellationToken.None)));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                notes.Add((true, $"Session: the files of the project \"{DisplayText.Safe(state.DisplayName, 100)}\" cannot be listed and the project stays closed: {DisplayText.Safe(ex.Message)}", null));
            }
            catch (Exception ex)
            {
                // Mirrors the restore of the workspace: whatever else goes wrong is logged and costs only this entry, never the restore.
                notes.Add((true, $"Session: restoring the files of the project \"{DisplayText.Safe(state.DisplayName, 100)}\" failed, the project stays closed.", ex));
            }
        }

        var restored = new List<RestoredProjectTab>();
        foreach (var (id, relativeFile) in tabs)
        {
            var shown = DisplayText.Safe(relativeFile ?? string.Empty, 200);
            if (!byId.TryGetValue(id, out var state) || state.Project is not { } project)
            {
                notes.Add((false, $"Session: tab \"{shown}\" was dropped, project {id} is no longer in the list or cannot be used.", null));
                continue;
            }

            // Checked before anything touches the file system: a stored path must not lead out of the pack folder.
            if (string.IsNullOrWhiteSpace(relativeFile) || Path.IsPathRooted(relativeFile) || relativeFile.Split('\\', '/').Contains(".."))
            {
                notes.Add((false, $"Session: tab \"{shown}\" of the project \"{DisplayText.Safe(state.DisplayName, 100)}\" was dropped, the path is not a path below the pack folder.", null));
                continue;
            }

            try
            {
                var opened = ProjectFileOpener.Open(state.Folder, relativeFile, schema, CancellationToken.None);
                if (opened.ReadProblem == EntryProblem.Unreadable)
                {
                    notes.Add((false, $"Session: tab \"{shown}\" of the project \"{DisplayText.Safe(state.DisplayName, 100)}\" was dropped, the file cannot be read: {DisplayText.Safe(opened.ReadDetail ?? string.Empty)}", null));
                    continue;
                }

                restored.Add(new RestoredProjectTab(id, MessageText.EscapeControlCharacters(state.DisplayName), opened));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                notes.Add((true, $"Session: tab \"{shown}\" of the project \"{DisplayText.Safe(state.DisplayName, 100)}\" was dropped: {DisplayText.Safe(ex.Message)}", null));
            }
            catch (Exception ex)
            {
                notes.Add((true, $"Session: restoring tab \"{shown}\" of the project \"{DisplayText.Safe(state.DisplayName, 100)}\" failed, the tab was dropped.", ex));
            }
        }

        return new RestoreRun(new ProjectRestoreResult(lists, restored), notes);
    }

    private sealed record RestoreRun(ProjectRestoreResult Result, IReadOnlyList<(bool IsWarning, string Text, Exception? Error)> Notes);

    private sealed record PreviewRead(ImportPreview? Preview, PackProblem Problem, string? Detail);

    // What one build run did. Written is null when the plan had problems; Uninstalled and Installed are null when that step did not run.
    // InstallCancelled: the old pack was removed and the install was cancelled afterwards.
    private sealed record BuildRun(BuildPlan Plan, BuildResult? Written, InstallResult? Uninstalled, InstallResult? Installed, bool InstallCancelled = false);

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
    private BuildReport ApplyBuildRun(ProjectState state, ModProject project, BuildRun run, string? dataFolder)
    {
        var ignored = run.Plan.Ignored;
        if (run.Written is not { } written)
        {
            return new BuildReport(false, false, null, run.Plan.Problems, ignored, null);
        }

        if (written.Outcome != BuildOutcome.Built)
        {
            return new BuildReport(false, false, null, [], ignored, MessageText.WithDetail(_texts.BuildWriteFailed, ShownDetail(written.Detail)));
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
        var oldName = state.Installed?.FileName ?? project.PackName;
        var oldRemoved = run.Uninstalled is { Problem: InstallProblem.None };
        if (run.Uninstalled is { Problem: not InstallProblem.None } stuck)
        {
            message = Join(_texts.BuildOldPackNotRemoved(oldName), MessageText.WithDetail(_texts.InstallProblemText(stuck.Problem), ShownDetail(stuck.Detail)));
        }
        else if (run.InstallCancelled)
        {
            message = Join(_texts.BuildOldRemovedNewNotInstalled(oldName, project.PackName),
                FindInstallLeftover(dataFolder, project.PackName) is { } leftover ? _texts.InstallLeftover(leftover) : null);
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

            message = MessageText.WithDetail(_texts.InstallProblemText(result.Problem), ShownDetail(result.Detail));
            if (oldRemoved && result.Problem != InstallProblem.InstalledStateUnknown)
            {
                // The user must know that nothing is installed any more, not only why the new pack could not be.
                message = Join(_texts.BuildOldRemovedNewNotInstalled(oldName, project.PackName), message);
            }
        }

        string? warning = null;
        if (listChanged)
        {
            // The new pack reached the data folder (also when its state could not be read back); otherwise only the old one was removed.
            var happened = run.Installed is { Problem: InstallProblem.None or InstallProblem.InstalledStateUnknown }
                ? _texts.InstallRecordNotSaved(project.PackName)
                : _texts.UninstallRecordNotSaved(oldName);
            (var note, warning) = CommitInstalledChange(happened);
            message = Join(message, note);
        }

        return new BuildReport(true, installed, written.PackPath, [], ignored, message, warning);
    }

    // The temp file the install of the library copies to first (the library's own suffix); null when it does not exist.
    private static string? FindInstallLeftover(string? dataFolder, string packName)
    {
        if (dataFolder is null)
        {
            return null;
        }

        var path = Path.Combine(dataFolder, packName + ".twmt-tmp");
        return File.Exists(path) ? path : null;
    }

    private string? IgnoredNote(IReadOnlyList<string> ignored) => ignored.Count > 0 ? _texts.BuildIgnored(ignored.Count) : null;

    // Records what is installed for a project, in the list and in the state. Saving and Changed follow in CommitInstalledChange.
    private void SetInstalled(Guid id, InstalledPack? installed)
    {
        _list = ProjectListEdits.SetInstalled(_list, id, installed).List;
        _states = [.. _states.Select(state => state.Id == id ? state with { Installed = installed } : state)];
    }

    // Saves the list after the data folder changed and announces the change. Note: the status note when the list is not saved. Warning:
    // when saving was tried and failed, what happened in the data folder and the detail of the file error, for a message box.
    private (string? Note, string? Warning) CommitInstalledChange(string happened)
    {
        var note = Persist(out var failure);
        Changed?.Invoke(this, EventArgs.Empty);
        return (note, failure is null ? null : MessageText.WithDetail(happened, Shown(failure)));
    }
}
