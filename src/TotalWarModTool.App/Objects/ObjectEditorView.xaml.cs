using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace TotalWarModTool.App;

/// <summary>
/// The AvalonEdit editor of an object tab. DataContext is the <see cref="ObjectTabViewModel"/>. Pure view wiring: the text goes in both
/// directions, the head line is read-only, the editor has no undo of its own.
/// </summary>
public sealed partial class ObjectEditorView : UserControl
{
    private readonly TextSegmentCollection<TextSegment> _readOnlySegments;
    private ObjectTabViewModel? _tab;
    private bool _settingText;

    public ObjectEditorView()
    {
        InitializeComponent();
        _readOnlySegments = new TextSegmentCollection<TextSegment>(Editor.Document);
        Editor.TextArea.ReadOnlySectionProvider = new TextSegmentReadOnlySectionProvider<TextSegment>(_readOnlySegments);
        Editor.TextArea.SetResourceReference(TextArea.SelectionBrushProperty, "Theme.Selection");

        // Ctrl+Z and Ctrl+Y belong to the global history, which the main window binds: the editor must not take them.
        Editor.Document.UndoStack.SizeLimit = 0;
        foreach (var binding in Editor.TextArea.CommandBindings.Cast<CommandBinding>()
                     .Where(binding => binding.Command == ApplicationCommands.Undo || binding.Command == ApplicationCommands.Redo).ToList())
        {
            Editor.TextArea.CommandBindings.Remove(binding);
        }

        foreach (var binding in Editor.TextArea.InputBindings.Cast<InputBinding>()
                     .Where(binding => binding.Command == ApplicationCommands.Undo || binding.Command == ApplicationCommands.Redo).ToList())
        {
            Editor.TextArea.InputBindings.Remove(binding);
        }

        Editor.TextChanged += OnEditorTextChanged;
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>The editor, for tests and for Plan 3b (colorizer, element generator, folding).</summary>
    internal TextEditor Editor => TextBox;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_tab is not null)
        {
            _tab.PropertyChanged -= OnTabPropertyChanged;
        }

        _tab = e.NewValue as ObjectTabViewModel;
        if (_tab is not null)
        {
            _tab.PropertyChanged += OnTabPropertyChanged;
        }

        ShowTabText();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ObjectTabViewModel.Text))
        {
            ShowTabText();
        }
        else if (e.PropertyName == nameof(ObjectTabViewModel.HeadLineLength))
        {
            SetReadOnlyHead();
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (!_settingText && _tab is not null)
        {
            _tab.Text = Editor.Text;
        }
    }

    private void ShowTabText()
    {
        var text = _tab?.Text ?? string.Empty;
        if (!string.Equals(Editor.Text, text, StringComparison.Ordinal))
        {
            _settingText = true;
            try
            {
                Editor.Text = text;
            }
            finally
            {
                _settingText = false;
            }
        }

        SetReadOnlyHead();
    }

    // The head line [0, HeadLineLength] cannot be edited inside; inserting at its end stays allowed so that the user can begin a new line there.
    private void SetReadOnlyHead()
    {
        _readOnlySegments.Clear();
        var length = Math.Min(_tab?.HeadLineLength ?? 0, Editor.Document.TextLength);
        if (length > 0)
        {
            _readOnlySegments.Add(new TextSegment { StartOffset = 0, Length = length });
        }
    }
}
