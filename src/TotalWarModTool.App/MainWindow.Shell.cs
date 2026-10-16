using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TotalWarModTool.App.Shell;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

// The tile bar and the small toolbar: only the wiring of the shell controls to the view model and the settings. What each entry runs and
// says comes from the view model (ToolbarBindingFor, ToolbarToolTip, Wizards); how the bars look comes from the shell controls.
public partial class MainWindow
{
    private static readonly string[] ToolbarRebuildTriggers =
    [
        nameof(GlobalSettings.ToolbarOrder), nameof(GlobalSettings.HiddenToolbarItems), nameof(GlobalSettings.ToolbarPosition),
        nameof(GlobalSettings.ToolbarLabelsVisible),
    ];

    // One button per toolbar id the view model has a command for, created once; every build places these same buttons.
    private readonly Dictionary<string, Button> _toolbarPool = new(StringComparer.Ordinal);
    private MainViewModel _shell = null!;
    private GlobalSettings _settings = null!;
    private ToolbarPositionGrip _grip = null!;

    // The wizards whose commands the tile bar listens to; the composition root may set them after the window exists.
    private ObjectWizardController? _wiredWizards;

    private void InitializeShell(MainViewModel viewModel, GlobalSettings settings)
    {
        _shell = viewModel;
        _settings = settings;
        var texts = viewModel.Texts;

        TileBarControl.Bind(settings, new TileSource(texts.TileLabel, texts.TileTip, _ => viewModel.HasWizards, IsTileEnabled, TileDisabledTip, _ => null));
        TileBarControl.TileClicked += (_, id) => TileCommand(id)?.Execute(null);
        WireWizards();

        foreach (var id in ToolbarCatalog.All)
        {
            if (viewModel.ToolbarBindingFor(id) is { } binding)
            {
                _toolbarPool[id] = CreateToolbarButton(id, binding);
            }
        }

        _grip = new ToolbarPositionGrip(() => settings.ToolbarPosition, texts.ToolbarPositionLabel, texts.ToolbarPositionTitle);
        _grip.PositionChosen += (_, position) => settings.ToolbarPosition = position;
        ApplyToolbarPosition();

        settings.Changed += OnShellSettingsChanged;
        viewModel.PropertyChanged += OnShellViewModelChanged;
        texts.PropertyChanged += (_, _) => OnShellTextsChanged();
    }

    /// <summary>Builds the toolbar from the pool in the order and with the visibility of the settings; vertical at the left and the right, without labels there.</summary>
    private void BuildToolbar()
    {
        var vertical = _settings.ToolbarPosition is ToolbarPosition.Left or ToolbarPosition.Right;
        var showLabel = !vertical && _settings.ToolbarLabelsVisible;
        foreach (var button in _toolbarPool.Values)
        {
            if (ToolbarItemView.LabelOf((FrameworkElement)button.Content) is { } label)
            {
                label.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        var panel = ToolbarBuilder.Build(new ToolbarBuild(_settings.ToolbarOrder,
            id => ToolbarCatalog.IsSeparator(id) || _settings.IsToolbarItemVisible(id), id => _toolbarPool.GetValueOrDefault(id), vertical, _grip,
            MoveToolbarItem));
        ToolbarHost.Child = vertical
            ? new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = panel }
            : panel;
    }

    /// <summary>Docks the toolbar at the side of the settings, with its border towards the work area, and builds it.</summary>
    private void ApplyToolbarPosition()
    {
        var (dock, border) = _settings.ToolbarPosition switch
        {
            ToolbarPosition.Left => (Dock.Left, new Thickness(0, 0, 1, 0)),
            ToolbarPosition.Right => (Dock.Right, new Thickness(1, 0, 0, 0)),
            ToolbarPosition.Bottom => (Dock.Bottom, new Thickness(0, 1, 0, 0)),
            _ => (Dock.Top, new Thickness(0, 0, 0, 1)),
        };
        DockPanel.SetDock(ToolbarHost, dock);
        ToolbarHost.BorderThickness = border;
        ToolbarHost.Padding = new Thickness(2);
        BuildToolbar();
    }

    private Button CreateToolbarButton(string id, ToolbarBinding binding)
    {
        // Not focusable: the grid keeps its selection and its keys after a click.
        var button = new Button
        {
            Content = ToolbarItemView.Content(id, binding.Label, showLabel: true),
            Command = binding.Command,
            Focusable = false,
            Tag = id,
            ToolTip = _shell.ToolbarToolTip(id),
        };
        ToolTipService.SetShowOnDisabled(button, true);
        return button;
    }

    private void MoveToolbarItem(string dragged, string target)
    {
        var order = _settings.ToolbarOrder.ToList();
        if (ReorderList.MoveNextTo(order, dragged, target))
        {
            _settings.ToolbarOrder = order;
        }
    }

    private void UpdateToolbarToolTips()
    {
        foreach (var (id, button) in _toolbarPool)
        {
            button.ToolTip = _shell.ToolbarToolTip(id);
        }
    }

    private ICommand? TileCommand(string id) => id switch
    {
        TileIds.NewBuilding => _shell.Wizards?.NewBuildingCommand,
        TileIds.NewTrait => _shell.Wizards?.NewTraitCommand,
        _ => null,
    };

    private bool IsTileEnabled(string id) => TileCommand(id)?.CanExecute(null) ?? false;

    private string? TileDisabledTip(string id) => id switch
    {
        TileIds.NewBuilding => _shell.Wizards?.NewBuildingDisabledReason,
        TileIds.NewTrait => _shell.Wizards?.NewTraitDisabledReason,
        _ => null,
    };

    // Moves the listening from the commands of the old wizards to those of the new ones and rebuilds the tiles (offered only with wizards).
    private void WireWizards()
    {
        if (_wiredWizards is { } old)
        {
            old.NewBuildingCommand.CanExecuteChanged -= OnWizardStateChanged;
            old.NewTraitCommand.CanExecuteChanged -= OnWizardStateChanged;
        }

        _wiredWizards = _shell.Wizards;
        if (_wiredWizards is { } wizards)
        {
            wizards.NewBuildingCommand.CanExecuteChanged += OnWizardStateChanged;
            wizards.NewTraitCommand.CanExecuteChanged += OnWizardStateChanged;
        }

        TileBarControl.Rebuild();
        ApplyTileBarVisibility();
    }

    private void OnWizardStateChanged(object? sender, EventArgs e) => TileBarControl.RefreshState();

    private void ApplyTileBarVisibility() =>
        TileBarControl.Visibility = _settings.TileBarVisible && _shell.HasWizards ? Visibility.Visible : Visibility.Collapsed;

    private void OnShellSettingsChanged(object? sender, string name)
    {
        if (name == nameof(GlobalSettings.TileBarVisible))
        {
            ApplyTileBarVisibility();
        }
        else if (name == nameof(GlobalSettings.ToolbarPosition))
        {
            ApplyToolbarPosition();
        }
        else if (ToolbarRebuildTriggers.Contains(name, StringComparer.Ordinal))
        {
            BuildToolbar();
        }
    }

    // The reasons of the toolbar live in properties of the view model, so any change of it may change a tool tip.
    private void OnShellViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Wizards) or null)
        {
            WireWizards();
        }

        UpdateToolbarToolTips();
    }

    private void OnShellTextsChanged()
    {
        foreach (var (id, button) in _toolbarPool)
        {
            ToolbarItemView.SetLabel((FrameworkElement)button.Content, _shell.Texts.ToolbarLabel(id));
        }

        UpdateToolbarToolTips();
        _grip.ToolTip = _shell.Texts.ToolbarPositionTitle;
        TileBarControl.Rebuild();
    }
}
