using System.IO;
using System.Threading;
using System.Windows.Input;
using TotalWarModTool.Operations;
using TotalWarModTool.Schema;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>View model of the main window.</summary>
public sealed class MainViewModel : ObservableObject
{
    private const int SlowStepCount = 100;
    private static readonly TimeSpan SlowStepDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan FailDelay = TimeSpan.FromSeconds(1);

    private readonly WorkspaceController _workspace;
    private readonly RelayCommand _refreshCommand;

    /// <param name="openFolder">Opens a folder in the file explorer. Passed in so the view model stays free of processes and tests need no explorer.</param>
    /// <param name="progress">The runner every user-started operation goes through. The workspace and the debug commands use it.</param>
    /// <param name="openSettings">Opens the settings window (or activates it if it is open). Passed in so the view model knows no windows.</param>
    /// <param name="workspace">The state of the game. The explorer, the tabs and the status line are built on it.</param>
    public MainViewModel(AppTexts texts, AppPaths paths, Action<string> openFolder, IOperationRunner progress, Action openSettings,
        WorkspaceController workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Texts = texts;
        _workspace = workspace;
        OpenSettingsCommand = new RelayCommand(openSettings);
        OpenLogFolderCommand = new RelayCommand(() => openFolder(paths.LogDirectory));

        Tabs = new TabsViewModel(texts);
        Explorer = new ExplorerViewModel(workspace, texts, OpenEntryAsync, openSettings);

        // async void on purpose, as for every command that starts an operation: the controller handles its failures, a surprise reaches the global handler.
        _refreshCommand = new RelayCommand(async () => await workspace.RefreshAsync(), () => workspace.CanStartOperation && workspace.Profile is not null);
        workspace.PackChanged += (_, pack) => Tabs.MarkStale(pack.Path);
        workspace.Changed += (_, _) =>
        {
            _refreshCommand.RaiseCanExecuteChanged();
            RaiseStatusChanged();
        };

        // The status line is built from texts: a language switch changes all of it.
        texts.PropertyChanged += (_, _) => RaiseStatusChanged();

        // The debug commands exist in every build; only the menu that offers them is limited to the debug build (IsDebugBuild).
        DebugThrowCommand = new RelayCommand(() => throw new InvalidOperationException("Debug test exception"));
        DebugSlowCommand = new RelayCommand(async () => await progress.RunAsync(texts.MenuDebugSlow, RunSlowWork));
        DebugFailCommand = new RelayCommand(async () => await progress.RunAsync<int>(texts.MenuDebugFail, RunFailingWork));
    }

    public AppTexts Texts { get; }

    public ExplorerViewModel Explorer { get; }

    public TabsViewModel Tabs { get; }

    /// <summary>Scans the packs and loads the schema again. Enabled when a game profile exists and no operation runs.</summary>
    public ICommand RefreshCommand => _refreshCommand;

    /// <summary>The name of the game, and "not found" or "loading" where that applies.</summary>
    public string StatusGame
    {
        get
        {
            var profile = _workspace.Profile;
            if (profile is null)
            {
                return Texts.StatusGameNone;
            }

            var text = _workspace.Status == GameStatus.NotFound ? Texts.StatusGameNotFound(profile.DisplayName) : Texts.StatusGame(profile.DisplayName);
            return _workspace.IsBusy && _workspace.Catalog is null ? text + " – " + Texts.StatusLoading : text;
        }
    }

    /// <summary>The install folder, null when the game was not found.</summary>
    public string? StatusGameToolTip => _workspace.Location?.InstallPath;

    public string StatusSchema => Texts.StatusSchema(Texts.SchemaSourceText(_workspace.Schema));

    /// <summary>The first thing the user should know about the data that is shown, null when there is nothing.</summary>
    public string? StatusHint
    {
        get
        {
            if (_workspace.Schema is not { } schema)
            {
                return null;
            }

            if (schema.Origin == SchemaOrigin.None)
            {
                return Texts.StatusHintNoSchema(Texts.SchemaProblemText(schema.ShippedAttempt?.Problem ?? SchemaProblem.None));
            }

            if (schema.OverrideAttempt is { Problem: not SchemaProblem.None } failed && _workspace.Profile is { } profile)
            {
                return Texts.StatusHintSchemaOverrideFailed(profile.SchemaFile, Texts.SchemaProblemText(failed.Problem));
            }

            return _workspace.Catalog?.FolderProblems.FirstOrDefault() is { } folder ? Texts.StatusHintFolderProblem(folder.Path) : null;
        }
    }

    public bool HasStatusHint => StatusHint is not null;

    /// <summary>Opens the entry in a tab. An entry that is already open only gets its tab selected; nothing is read then.</summary>
    public async Task OpenEntryAsync(PackSource pack, string entryPath)
    {
        if (Tabs.TryActivate(pack.Path, entryPath))
        {
            return;
        }

        if (await _workspace.OpenEntryAsync(pack, entryPath) is { } opened)
        {
            Tabs.Add(pack, opened, _workspace.Schema);
        }
    }

    private void RaiseStatusChanged()
    {
        OnPropertyChanged(nameof(StatusGame));
        OnPropertyChanged(nameof(StatusGameToolTip));
        OnPropertyChanged(nameof(StatusSchema));
        OnPropertyChanged(nameof(StatusHint));
        OnPropertyChanged(nameof(HasStatusHint));
    }

    /// <summary>True in the debug build only. The Debug menu binds its visibility to it.</summary>
    public bool IsDebugBuild { get; } = ReadIsDebugBuild();

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenLogFolderCommand { get; }

    /// <summary>Throws on the UI thread, to try out the global exception handling and the crash dialog.</summary>
    public ICommand DebugThrowCommand { get; }

    /// <summary>A five-second operation with a progress count, to try out the progress window and Cancel.</summary>
    public ICommand DebugSlowCommand { get; }

    /// <summary>An operation that fails after one second, to try out the error report of the progress runner.</summary>
    public ICommand DebugFailCommand { get; }

    private static bool ReadIsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    // Waits on the token instead of sleeping, so Cancel ends the run at once. Returns the steps done, which is also the partial result after a cancel.
    private static int RunSlowWork(IProgress<OperationProgress> progress, CancellationToken cancellation)
    {
        var done = 0;
        while (done < SlowStepCount && !cancellation.WaitHandle.WaitOne(SlowStepDelay))
        {
            done++;
            progress.Report(new OperationProgress(done, SlowStepCount, $"step {done}"));
        }

        return done;
    }

    private static int RunFailingWork(IProgress<OperationProgress> progress, CancellationToken cancellation)
    {
        Thread.Sleep(FailDelay);
        throw new IOException("Debug failure");
    }
}
