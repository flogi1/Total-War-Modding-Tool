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

    // Texts are read from any thread (UI and Task.Run work), so every access to the dictionary happens under this lock.
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _requestedKeys = new(StringComparer.Ordinal);

    protected TextsBase(UiText ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;
        _ui.Changed += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Key to inline fallback of every text read so far; filled as the texts are read. Each read returns a snapshot taken under the lock:
    /// it stays consistent while other threads keep reading texts, and it does not show keys that are requested later.
    /// </summary>
    public IReadOnlyDictionary<string, string> RequestedKeys
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, string>(_requestedKeys, StringComparer.Ordinal);
            }
        }
    }

    protected string T(string key, string fallback)
    {
        Record(key, fallback);
        return _ui.Get(key, fallback);
    }

    protected string F(string key, string fallback, params object?[] args)
    {
        Record(key, fallback);
        return _ui.Format(key, fallback, args);
    }

    private void Record(string key, string fallback)
    {
        lock (_gate)
        {
            _requestedKeys[key] = fallback;
        }
    }
}
