using System.IO;
using System.Windows.Threading;
using TotalWarModTool.Logging;
using TotalWarModTool.Settings;
using TotalWarModTool.Storage;

namespace TotalWarModTool.App;

/// <summary>
/// Saves the settings 500 ms after the last change, so every setting takes effect and persists without an OK button, and a burst of
/// changes (dragging a window) writes the file once. Only a change marks the settings as unsaved; the timer and <see cref="Flush"/>
/// write nothing else. Must be created on the UI thread, before the first change that should be saved. A failed save is logged,
/// never thrown: losing one settings write must not take the app down.
/// </summary>
public sealed class SettingsAutoSaver
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly GlobalSettings _settings;
    private readonly GlobalSettingsStore _store;
    private readonly IAppLog _log;
    private readonly DispatcherTimer _timer;
    private bool _unsaved;

    /// <param name="loadStatus">
    /// How the settings file was loaded. For <see cref="LoadStatus.NewerVersion"/> and <see cref="LoadStatus.Unreadable"/> the saver
    /// never writes in this session (and says so with one warning now): a newer file would lose the fields this version does not
    /// know, and the settings of an unreadable file are only defaults, which would overwrite the user's real ones.
    /// </param>
    public SettingsAutoSaver(GlobalSettings settings, GlobalSettingsStore store, IAppLog log, LoadStatus loadStatus)
    {
        _settings = settings;
        _store = store;
        _log = log;
        _timer = new DispatcherTimer { Interval = SaveDelay };

        if (loadStatus is LoadStatus.NewerVersion or LoadStatus.Unreadable)
        {
            var reason = loadStatus == LoadStatus.NewerVersion
                ? "was written by a newer version of the app and would lose the settings this version does not know"
                : "could not be read and would be overwritten with the default settings";
            _log.Warn($"Settings will not be saved in this session: {store.FilePath} {reason}.");

            // Never subscribed, so nothing is ever marked as unsaved and nothing is ever written.
            return;
        }

        _timer.Tick += (_, _) => Flush();
        _settings.Changed += (_, _) =>
        {
            _unsaved = true;

            // Stop and start again to restart the delay.
            _timer.Stop();
            _timer.Start();
        };
    }

    /// <summary>Saves now if there are unsaved changes and cancels the pending delayed save. A failed save keeps the changes pending.</summary>
    public void Flush()
    {
        _timer.Stop();
        if (!_unsaved)
        {
            return;
        }

        try
        {
            _store.Save(_settings);
            _unsaved = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"The settings could not be saved to {_store.FilePath}: {ex.Message}");
        }
    }
}
