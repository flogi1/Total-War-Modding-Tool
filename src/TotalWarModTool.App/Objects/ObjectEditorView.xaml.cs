using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>
/// The AvalonEdit editor of an object tab. DataContext is the <see cref="ObjectTabViewModel"/>. Pure view wiring: the text goes in both
/// directions, the head line is read-only, the editor has no undo of its own.
/// </summary>
public sealed partial class ObjectEditorView : UserControl
{
    /// <summary>How long the text must rest before the check runs.</summary>
    internal static readonly TimeSpan CheckDelay = TimeSpan.FromMilliseconds(300);

    private readonly DispatcherTimer _checkTimer;
    private readonly TextSegmentCollection<TextSegment> _readOnlySegments;
    private ObjectTabViewModel? _tab;
    private BlockLineMap? _map;
    private readonly DiagnosticRenderer _diagnostics;
    private readonly FoldingManager _foldings;
    private bool _updatingFoldings;
    private readonly ToolTip _hoverToolTip = new();
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

        Editor.TextArea.TextView.LineTransformers.Add(new BlockColorizer(() => _map, key => Editor.TryFindResource(key) as Brush));
        _diagnostics = new DiagnosticRenderer(() => _tab?.Diagnostics ?? [], key => Editor.TryFindResource(key) as Brush);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_diagnostics);
        Editor.TextArea.PreviewMouseDown += OnPreviewMouseDown;
        Editor.TextArea.PreviewMouseMove += OnPreviewMouseMove;
        Editor.TextArea.PreviewKeyUp += (_, _) => ShowLinkCursor(false);
        Editor.TextArea.TextView.MouseLeave += (_, _) => ShowLinkCursor(false);
        Editor.TextArea.TextView.MouseHover += OnMouseHover;
        Editor.TextArea.TextView.MouseHoverStopped += (_, _) => _hoverToolTip.IsOpen = false;
        Editor.TextArea.TextView.ElementGenerators.Add(new TrailingGenerator(() => _map, () => _tab, key => Editor.TryFindResource(key) as Brush));

        _checkTimer = new DispatcherTimer { Interval = CheckDelay };
        _checkTimer.Tick += (_, _) =>
        {
            _checkTimer.Stop();
            _tab?.Check();
        };

        _foldings = FoldingManager.Install(Editor.TextArea);
        Editor.TextArea.TextView.VisualLinesChanged += (_, _) => RememberFoldState();

        Editor.TextChanged += OnEditorTextChanged;
        DataContextChanged += OnDataContextChanged;

        // The tab becomes visible: typing can start at once.
        Loaded += (_, _) => Editor.Focus();
    }

    /// <summary>The editor, for tests and for Plan 3b (colorizer, element generator, folding).</summary>
    internal TextEditor Editor => TextBox;

    /// <summary>The foldings of the long sections, for tests.</summary>
    internal FoldingManager Foldings => _foldings;

    /// <summary>The underlines of the diagnostics, for tests.</summary>
    internal DiagnosticRenderer Diagnostics => _diagnostics;

    /// <summary>The tool tip text at a point of the text view (view coordinates), or null.</summary>
    internal string? ToolTipAt(Point viewPoint)
    {
        var textView = Editor.TextArea.TextView;
        if (_tab is null || textView.GetPositionFloor(viewPoint + textView.ScrollOffset) is not { } position)
        {
            return null;
        }

        return EditorHover.ToolTipAt(_tab, _map, position.Line, position.Column - 1, _tab.Texts);
    }

    /// <summary>
    /// Opens the target of the Link span under the point (view coordinates of the text view) in the tab, or in a new tab; false when the point is not
    /// on a link of an intact line.
    /// </summary>
    internal bool TryOpenLinkAt(Point viewPoint, bool newTab)
    {
        if (LinkAt(viewPoint) is not { Target: { } target })
        {
            return false;
        }

        _tab!.NavigateTo(target, newTab);
        return true;
    }

    private TextSpan? LinkAt(Point viewPoint)
    {
        var textView = Editor.TextArea.TextView;
        return _tab is not null && textView.GetPositionFloor(viewPoint + textView.ScrollOffset) is { } position
            ? EditorHover.LinkAt(_map, position.Line, position.Column - 1)
            : null;
    }

    // Ctrl+click follows a link (Shift: in a new tab), the middle button opens it in a new tab; a click without Ctrl only places the caret.
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(Editor.TextArea.TextView);
        var open = e.ChangedButton switch
        {
            MouseButton.Left when Keyboard.Modifiers.HasFlag(ModifierKeys.Control) => TryOpenLinkAt(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)),
            MouseButton.Middle => TryOpenLinkAt(point, true),
            _ => false,
        };
        if (open)
        {
            e.Handled = true;
        }
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e) =>
        ShowLinkCursor(Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && LinkAt(e.GetPosition(Editor.TextArea.TextView)) is not null);

    // The cursor of the text view wins over the one the text area sets, so the hand stays while Ctrl is held over a link.
    private void ShowLinkCursor(bool show)
    {
        if (show)
        {
            Editor.TextArea.TextView.Cursor = Cursors.Hand;
        }
        else
        {
            Editor.TextArea.TextView.ClearValue(CursorProperty);
        }
    }

    private void OnMouseHover(object? sender, MouseEventArgs e)
    {
        if (ToolTipAt(e.GetPosition(Editor.TextArea.TextView)) is { } text)
        {
            _hoverToolTip.Content = text;
            _hoverToolTip.PlacementTarget = Editor.TextArea.TextView;
            _hoverToolTip.IsOpen = true;
            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _checkTimer.Stop();
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
        RebuildMap();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ObjectTabViewModel.Text))
        {
            ShowTabText();
        }
        else if (e.PropertyName == nameof(ObjectTabViewModel.Block))
        {
            RebuildMap();
        }
        else if (e.PropertyName == nameof(ObjectTabViewModel.Diagnostics))
        {
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
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
            _checkTimer.Stop();
            _checkTimer.Start();
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

            _checkTimer.Stop();
            RebuildMap();
        }

        SetReadOnlyHead();
    }

    // The map belongs to the baseline block. A document with unapplied text (the tab is shown again) is matched to the block line by line;
    // typing keeps the map (anchors follow).
    private void RebuildMap()
    {
        _map?.Dispose();
        _map = _tab?.Block is { } block ? BlockLineMap.Create(Editor.Document, block) : null;
        UpdateFoldings();
        Editor.TextArea.TextView.Redraw();
    }

    // One folding per long section of the block, folded unless the tab remembers the section as unfolded. They follow the text as segments.
    private void UpdateFoldings()
    {
        var foldings = new List<(SectionFolding Folding, NewFolding New)>();
        if (_map is not null && _tab?.Block is { } block && Editor.Document.TextLength > 0)
        {
            var document = Editor.Document;
            var map = _map;
            foreach (var folding in BlockFolding.Compute(block, line => map.DocumentLineOf(line) is { } number ? document.GetLineByNumber(number).EndOffset : null))
            {
                foldings.Add((folding, new NewFolding(folding.StartOffset, folding.EndOffset) { Name = _tab.Texts.ObjectsMoreRows(folding.HiddenRows) }));
            }
        }

        _updatingFoldings = true;
        try
        {
            _foldings.UpdateFoldings(foldings.Select(f => f.New), -1);
            foreach (var section in _foldings.AllFoldings.ToList())
            {
                var folding = foldings.FirstOrDefault(f => f.Folding.StartOffset == section.StartOffset).Folding;
                section.Tag = folding?.Section;
                section.IsFolded = folding is null || _tab?.ExpandedSections.Contains(folding.Section) != true;
            }
        }
        finally
        {
            _updatingFoldings = false;
        }
    }

    // The user folded or unfolded a section: the tab remembers it, so that it survives a rebuild after an apply.
    private void RememberFoldState()
    {
        if (_updatingFoldings || _tab is null)
        {
            return;
        }

        foreach (var section in _foldings.AllFoldings)
        {
            if (section.Tag is int index && _tab.ExpandedSections.Contains(index) == section.IsFolded)
            {
                _tab.SetSectionExpanded(index, !section.IsFolded);
            }
        }
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
