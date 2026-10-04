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
public sealed partial class ProjectsController
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

    // Whether the game folder was known at the last workspace change: InstallDisabledReason depends on it.
    private bool _hasGameFolder;

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
        _hasGameFolder = workspace.Location is not null;

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
        var hasGameFolder = _workspace.Location is not null;
        if (!names.SetEquals(_vanilla) || hasGameFolder != _hasGameFolder)
        {
            _vanilla = names;
            _hasGameFolder = hasGameFolder;
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
