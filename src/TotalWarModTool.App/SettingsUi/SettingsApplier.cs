using TotalWarModTool.Localization;
using TotalWarModTool.Logging;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

/// <summary>
/// Applies changes of the settings at once: a new language reloads the texts (bound windows refresh themselves), a new theme repaints.
/// Create it after the start values were applied; from then on it follows the settings for the whole run.
/// </summary>
public sealed class SettingsApplier
{
    private readonly GlobalSettings _settings;
    private readonly UiText _ui;
    private readonly ThemeManager _theme;
    private readonly IAppLog _log;

    public SettingsApplier(GlobalSettings settings, UiText ui, ThemeManager theme, IAppLog log)
    {
        _settings = settings;
        _ui = ui;
        _theme = theme;
        _log = log;
        settings.Changed += OnChanged;
    }

    private void OnChanged(object? sender, string property)
    {
        switch (property)
        {
            case nameof(GlobalSettings.Language):
                LanguageLoader.Load(_ui, _settings.Language, _log);
                break;
            case nameof(GlobalSettings.Theme):
                _theme.Apply(_settings.Theme);
                break;
        }
    }
}
