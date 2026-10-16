using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TotalWarModTool.App.Shell;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

/// <summary>
/// A code-built page of the settings window: a hint, a <see cref="ReorderListBox"/> and a row of buttons. The list is rebuilt when one of its
/// settings changes (from the page or from outside, for example the tile bar), the whole page when the language changes. The page listens to
/// the settings and the texts, which outlive the window, so the window calls <see cref="Detach"/> when it closes.
/// </summary>
public sealed class BarSettingsPage : DockPanel
{
    private readonly SettingsViewModel _viewModel;
    private readonly Func<string> _hint;
    private readonly Func<SettingsViewModel, ReorderListBox> _createList;
    private readonly Func<SettingsViewModel, IEnumerable<Button>> _createButtons;
    private readonly IReadOnlySet<string> _listSettings;

    internal BarSettingsPage(SettingsViewModel viewModel, Func<string> hint, Func<SettingsViewModel, ReorderListBox> createList,
        Func<SettingsViewModel, IEnumerable<Button>> createButtons, IReadOnlySet<string> listSettings)
    {
        _viewModel = viewModel;
        _hint = hint;
        _createList = createList;
        _createButtons = createButtons;
        _listSettings = listSettings;
        List = Build();
        viewModel.Settings.Changed += OnSettingsChanged;
        viewModel.Texts.PropertyChanged += OnTextsChanged;
    }

    /// <summary>The list of the page; a new one after a change of the language.</summary>
    public ReorderListBox List { get; private set; }

    /// <summary>Stops listening to the settings and the texts.</summary>
    public void Detach()
    {
        _viewModel.Settings.Changed -= OnSettingsChanged;
        _viewModel.Texts.PropertyChanged -= OnTextsChanged;
    }

    private ReorderListBox Build()
    {
        Children.Clear();

        var hint = new TextBlock { Text = _hint(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextMuted");
        SetDock(hint, Dock.Top);
        Children.Add(hint);

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var button in _createButtons(_viewModel))
        {
            button.Padding = new Thickness(10, 3, 10, 3);
            button.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(button);
        }

        SetDock(buttons, Dock.Bottom);
        Children.Add(buttons);

        // The scroll viewer is also what the list scrolls while a row is dragged near its edge.
        var list = _createList(_viewModel);
        Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return list;
    }

    private void OnSettingsChanged(object? sender, string property)
    {
        if (_listSettings.Contains(property))
        {
            List.Rebuild();
        }
    }

    private void OnTextsChanged(object? sender, PropertyChangedEventArgs e) => List = Build();
}

/// <summary>Builds the pages "Tile bar" and "Toolbar" of the settings window. Every check box and button writes the settings at once.</summary>
public static class BarSettingsPages
{
    private const double IconSize = 16;

    /// <summary>All tiles in their order, hidden ones included (they keep their place), each with "Visible" and "Icon only".</summary>
    public static BarSettingsPage Tiles(SettingsViewModel viewModel) =>
        new(viewModel, () => viewModel.Texts.SettingsTileOrderHint, TileList,
            vm => [CommandButton("TileResetButton", vm.Texts.SettingsTileReset, vm.ResetTilesCommand)],
            new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(GlobalSettings.TileOrder), nameof(GlobalSettings.HiddenTiles), nameof(GlobalSettings.IconOnlyTiles),
            });

    /// <summary>The toolbar in its order with the separators as rows of their own (removable), each entry with "Visible".</summary>
    public static BarSettingsPage Toolbar(SettingsViewModel viewModel) =>
        new(viewModel, () => viewModel.Texts.SettingsToolbarOrderHint, ToolbarList,
            vm =>
            [
                CommandButton("AddSeparatorButton", vm.Texts.SettingsToolbarAddSeparator, vm.AddToolbarSeparatorCommand),
                CommandButton("ToolbarResetButton", vm.Texts.SettingsToolbarReset, vm.ResetToolbarCommand),
            ],
            new HashSet<string>(StringComparer.Ordinal) { nameof(GlobalSettings.ToolbarOrder), nameof(GlobalSettings.HiddenToolbarItems) });

    private static ReorderListBox TileList(SettingsViewModel viewModel)
    {
        var settings = viewModel.Settings;
        var texts = viewModel.Texts;
        return new ReorderListBox(() => settings.TileOrder, order => settings.TileOrder = order, id =>
        {
            var visible = settings.IsTileVisible(id);
            var iconOnly = Check("IconOnlyCheck", texts.SettingsTileIconOnly, settings.IsTileIconOnly(id), value => settings.SetTileIconOnly(id, value));
            iconOnly.Margin = new Thickness(12, 0, 0, 0);
            var checks = new StackPanel { Orientation = Orientation.Horizontal };
            checks.Children.Add(Check("VisibleCheck", texts.SettingsTileVisible, visible, value => settings.SetTileVisible(id, value)));
            checks.Children.Add(iconOnly);
            return new ReorderRow(id, texts.TileLabel(id), Muted: !visible, Icon: ToolIcons.Create(id, IconSize, "Theme.Text"), Trailing: checks);
        }, ReorderTextsOf(texts));
    }

    private static ReorderListBox ToolbarList(SettingsViewModel viewModel)
    {
        var settings = viewModel.Settings;
        var texts = viewModel.Texts;
        return new ReorderListBox(() => settings.ToolbarOrder, order => settings.ToolbarOrder = order, id =>
        {
            if (ToolbarCatalog.IsSeparator(id))
            {
                return new ReorderRow(id, texts.SettingsToolbarSeparator, IsSeparator: true, Remove: () => viewModel.RemoveToolbarSeparator(id));
            }

            var visible = settings.IsToolbarItemVisible(id);
            return new ReorderRow(id, texts.ToolbarLabel(id), Muted: !visible, Icon: ToolIcons.Create(id, IconSize, "Theme.Text"),
                Trailing: Check("VisibleCheck", texts.SettingsTileVisible, visible, value => settings.SetToolbarItemVisible(id, value)));
        }, ReorderTextsOf(texts));
    }

    private static ReorderTexts ReorderTextsOf(AppTexts texts) => new(texts.ReorderMoveUp, texts.ReorderMoveDown, texts.ReorderRemove);

    // The check box starts with the value of the settings and writes a fixed value per event; its own state is never read.
    private static CheckBox Check(string name, string label, bool isChecked, Action<bool> write)
    {
        var box = new CheckBox { Name = name, Content = label, IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center };
        box.Checked += (_, _) => write(true);
        box.Unchecked += (_, _) => write(false);
        return box;
    }

    private static Button CommandButton(string name, string label, ICommand command) => new() { Name = name, Content = label, Command = command };
}
