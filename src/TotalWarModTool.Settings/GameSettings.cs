namespace TotalWarModTool.Settings;

/// <summary>
/// Settings of one game (stored per game, next to its session). Every setting is changed through its property, which raises
/// <see cref="Changed"/> only when the value really changes.
/// </summary>
public sealed class GameSettings
{
    private string? _installPath;

    /// <summary>Argument: the name of the changed property.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>Manually chosen install folder. The setter trims; a blank value becomes null (detect the game through Steam).</summary>
    public string? InstallPath
    {
        get => _installPath;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (normalized == _installPath)
            {
                return;
            }

            _installPath = normalized;
            Changed?.Invoke(this, nameof(InstallPath));
        }
    }
}
