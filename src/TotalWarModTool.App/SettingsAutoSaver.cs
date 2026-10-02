using System.IO;
using System.Windows.Threading;
using TotalWarModTool.Logging;
using TotalWarModTool.Settings;

namespace TotalWarModTool.App;

/// <summary>
/// Saves the settings 500 ms after the last change, so every setting takes effect and persists without an OK button, and a burst of
/// changes (dragging a window) writes the file once. Must be created on the UI thread. A failed save is logged, never thrown:
/// losing one settings write must not take the app down.
/// </summary>
public sealed class SettingsAutoSaver
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly GlobalSettings _settings;
    private readonly GlobalSettingsStore _store;
    private readonly IAppLog _log;
    private readonly DispatcherTimer _timer;

    public SettingsAutoSaver(GlobalSettings settings, GlobalSettingsStore store, IAppLog log)
    {
        _settings = settings;
        _store = store;
        _log = log;

        _timer = new DispatcherTimer { Interval = SaveDelay };
        _timer.Tick += (_, _) => Flush();
        _settings.Changed += (_, _) =>
        {
            // Stop and start again to restart the delay.
            _timer.Stop();
            _timer.Start();
        };
    }

    /// <summary>Saves now and cancels the pending delayed save.</summary>
    public void Flush()
    {
        _timer.Stop();
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"The settings could not be saved to {_store.FilePath}: {ex.Message}");
        }
    }
}
