using System.Windows.Input;
using TotalWarModTool.Editing;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

public enum ObjectTabState { Idle, Building, Ready, Cancelled, Failed }

/// <summary>What the controller found out when it tried to jump from the object to its row.</summary>
public enum JumpOutcome { None, SourceGone, RowNotFound }

/// <summary>
/// The tab of one object: the history of the objects it showed, the state of the build, and the text of the last result. The tab does not build
/// anything itself; the controller sets the handlers and reports the outcome of a build through <see cref="BeginBuild"/>,
/// <see cref="ApplyResult"/>, <see cref="ApplyCancelled"/> and <see cref="ApplyFailed"/>. Text, status and warnings belong to the last result and
/// stay (also while a build runs, after it was cancelled or failed) until the tab moves to another object or a new result comes.
/// </summary>
public sealed class ObjectTabViewModel : TabViewModel
{
    public const int MaxHistory = 50;

    private readonly List<ObjectRef> _history;
    private readonly Func<string, string> _translate;
    private readonly RelayCommand _backCommand;
    private readonly RelayCommand _forwardCommand;
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _jumpToRowCommand;
    private readonly RelayCommand _applyCommand;
    private Action<ObjectTabViewModel>? _jumpHandler;
    private int _position;
    private ObjectTabState _state = ObjectTabState.Idle;
    private ObjectBuildResult? _result;
    private string? _resultLine;
    private Func<ObjectTabViewModel, Task>? _applyHandler;
    private string _text = string.Empty;
    private string _baselineText = string.Empty;
    private BlockText? _block;
    private int _headLineLength;
    private bool _hasUnappliedText;
    private string _status = string.Empty;
    private string? _warningsText;
    private string? _notice;
    private string? _failedReason;
    private bool _unsavedSource;
    private bool _categoryUnreadable;
    private JumpOutcome _jumpOutcome;

    /// <param name="history">The entries so far, at least one.</param>
    /// <param name="position">The index of the one shown. The tab starts Idle.</param>
    /// <param name="translate">A loc key of a section title to its text.</param>
    public ObjectTabViewModel(AppTexts texts, Action<TabViewModel> close, IReadOnlyList<ObjectRef> history, int position, Func<string, string> translate)
        : base(SourceOf(texts, history, position), history[position].ToObjectId().ToString(), texts, close)
    {
        ArgumentNullException.ThrowIfNull(translate);
        _history = [.. history];
        _position = position;
        _translate = translate;
        _backCommand = new RelayCommand(Back, () => _position > 0 && !IsBuilding);
        _forwardCommand = new RelayCommand(Forward, () => _position < _history.Count - 1 && !IsBuilding);
        _refreshCommand = new RelayCommand(Refresh, () => !IsBuilding);
        _jumpToRowCommand = new RelayCommand(() => JumpHandler?.Invoke(this), CanJump);
        _applyCommand = new RelayCommand(Apply, () => _applyHandler is not null && ApplyDisabledReason is null);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsStale))
            {
                RefreshApplyState();
            }
        };
        _notice = NoticeText();
        ShowCurrent();
    }

    public ObjectRef Current => _history[_position];

    public IReadOnlyList<ObjectRef> History => _history;

    public int Position => _position;

    public ObjectTabState State => _state;

    /// <summary>The last successful build of <see cref="Current"/>; null before the first one and after the tab moved to another object.</summary>
    public ObjectBuildResult? Result => _result;

    /// <summary>"Vanilla + 2 mods + project", plus ", 2 of 3 mods loaded" when a mod pack is missing, plus ", 3 warnings" when there are warnings; empty without a result.</summary>
    public string Status => _status;

    /// <summary>Every warning of the view as "Kind: Name: Detail" lines (data, escaped), for the tool tip of the status; null without warnings.</summary>
    public string? WarningsText => _warningsText;

    /// <summary>The text of the editor. Set by the view on every change; set by the tab when a new result comes or the text is discarded.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty))
            {
                UpdateUnapplied();
            }
        }
    }

    /// <summary>The text of a section title key (the loc key of a category rule) in the current language; for the trailing of the editor.</summary>
    public string TranslateTitle(string key) => _translate(key);

    /// <summary>The block text of the last result (ObjectText.RenderBlock(...).Text); empty without a result.</summary>
    public string BaselineText => _baselineText;

    /// <summary>The block of the last result, for the views of Plan 3b; null without a result.</summary>
    public BlockText? Block => _block;

    /// <summary>Text differs from <see cref="BaselineText"/> (ordinal). Also what <see cref="IsModified"/> reports: the header shows the mark of unsaved changes.</summary>
    public bool HasUnappliedText => _hasUnappliedText;

    public override bool IsModified => _hasUnappliedText;

    /// <summary>The length of the head line of the text (first line without its line break); 0 without a result. The view makes it read-only.</summary>
    public int HeadLineLength => _headLineLength;

    /// <summary>
    /// What the user must know, one line each, joined with line breaks: the state (Idle, Building, Cancelled, Failed), object not found, unsaved
    /// source, root pack added, used_mods.txt missing, category file unreadable, row source gone, row not found. Null when there is nothing.
    /// </summary>
    public string? Notice => _notice;

    /// <summary>Position &gt; 0 and no build running.</summary>
    public ICommand BackCommand => _backCommand;

    /// <summary>Position before the last entry and no build running.</summary>
    public ICommand ForwardCommand => _forwardCommand;

    /// <summary>Builds the current object again; no build running.</summary>
    public ICommand RefreshCommand => _refreshCommand;

    /// <summary>Ready, the object has a row, and the controller can jump.</summary>
    public ICommand JumpToRowCommand => _jumpToRowCommand;

    /// <summary>Writes the text into the project (ObjectApplyController through ApplyHandler). Enabled when ApplyDisabledReason is null.</summary>
    public ICommand ApplyCommand => _applyCommand;

    /// <summary>
    /// Why <see cref="ApplyCommand"/> is disabled, the first that applies: the environment (no active project, another operation runs), the root
    /// pack is not in the load order, the tab is not Ready, the tab is stale, the text equals the baseline. Null when it can run.
    /// </summary>
    public string? ApplyDisabledReason
    {
        get
        {
            if (ApplyEnvironmentReason?.Invoke() is { } environment)
            {
                return environment;
            }

            if (_result is { } result && result.Order.Warnings.Any(warning => warning.Problem == LoadOrderProblem.RootPackAdded))
            {
                return Texts.ObjectsApplyRootPackNotInOrder;
            }

            if (_state != ObjectTabState.Ready || _result is null)
            {
                return Texts.ObjectsApplyNotReady;
            }

            if (IsStale)
            {
                return Texts.ObjectsApplyStale;
            }

            return _hasUnappliedText ? null : Texts.ObjectsApplyNoChanges;
        }
    }

    /// <summary>The tool tip of the apply button: the reason it is disabled, else its name with the key.</summary>
    public string ApplyToolTip => ApplyDisabledReason ?? Texts.ObjectsApply + " (" + Texts.ObjectsApplyGesture + ")";

    /// <summary>The result line of the last apply ("3 rows in 2 files changed, 1 file created: db\x_tables\flo.tsv"); null until an apply, cleared by a move to another object.</summary>
    public string? ResultLine => _resultLine;

    /// <summary>Set by the controller: whether a project is active and usable, and whether an operation runs. Asked by <see cref="ApplyDisabledReason"/>.</summary>
    internal Func<string?>? ApplyEnvironmentReason { get; set; }

    /// <summary>Set by the controller: runs the apply. Without it the command is disabled.</summary>
    internal Func<ObjectTabViewModel, Task>? ApplyHandler
    {
        get => _applyHandler;
        set
        {
            _applyHandler = value;
            _applyCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Set by the controller: asked before Back, Forward, Refresh and a link navigation while there is unapplied text; true lets the tab move on (the text was applied or discarded).</summary>
    internal Func<ObjectTabViewModel, Task<bool>>? LeaveHandler { get; set; }

    /// <summary>Set by the controller: builds the current object and reports through <c>BeginBuild</c> and the <c>Apply</c> methods. Without it the tab stays as it is.</summary>
    internal Action<ObjectTabViewModel>? BuildHandler { get; set; }

    /// <summary>Set by the controller: opens the target in a new tab (from, target, newTab).</summary>
    internal Action<ObjectTabViewModel, ObjectRef, bool>? OpenHandler { get; set; }

    /// <summary>Set by the controller: selects the row of the object in the table tab it came from.</summary>
    internal Action<ObjectTabViewModel>? JumpHandler
    {
        get => _jumpHandler;
        set
        {
            _jumpHandler = value;
            _jumpToRowCommand.RaiseCanExecuteChanged();
        }
    }

    protected override string PackStaleText => Texts.ObjectsStale;

    private bool IsBuilding => _state == ObjectTabState.Building;

    /// <summary>Sets <see cref="Text"/> back to <see cref="BaselineText"/>.</summary>
    public void DiscardText() => Text = _baselineText;

    /// <summary>
    /// A link: the target replaces the forward part of the history (or opens a new tab) and is built. The target keeps the
    /// <see cref="ObjectRef.RootPackPath"/> of <see cref="Current"/>.
    /// </summary>
    public void NavigateTo(ObjectId target, bool newTab)
    {
        ArgumentNullException.ThrowIfNull(target);
        var entry = new ObjectRef(target.Table, target.KeyValues, Current.RootPackPath);
        if (newTab)
        {
            OpenHandler?.Invoke(this, entry, true);
            return;
        }

        if (_hasUnappliedText && LeaveHandler is not null)
        {
            NavigateAfterLeave(entry);
            return;
        }

        Navigate(entry);
    }

    /// <summary>The build started. The lines stay until the result comes.</summary>
    public void BeginBuild()
    {
        _failedReason = null;
        SetState(ObjectTabState.Building);
    }

    /// <summary>
    /// The build delivered <paramref name="result"/>: it is rendered, the expanded sections, the notice of a jump and the pack and schema marks
    /// are forgotten.
    /// </summary>
    public void ApplyResult(ObjectBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _result = result;
        _failedReason = null;
        _jumpOutcome = JumpOutcome.None;
        OnPropertyChanged(nameof(Result));
        _state = ObjectTabState.Ready;
        OnPropertyChanged(nameof(State));
        SetBlock(ObjectText.RenderBlock(result.View, result.Category));
        SetProperty(ref _warningsText, WarningsOf(result), nameof(WarningsText));
        ClearStale();
        RefreshDerived();
    }

    /// <summary>The build was cancelled. The old lines stay.</summary>
    public void ApplyCancelled() => SetState(ObjectTabState.Cancelled);

    /// <summary>The build failed. <paramref name="reason"/> null: the general text; else the given text (for example that no game is set).</summary>
    public void ApplyFailed(string? reason = null)
    {
        _failedReason = reason;
        SetState(ObjectTabState.Failed);
    }

    /// <summary>The tab the object was opened from has unsaved changes, so the view shows the saved state.</summary>
    public void SetUnsavedSource(bool unsaved)
    {
        _unsavedSource = unsaved;
        RefreshNotice();
    }

    /// <summary>The category file of the table could not be read, so the default layout is used.</summary>
    public void SetCategoryProblem(bool unreadable)
    {
        _categoryUnreadable = unreadable;
        RefreshNotice();
    }

    /// <summary>The outcome of the last jump to the row; it is forgotten with the next result.</summary>
    public void SetJumpNotice(JumpOutcome outcome)
    {
        _jumpOutcome = outcome;
        RefreshNotice();
    }

    /// <summary>The controller reports the result of an apply: the result line is set (counts, created files, warnings) and shown until the tab moves to another object.</summary>
    public void SetApplyResult(WriteSummary summary, IReadOnlyList<string> createdFiles, int warnings)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(createdFiles);
        var line = Texts.ObjectsApplyResultChanged(summary.RowsSet + summary.RowsInserted + summary.RowsDeleted, summary.FilesChanged);
        if (summary.TextsSet + summary.TextsInserted > 0)
        {
            line += ", " + Texts.ObjectsApplyResultTexts(summary.TextsSet + summary.TextsInserted);
        }

        if (summary.FilesCreated + summary.Copies > 0)
        {
            line += ", " + Texts.ObjectsApplyResultCreated(summary.FilesCreated + summary.Copies,
                string.Join(", ", createdFiles.Select(MessageText.EscapeControlCharacters)));
        }

        if (warnings > 0)
        {
            line += ", " + Texts.ObjectsApplyResultWarnings(warnings);
        }

        SetProperty(ref _resultLine, line, nameof(ResultLine));
    }

    /// <summary>The controller says that the apply environment changed (project, runner): the reason and the command ask again.</summary>
    public void RefreshApplyState()
    {
        OnPropertyChanged(nameof(ApplyDisabledReason));
        OnPropertyChanged(nameof(ApplyToolTip));
        _applyCommand.RaiseCanExecuteChanged();
    }

    protected override void RefreshTexts() => RefreshDerived();

    private static TabSource SourceOf(AppTexts texts, IReadOnlyList<ObjectRef> history, int position)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count == 0 || position < 0 || position >= history.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "The history needs an entry at the position.");
        }

        return TabSource.ForObject(history[position].Table, texts.ObjectsTabSourceName);
    }

    // The commands ask the LeaveHandler first when there is unapplied text; async void because a command cannot be awaited, and without a question
    // everything below runs synchronously.
    private async void Back()
    {
        if (_position > 0 && !IsBuilding && await CanLeaveAsync() && _position > 0 && !IsBuilding)
        {
            MoveTo(_position - 1);
        }
    }

    private async void Forward()
    {
        if (_position < _history.Count - 1 && !IsBuilding && await CanLeaveAsync() && _position < _history.Count - 1 && !IsBuilding)
        {
            MoveTo(_position + 1);
        }
    }

    private async void Refresh()
    {
        if (!IsBuilding && await CanLeaveAsync() && !IsBuilding)
        {
            BuildHandler?.Invoke(this);
        }
    }

    private async void Apply()
    {
        if (_applyHandler is { } handler)
        {
            await handler(this);
        }
    }

    private async void NavigateAfterLeave(ObjectRef entry)
    {
        if (await CanLeaveAsync())
        {
            Navigate(entry);
        }
    }

    private async Task<bool> CanLeaveAsync() => !_hasUnappliedText || LeaveHandler is null || await LeaveHandler(this);

    private void Navigate(ObjectRef entry)
    {
        _history.RemoveRange(_position + 1, _history.Count - _position - 1);
        _history.Add(entry);
        if (_history.Count > MaxHistory)
        {
            _history.RemoveRange(0, _history.Count - MaxHistory);
        }

        MoveTo(_history.Count - 1);
    }

    /// <summary>
    /// Shows another entry: what the old object showed, its stale marks and the flags of its source tab are dropped, then the controller builds.
    /// </summary>
    private void MoveTo(int position)
    {
        _position = position;
        ShowCurrent();
        _result = null;
        _failedReason = null;
        _jumpOutcome = JumpOutcome.None;
        _unsavedSource = false;
        _categoryUnreadable = false;
        _resultLine = null;
        _state = ObjectTabState.Idle;
        _status = string.Empty;
        _warningsText = null;
        SetBlock(null);
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(History));
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(ResultLine));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(WarningsText));
        ClearStale();
        RefreshNotice();
        RaiseCommands();
        BuildHandler?.Invoke(this);
    }

    private void ShowCurrent()
    {
        var key = MessageText.EscapeControlCharacters(Current.DisplayKey);
        Title = key;
        ToolTip = MessageText.EscapeControlCharacters(ObjectText.ShortTableName(Current.Table)) + ": " + key;
    }

    private void SetState(ObjectTabState state)
    {
        _state = state;
        OnPropertyChanged(nameof(State));
        RefreshNotice();
        RaiseCommands();
    }

    // The block of a new result (or none): baseline, head line and the editor text follow it; typed text of the old result is dropped.
    private void SetBlock(BlockText? block)
    {
        _block = block;
        OnPropertyChanged(nameof(Block));
        var baseline = block?.Text ?? string.Empty;
        if (SetProperty(ref _baselineText, baseline, nameof(BaselineText)))
        {
            UpdateUnapplied();
        }

        var lineEnd = baseline.IndexOf('\n');
        SetProperty(ref _headLineLength, lineEnd < 0 ? baseline.Length : lineEnd, nameof(HeadLineLength));
        Text = baseline;
    }

    private void UpdateUnapplied()
    {
        if (SetProperty(ref _hasUnappliedText, !string.Equals(_text, _baselineText, StringComparison.Ordinal), nameof(HasUnappliedText)))
        {
            OnPropertyChanged(nameof(IsModified));
        }

        RefreshApplyState();
    }

    private void RefreshDerived()
    {
        SetProperty(ref _status, StatusText(), nameof(Status));
        RefreshNotice();
        RaiseCommands();
    }

    private void RefreshNotice() => SetProperty(ref _notice, NoticeText(), nameof(Notice));

    private void RaiseCommands()
    {
        _backCommand.RaiseCanExecuteChanged();
        _forwardCommand.RaiseCanExecuteChanged();
        _refreshCommand.RaiseCanExecuteChanged();
        _jumpToRowCommand.RaiseCanExecuteChanged();
        RefreshApplyState();
    }

    private bool CanJump() => _state == ObjectTabState.Ready && _result?.View.Root is not null && _jumpHandler is not null;

    private string StatusText()
    {
        if (_result is null)
        {
            return string.Empty;
        }

        var sources = _result.Order.Sources;
        var mods = sources.Count(source => source.Kind == SourceKind.Mod);
        var text = sources.Any(source => source.Kind == SourceKind.Project) ? Texts.ObjectsStatusWithProject(mods) : Texts.ObjectsStatus(mods);
        var missing = _result.Order.Warnings.Count(warning => warning.Problem == LoadOrderProblem.ModPackMissing);
        if (missing > 0)
        {
            text += ", " + Texts.ObjectsStatusModsLoaded(mods, mods + missing);
        }

        var warnings = _result.View.Warnings.Count;
        return warnings > 0 ? text + ", " + Texts.ObjectsStatusWarnings(warnings) : text;
    }

    private static string? WarningsOf(ObjectBuildResult result)
    {
        if (result.View.Warnings.Count == 0)
        {
            return null;
        }

        return string.Join(Environment.NewLine, result.View.Warnings.Select(warning => MessageText.EscapeControlCharacters(
            warning.Name is null ? warning.Kind + ": " + warning.Detail : warning.Kind + ": " + warning.Name + ": " + warning.Detail)));
    }

    private string? NoticeText()
    {
        var lines = new List<string>();
        switch (_state)
        {
            case ObjectTabState.Idle:
                lines.Add(Texts.ObjectsStateIdle);
                break;
            case ObjectTabState.Building:
                lines.Add(Texts.ObjectsStateBuilding);
                break;
            case ObjectTabState.Cancelled:
                lines.Add(Texts.ObjectsStateCancelled);
                break;
            case ObjectTabState.Failed:
                lines.Add(_failedReason ?? Texts.ObjectsStateFailed);
                break;
        }

        if (_result is { } result && result.View.Warnings.Any(warning => warning.Kind == ObjectWarningKind.RootNotFound))
        {
            lines.Add(Texts.ObjectsNotFound);
        }

        if (_unsavedSource)
        {
            lines.Add(Texts.ObjectsNoticeUnsavedSource);
        }

        if (_result is not null)
        {
            var problems = _result.Order.Warnings.Select(warning => warning.Problem).ToHashSet();
            if (problems.Contains(LoadOrderProblem.RootPackAdded))
            {
                lines.Add(Texts.ObjectsNoticeRootPackAdded);
            }

            if (problems.Contains(LoadOrderProblem.UsedModsMissing) || problems.Contains(LoadOrderProblem.UsedModsUnreadable))
            {
                lines.Add(Texts.ObjectsNoticeNoUsedMods);
            }
        }

        if (_categoryUnreadable)
        {
            lines.Add(Texts.ObjectsNoticeCategoryUnreadable);
        }

        switch (_jumpOutcome)
        {
            case JumpOutcome.SourceGone:
                lines.Add(Texts.ObjectsNoticeRowSourceGone);
                break;
            case JumpOutcome.RowNotFound:
                lines.Add(Texts.ObjectsNoticeRowNotFound);
                break;
        }

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }
}
