using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>
/// The row commands of the toolbar: insert, duplicate and delete rows, copy and paste. They act on the selected tab, so the toolbar needs no
/// view of the grid. An editable tab gets all five; a read-only table or loc tab only gets copy; any other tab, or none, gets nothing. A
/// command that cannot run says why in a reason (null while it can run), which the tool tip of the button shows.
/// Follows <see cref="TabsViewModel.SelectedTab"/>, the <c>CanExecuteChanged</c> of the commands of that tab, and the language. It listens to
/// the commands of the selected tab only and lets go of them when the selection moves on.
/// </summary>
public sealed class RowCommandsViewModel : ObservableObject
{
    private readonly TabsViewModel _tabs;
    private readonly AppTexts _texts;
    private readonly RelayCommand _insertRow;
    private readonly RelayCommand _duplicateRows;
    private readonly RelayCommand _deleteRows;
    private readonly RelayCommand _copy;
    private readonly RelayCommand _paste;

    // The commands of the selected tab that are listened to, so that the next change of the selection can stop listening to them.
    private readonly List<ICommand> _followed = [];

    public RowCommandsViewModel(TabsViewModel tabs, AppTexts texts)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(texts);
        _tabs = tabs;
        _texts = texts;
        _insertRow = Forward(InsertTarget);
        _duplicateRows = Forward(DuplicateTarget);
        _deleteRows = Forward(DeleteTarget);
        _copy = Forward(CopyTarget);
        _paste = Forward(PasteTarget);

        tabs.Changed += (_, _) => FollowSelectedTab();
        texts.PropertyChanged += (_, _) => RaiseChanged();
        FollowSelectedTab();
    }

    /// <summary>A default row after the last selected row, or at the end. Of the selected editable tab.</summary>
    public ICommand InsertRowCommand => _insertRow;

    /// <summary>Duplicates the selected rows. Of the selected editable tab.</summary>
    public ICommand DuplicateRowsCommand => _duplicateRows;

    /// <summary>Deletes the selected rows. Of the selected editable tab.</summary>
    public ICommand DeleteRowsCommand => _deleteRows;

    /// <summary>Copies the selected rows to the clipboard. Of the selected table or loc tab, editable or not.</summary>
    public ICommand CopyCommand => _copy;

    /// <summary>Inserts the rows of the clipboard. Of the selected editable tab.</summary>
    public ICommand PasteCommand => _paste;

    /// <summary>Why <see cref="InsertRowCommand"/> cannot run; null when it can.</summary>
    public string? InsertRowReason => ReasonFor(InsertTarget);

    /// <summary>Why <see cref="DuplicateRowsCommand"/> cannot run; null when it can.</summary>
    public string? DuplicateRowsReason => ReasonFor(DuplicateTarget);

    /// <summary>Why <see cref="DeleteRowsCommand"/> cannot run; null when it can.</summary>
    public string? DeleteRowsReason => ReasonFor(DeleteTarget);

    /// <summary>Why <see cref="CopyCommand"/> cannot run; null when it can.</summary>
    public string? CopyReason => ReasonFor(CopyTarget);

    /// <summary>Why <see cref="PasteCommand"/> cannot run; null when it can.</summary>
    public string? PasteReason => ReasonFor(PasteTarget);

    /// <summary>The tool tip of the button: the reason while the command cannot run, else the action with its key.</summary>
    public string InsertRowToolTip => InsertRowReason ?? _texts.ToolbarInsertRowToolTip;

    public string DuplicateRowsToolTip => DuplicateRowsReason ?? _texts.ToolbarDuplicateRowsToolTip;

    public string DeleteRowsToolTip => DeleteRowsReason ?? _texts.ToolbarDeleteRowsToolTip;

    public string CopyToolTip => CopyReason ?? _texts.ToolbarCopyToolTip;

    public string PasteToolTip => PasteReason ?? _texts.ToolbarPasteToolTip;

    // Which command of a tab a button forwards to; null when the tab has none.
    private static ICommand? InsertTarget(TabViewModel? tab) => (tab as EditableRowsTabViewModel)?.InsertRowCommand;

    private static ICommand? DuplicateTarget(TabViewModel? tab) => (tab as EditableRowsTabViewModel)?.DuplicateRowsCommand;

    private static ICommand? DeleteTarget(TabViewModel? tab) => (tab as EditableRowsTabViewModel)?.DeleteRowsCommand;

    private static ICommand? CopyTarget(TabViewModel? tab) => (tab as RowsTabViewModel)?.CopyRowsCommand;

    private static ICommand? PasteTarget(TabViewModel? tab) => (tab as EditableRowsTabViewModel)?.PasteRowsCommand;

    private RelayCommand Forward(Func<TabViewModel?, ICommand?> target) => new(() =>
    {
        var tab = _tabs.SelectedTab;
        if (target(tab) is not { } command || !command.CanExecute(null))
        {
            return;
        }

        // A click on a button that cannot take the focus does not make the grid leave the cell that is being edited, so the edit is committed
        // here, as the context menu of the grid does. A value that cannot be committed keeps the cell in edit mode, and the command waits.
        if (tab is EditableRowsTabViewModel editable && !editable.TryCommitPendingEdit())
        {
            return;
        }

        command.Execute(null);
    }, () => target(_tabs.SelectedTab)?.CanExecute(null) ?? false);

    private string? ReasonFor(Func<TabViewModel?, ICommand?> target)
    {
        var tab = _tabs.SelectedTab;
        if (target(tab) is { } command)
        {
            return command.CanExecute(null) ? null : _texts.ToolbarSelectRows;
        }

        // A table or loc tab that is not editable has copy, but no other row command.
        return tab is RowsTabViewModel ? _texts.ToolbarReadOnly : _texts.ToolbarOpenTable;
    }

    // Stops listening to the commands of the tab that was selected, starts listening to those of the tab that is selected now.
    private void FollowSelectedTab()
    {
        foreach (var command in _followed)
        {
            command.CanExecuteChanged -= OnTabCommandChanged;
        }

        _followed.Clear();
        var tab = _tabs.SelectedTab;
        foreach (var target in new[] { InsertTarget, DuplicateTarget, DeleteTarget, CopyTarget, PasteTarget })
        {
            if (target(tab) is { } command)
            {
                command.CanExecuteChanged += OnTabCommandChanged;
                _followed.Add(command);
            }
        }

        RaiseChanged();
    }

    private void OnTabCommandChanged(object? sender, EventArgs e) => RaiseChanged();

    private void RaiseChanged()
    {
        _insertRow.RaiseCanExecuteChanged();
        _duplicateRows.RaiseCanExecuteChanged();
        _deleteRows.RaiseCanExecuteChanged();
        _copy.RaiseCanExecuteChanged();
        _paste.RaiseCanExecuteChanged();
        foreach (var name in new[]
        {
            nameof(InsertRowReason), nameof(DuplicateRowsReason), nameof(DeleteRowsReason), nameof(CopyReason), nameof(PasteReason),
            nameof(InsertRowToolTip), nameof(DuplicateRowsToolTip), nameof(DeleteRowsToolTip), nameof(CopyToolTip), nameof(PasteToolTip),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
