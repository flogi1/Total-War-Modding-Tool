using System.ComponentModel;

namespace TotalWarModTool.Localization;

/// <summary>
/// Base class for a module's text class: one property (or method with arguments) per UI text, each reading through <see cref="T"/> or <see cref="F"/>.
/// Remembers every requested key with its inline fallback so <see cref="LocaleCheck"/> can compare the locale files against the code.
/// Raises <see cref="PropertyChanged"/> for all properties when the language is loaded again, so bound views refresh.
/// </summary>
public abstract class TextsBase : INotifyPropertyChanged
{
    private readonly UiText _ui;
    private readonly Dictionary<string, string> _requestedKeys = new(StringComparer.Ordinal);

    protected TextsBase(UiText ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;
        _ui.Changed += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Key to inline fallback of every text read so far; filled as the texts are read.</summary>
    public IReadOnlyDictionary<string, string> RequestedKeys => _requestedKeys;

    protected string T(string key, string fallback)
    {
        _requestedKeys[key] = fallback;
        return _ui.Get(key, fallback);
    }

    protected string F(string key, string fallback, params object?[] args)
    {
        _requestedKeys[key] = fallback;
        return _ui.Format(key, fallback, args);
    }
}
