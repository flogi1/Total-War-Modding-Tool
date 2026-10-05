using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TotalWarModTool.App;

// The editing side of the grid: the state of a cell in edit mode, the commit for the actions of the app, the keys, and the link to the view model
// of the data context. All of it is view wiring: the actions themselves are the commands of the view model.
public sealed partial class TableGrid
{
    private readonly Func<bool> _commitPendingEdit;
    private readonly EventHandler<IReadOnlyList<int>> _selectionRequested;

    // The view model this grid is linked to (while the grid is loaded), else null.
    private RowsTabViewModel? _attached;
    private bool _isEditing;

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
            editable.SelectionRequested += _selectionRequested;
        }

        model.SelectedRows = IndexesOfSelection();
    }

    // Cuts the link. An edit that is still going on cannot be shown any more and is dropped (a valid one was committed when the focus left),
    // and the view model forgets the selection, which this grid no longer shows.
    private void Detach()
    {
        if (_attached is not { } model)
        {
            return;
        }

        CancelPendingEdit();
        _attached = null;
        if (model is EditableRowsTabViewModel editable)
        {
            if (editable.CommitPendingEdit == _commitPendingEdit)
            {
                editable.CommitPendingEdit = null;
            }

            editable.SelectionRequested -= _selectionRequested;
            editable.SetPendingEdit(false);
        }

        model.SelectedRows = [];
    }

    // The text view hides the grid: a cell that is still being edited is committed, or dropped when its value is not valid.
    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false && !CommitPendingEdit())
        {
            CancelPendingEdit();
        }
    }

    // ----- the cell in edit mode -----

    protected override void OnBeginningEdit(DataGridBeginningEditEventArgs e)
    {
        base.OnBeginningEdit(e);
        if (!e.Cancel)
        {
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
            SetEditing(false);
        }
    }

    private void SetEditing(bool editing)
    {
        _isEditing = editing;
        if (_attached is EditableRowsTabViewModel editable)
        {
            editable.SetPendingEdit(editing);
        }
    }

    // A value that the rule or the setter refuses: the cell stays in edit mode, and the message shows under the header of the tab.
    private void OnValidationError(object? sender, ValidationErrorEventArgs e)
    {
        if (_attached is not EditableRowsTabViewModel editable)
        {
            return;
        }

        if (e.Action == ValidationErrorEventAction.Added)
        {
            SetEditing(true);
            editable.SetValidationMessage(e.Error.ErrorContent?.ToString());
        }
        else
        {
            editable.SetValidationMessage(null);
        }
    }

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

    private void CancelPendingEdit()
    {
        CancelEdit(DataGridEditingUnit.Row);
        SetEditing(false);
    }

    // The editing element of the current cell, null when no cell is in edit mode.
    private FrameworkElement? FindEditor() =>
        CurrentColumn is { } column && CurrentItem is { } item && column.GetCellContent(item) is { } content && ElementIsEditor(content) ? content : null;

    private static bool ElementIsEditor(FrameworkElement content) => content is TextBox or CheckBox { IsHitTestVisible: true };

    // ----- keys and the mouse -----

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
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

    // A right click on a row that is not selected selects that row alone, so that the menu acts on what the user points at.
    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (!_isEditing && FindRow(e.OriginalSource as DependencyObject) is { IsSelected: false } row)
        {
            UnselectAll();
            row.IsSelected = true;
        }

        base.OnPreviewMouseRightButtonDown(e);
    }

    private static DataGridRow? FindRow(DependencyObject? element)
    {
        while (element is not null and not DataGridRow)
        {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return element as DataGridRow;
    }
}
