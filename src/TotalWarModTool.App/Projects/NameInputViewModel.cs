namespace TotalWarModTool.App;

/// <summary>
/// The model of the name dialog (rename, new loc file, add a pack entry): one name, checked by the caller's <c>validate</c> on every change.
/// The name is taken as typed; nothing is trimmed or normalised.
/// </summary>
public sealed class NameInputViewModel : ObservableObject
{
    private readonly Func<string, string?> _validate;
    private string _name;
    private string? _error;

    /// <param name="label">The label of the name box, e.g. "Name".</param>
    /// <param name="suggestion">The name the box starts with.</param>
    /// <param name="validate">Returns the text that says why the name cannot be accepted, or null.</param>
    public NameInputViewModel(AppTexts texts, string label, string suggestion, Func<string, string?> validate)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(suggestion);
        ArgumentNullException.ThrowIfNull(validate);

        Texts = texts;
        Label = label;
        _validate = validate;
        _name = suggestion;
        _error = validate(_name);
    }

    public AppTexts Texts { get; }

    public string Label { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value ?? string.Empty))
            {
                return;
            }

            if (SetProperty(ref _error, _validate(_name), nameof(Error)))
            {
                OnPropertyChanged(nameof(CanAccept));
            }
        }
    }

    /// <summary>Why the name cannot be accepted; null when it can.</summary>
    public string? Error => _error;

    public bool CanAccept => _error is null;

    /// <summary>The name as it stands.</summary>
    public string Result => _name;
}
