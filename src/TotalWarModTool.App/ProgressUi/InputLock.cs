using System.Windows;
using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>
/// Blocks mouse and keyboard input on the given windows until disposed. Uses <c>IsHitTestVisible</c> and handled preview events instead of
/// <c>IsEnabled</c>, so nothing turns grey for the short moment the lock is held. Create and dispose on the UI thread.
/// </summary>
public sealed class InputLock : IDisposable
{
    private readonly List<(Window Window, bool WasHitTestVisible)> _locked = [];
    private bool _disposed;

    public InputLock(IReadOnlyList<Window> windows)
    {
        foreach (var window in windows.Distinct())
        {
            _locked.Add((window, window.IsHitTestVisible));
            window.IsHitTestVisible = false;
            window.PreviewKeyDown += SwallowKey;
            window.PreviewKeyUp += SwallowKey;
            window.PreviewTextInput += SwallowText;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var (window, wasHitTestVisible) in _locked)
        {
            window.PreviewKeyDown -= SwallowKey;
            window.PreviewKeyUp -= SwallowKey;
            window.PreviewTextInput -= SwallowText;
            window.IsHitTestVisible = wasHitTestVisible;
        }
    }

    private static void SwallowKey(object sender, KeyEventArgs e) => e.Handled = true;

    private static void SwallowText(object sender, TextCompositionEventArgs e) => e.Handled = true;
}
