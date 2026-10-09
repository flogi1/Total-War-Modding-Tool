using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TotalWarModTool.App;

// The editing side of the grid: the state of a cell in edit mode, the commit for the actions of the app, the keys, and the link to the view model
// of the data context. All of it is view wiring: the actions themselves are the commands of the view model.
public sealed partial class TableGrid
{
    private readonly Func<bool> _commitPendingEdit;
    private readonly Action _cancelPendingEdit;
    private readonly EventHandler<IReadOnlyList<int>> _selectionRequested;

    // The view model this grid is linked to (while the grid is loaded), else null.
    private RowsTabViewModel? _attached;
    private bool _isEditing;
    private bool _dirty;
    private bool _userCancel;
    private TextBox? _editBox;
    private string? _typed;
    private EditRow? _editRow;
    private int? _editField;

    private void InitializeEditing()
    {
        Validation.AddErrorHandler(this, OnValidationError);
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        IsVisibleChanged += OnVisibleChanged;
    }

    // ----- the link to the view model -----

    // Links the grid to the view model of its data context: the selection goes to it, the edit commit and the requests for a selection come
    // from it. One grid shows several tabs one after another (the data context changes), so the old link is cut first.
    private void Attach()
    {
        var model = IsLoaded ? DataContext as RowsTabViewModel : null;
        if (ReferenceEquals(model, _attached))
        {
            return;
        }

        Detach();
        if (model is null)
        {
            return;
        }

        _attached = model;
        if (model is EditableRowsTabViewModel editable)
        {
            editable.CommitPendingEdit = _commitPendingEdit;
            editable.CancelPendingEdit = _cancelPendingEdit;
        }

        model.SelectionRequested += _selectionRequested;
        model.SelectedRows = IndexesOfSelection();
    }

    // Cuts the link. An edit that is still going on cannot be shown any more: its value is committed, and only a value that cannot be
    // committed is dropped, with a notice (the tab list does not let the tab go while its cell holds such a value, so this is the way
    // of a tab that is closed or replaced by the app). The view model forgets the selection, which this grid no longer shows.
    private void Detach()
    {
        if (_attached is not { } model)
        {
            return;
        }

        LeaveEdit();
        _attached = null;
        if (model is EditableRowsTabViewModel editable)
        {
            if (editable.CommitPendingEdit == _commitPendingEdit)
            {
                editable.CommitPendingEdit = null;
            }

            if (editable.CancelPendingEdit == _cancelPendingEdit)
            {
                editable.CancelPendingEdit = null;
            }

            editable.SetPendingEdit(false);
        }

        model.SelectionRequested -= _selectionRequested;
        model.SelectedRows = [];
    }

    // The text view hides the grid (the switch commits first and refuses an invalid value, so this is a second line).
    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
        {
            LeaveEdit();
        }
    }

    // ----- the cell in edit mode -----

    protected override void OnBeginningEdit(DataGridBeginningEditEventArgs e)
    {
        base.OnBeginningEdit(e);
        if (!e.Cancel)
        {
            // What is edited, kept for the moments in which the cell cannot be asked any more (the grid is being removed).
            _editRow = e.Row.Item as EditRow;
            _editField = (e.Column as GridTextColumn)?.FieldIndex;
            SetEditing(true);
        }
    }

    // Commit or cancel: the cell leaves edit mode. A commit that then fails on the rule keeps the cell in edit mode, which the error of the
    // binding reports (OnValidationError), so the state is right in both cases.
    protected override void OnCellEditEnding(DataGridCellEditEndingEventArgs e)
    {
        base.OnCellEditEnding(e);
        if (!e.Cancel)
        {
            // A cancel that the user did not ask for (Esc) is the grid dropping the edit itself: the rows changed under it, or it is being
            // removed from the screen. The typed text is written to its row if the row is still there, else the user is told.
            if (e.EditAction == DataGridEditAction.Cancel && !_userCancel)
            {
                SaveTypedText(e);
            }

            _userCancel = false;
            SetEditing(false);
        }
    }

    // Writes what is typed into the editor of the cell to its row; a text that is not valid, or a row that is gone, gives the notice.
    private void SaveTypedText(DataGridCellEditEndingEventArgs e)
    {
        if (!_dirty || _attached is not EditableRowsTabViewModel editable)
        {
            return;
        }

        if (_typed is null || e.Row.Item is not EditRow row || e.Column is not GridTextColumn column
            || !editable.TryApplyText(row, column.FieldIndex, _typed))
        {
            editable.NoteDiscardedEdit();
        }
    }

    // Whether a cell is in edit mode (the keys belong to its editor); leaving edit mode ends the change that was pending.
    private void SetEditing(bool editing)
    {
        _isEditing = editing;
        if (!editing)
        {
            SetDirty(false);
        }
    }

    // The text of the editor changed. The last text that differed from the cell is kept: when the grid goes away the editor gets its binding
    // back to the row, and its text with it.
    private void TextEdited(TextBox box, bool dirty)
    {
        _editBox = box;
        if (dirty)
        {
            _typed = box.Text;
        }

        SetDirty(dirty);
    }

    // Whether the text of the cell in edit mode differs from the value of the cell: only then the tab has a change that is not in the
    // document (Save is enabled, a close asks).
    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        if (_attached is EditableRowsTabViewModel editable)
        {
            editable.SetPendingEdit(dirty);
        }
    }

    // A value that the rule or the setter refuses: the cell stays in edit mode, and the message shows under the header of the tab. The
    // message is worked out again from the typed text when the language changes.
    private void OnValidationError(object? sender, ValidationErrorEventArgs e)
    {
        if (_attached is not EditableRowsTabViewModel editable)
        {
            return;
        }

        if (e.Action == ValidationErrorEventAction.Added)
        {
            _isEditing = true;
            SetDirty(true);
            var given = e.Error.ErrorContent?.ToString();
            if (e.OriginalSource is TextBox box && FindAncestor<DataGridCell>(box)?.Column is GridTextColumn column)
            {
                var typed = box.Text;
                editable.SetValidationCheck(() => editable.CheckCell(column.FieldIndex, typed) ?? given);
            }
            else
            {
                editable.SetValidationMessage(given);
            }
        }
        else
        {
            editable.SetValidationMessage(null);
        }
    }

    // The typed text is written to the document when it can be; a text that cannot be (it is not a value of the column) is dropped and the
    // user is told. For the moments in which the grid cannot keep the edit (it goes away, its columns change). The grid itself is asked to
    // commit first; but one that is going away reports the commit as done or as failed without writing the text (its editor is cut off
    // from the row), so the text that was kept is written by the tab, which is no change when the commit has written it.
    private void LeaveEdit()
    {
        if (!_isEditing)
        {
            return;
        }

        var typed = _dirty ? _typed : null;
        var row = _editRow;
        var column = _editField;
        var committed = CommitEdit(DataGridEditingUnit.Row, true);
        var written = typed is not null && _attached is EditableRowsTabViewModel editable
            && row is not null && column is { } index && editable.TryApplyText(row, index, typed);
        if (!committed)
        {
            // Still in edit mode: the cell goes back to its shown state. The notice below (if any) is the tab's, so the cancel is not told twice.
            if (typed is not null && !written)
            {
                (_attached as EditableRowsTabViewModel)?.NoteDiscardedEdit();
            }

            _userCancel = true;
            CancelEdit(DataGridEditingUnit.Row);
            _userCancel = false;
        }
        else if (typed is not null && !written)
        {
            (_attached as EditableRowsTabViewModel)?.NoteDiscardedEdit();
        }

        SetEditing(false);
    }

    // The app replaces the document on purpose (the user chose "Reload"): the cell goes back to its shown state, and its text is written
    // nowhere and no notice is set, so that this editor cannot write into the new document. The flag keeps the end of the edit from saving the text.
    private void DiscardEdit()
    {
        if (!_isEditing)
        {
            return;
        }

        _userCancel = true;
        try
        {
            CancelEdit(DataGridEditingUnit.Row);
        }
        finally
        {
            _userCancel = false;
        }

        SetEditing(false);
    }

    // Tells the tab that typed text was dropped (only if there was a change to lose).
    private void NoteDiscard()
    {
        if (_dirty && _attached is EditableRowsTabViewModel editable)
        {
            editable.NoteDiscardedEdit();
        }
    }

    // A check box has been toggled: the edit of that cell is over. Later, not inside the event of the toggle.
    private void CommitAfterToggle() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_isEditing)
            {
                CommitPendingEdit();
            }
        });

    // Writes the cell that is being edited to the document. True when nothing was being edited or the commit worked; false when the value is
    // not valid and the cell stays in edit mode, then the focus goes back into it.
    private bool CommitPendingEdit()
    {
        if (CommitEdit(DataGridEditingUnit.Row, true))
        {
            SetEditing(false);
            return true;
        }

        FindEditor()?.Focus();
        return false;
    }

    // The editing element of the current cell, null when no cell is in edit mode.
    private FrameworkElement? FindEditor() =>
        CurrentColumn is { } column && CurrentItem is { } item && column.GetCellContent(item) is { } content && ElementIsEditor(content) ? content : null;

    private static bool ElementIsEditor(FrameworkElement content) => content is TextBox or CheckBox { IsHitTestVisible: true };

    // ----- keys and the mouse -----

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape && _isEditing)
        {
            _userCancel = true;                                                             // the user discards the edit: no notice
        }

        if (HandleKey(key, Keyboard.Modifiers, _isEditing))
        {
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>
    /// What a key does in the grid; true when the grid handled it. While a cell is in edit mode the keys belong to its editor, except undo and
    /// redo: the editor undoes its own typing, and the key never reaches the global undo of the window, which would undo an older step while the
    /// user is typing (also when the editor has nothing to undo). Otherwise: Insert, Delete and Ctrl+D (editable grids), Ctrl+C everywhere,
    /// Ctrl+V (editable grids). The command is run when it is enabled; the key counts as handled either way.
    /// </summary>
    internal bool HandleKey(Key key, ModifierKeys modifiers, bool isEditing)
    {
        if (isEditing)
        {
            if (modifiers == ModifierKeys.Control && key is Key.Z or Key.Y)
            {
                var box = FindEditor() as TextBox ?? Keyboard.FocusedElement as TextBox;
                if (key == Key.Z && box is { CanUndo: true })
                {
                    box.Undo();
                }
                else if (key == Key.Y && box is { CanRedo: true })
                {
                    box.Redo();
                }

                return true;
            }

            return false;
        }

        if (DataContext is not RowsTabViewModel model)
        {
            return false;
        }

        var editable = model as EditableRowsTabViewModel;
        switch (key, modifiers)
        {
            case (Key.C, ModifierKeys.Control):
                Run(model.CopyRowsCommand);
                return true;
            case (Key.V, ModifierKeys.Control) when editable is not null:
                Run(editable.PasteRowsCommand);
                return true;
            case (Key.D, ModifierKeys.Control) when editable is not null:
                Run(editable.DuplicateRowsCommand);
                return true;
            case (Key.Insert, ModifierKeys.None) when editable is not null:
                Run(editable.InsertRowCommand);
                return true;
            case (Key.Delete, ModifierKeys.None) when editable is not null:
                Run(editable.DeleteRowsCommand);
                return true;
            default:
                return false;
        }
    }

    private static void Run(ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    // The menu acts on the document, so a cell that is being edited is committed first (the menu of the editor itself handles its own event and
    // never gets here). A value that cannot be committed keeps the menu closed and the editor in place.
    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        if (!PrepareContextMenu())
        {
            e.Handled = true;
        }

        base.OnContextMenuOpening(e);
    }

    /// <summary>True when the context menu may open: no cell is in edit mode, or its value was committed. False: the cell holds an invalid value.</summary>
    internal bool PrepareContextMenu() => !_isEditing || CommitPendingEdit();

    // A right click on a row that is not selected selects that row alone, so that the menu acts on what the user points at. While a cell is
    // in edit mode a click on that cell is for its editor (its own menu); a click anywhere else commits the cell first (an invalid value
    // keeps the click from doing anything), then selects the row under the mouse.
    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (_isEditing && FindAncestor<DataGridCell>(source) is { IsEditing: true })
        {
            base.OnPreviewMouseRightButtonDown(e);
            return;
        }

        if (_isEditing && !CommitPendingEdit())
        {
            e.Handled = true;
            return;
        }

        if (FindAncestor<DataGridRow>(source) is { IsSelected: false } row)
        {
            UnselectAll();
            row.IsSelected = true;
        }

        base.OnPreviewMouseRightButtonDown(e);
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null and not T)
        {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return element as T;
    }
}
