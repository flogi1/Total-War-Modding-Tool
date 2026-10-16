using System.Globalization;
using System.Windows.Data;
using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>
/// The text of a wizard list entry, escaped (names and keys are game data): a <see cref="WizardChoice"/> as "Name — Key" (only the key without a
/// name), or with the parameter "name" as Name, else Key; a <see cref="BuildingStage"/> as "level: Name", else "level: LevelKey". View wiring only.
/// </summary>
public sealed class WizardLabelConverter : IValueConverter
{
    public const string NameOnly = "name";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        WizardChoice choice when Equals(parameter, NameOnly) => DisplayText.Safe(choice.Name ?? choice.Key),
        WizardChoice choice => choice.Name is { } name ? DisplayText.Safe(name) + " — " + DisplayText.Safe(choice.Key) : DisplayText.Safe(choice.Key),
        BuildingStage stage => stage.Level.ToString(CultureInfo.InvariantCulture) + ": " + DisplayText.Safe(stage.Name ?? stage.LevelKey),
        _ => string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
