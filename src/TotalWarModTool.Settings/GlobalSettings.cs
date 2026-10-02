namespace TotalWarModTool.Settings;

public enum ThemeChoice { System, Light, Dark }

/// <summary>
/// Global (per user, not per project) settings. Every setting is changed through its property, which raises <see cref="Changed"/>
/// only when the value really changes, so the UI and other services can react immediately.
/// </summary>
public sealed class GlobalSettings
{
    private const string DefaultLanguage = "en";

    private string _language = DefaultLanguage;
    private ThemeChoice _theme = ThemeChoice.System;
    private WindowPlacement? _mainWindow;

    /// <summary>Argument: the name of the changed property.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>Language code. The setter trims and lower-cases it; a blank value becomes <see cref="DefaultLanguage"/>.</summary>
    public string Language
    {
        get => _language;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? DefaultLanguage : value.Trim().ToLowerInvariant();
            if (normalized == _language)
            {
                return;
            }

            _language = normalized;
            Changed?.Invoke(this, nameof(Language));
        }
    }

    public ThemeChoice Theme
    {
        get => _theme;
        set
        {
            if (value == _theme)
            {
                return;
            }

            _theme = value;
            Changed?.Invoke(this, nameof(Theme));
        }
    }

    public WindowPlacement? MainWindow
    {
        get => _mainWindow;
        set
        {
            if (Equals(value, _mainWindow))
            {
                return;
            }

            _mainWindow = value;
            Changed?.Invoke(this, nameof(MainWindow));
        }
    }
}