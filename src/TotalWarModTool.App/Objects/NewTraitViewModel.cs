using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>
/// The model of the new trait dialog (spec 5.8): the traits as templates with a filter over key and name, and the new key. The key follows the
/// template ("&lt;template&gt;_copy") until the user types one. The caller's ValidateKey checks the key on every change; with no template listed
/// OK is blocked with a text. Like <see cref="NewBuildingViewModel"/> without race and chain.
/// </summary>
public sealed class NewTraitViewModel : ObservableObject
{
    private const string KeySuffix = "_copy";

    private readonly NewTraitInput _input;
    private string _filter = string.Empty;
    private IReadOnlyList<WizardChoice> _filtered;
    private WizardChoice? _template;
    private string _key = string.Empty;
    private bool _keyTyped;
    private string? _error;

    // True while the list is replaced: the list box clears its selection then and reports that, which is not a choice of the user.
    private bool _replacingList;

    public NewTraitViewModel(AppTexts texts, NewTraitInput input)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(input);
        Texts = texts;
        _input = input;
        _filtered = input.Templates;
        _template = _filtered.Count > 0 ? _filtered[0] : null;
        FollowTemplate();
        _error = Check();
    }

    public AppTexts Texts { get; }

    /// <summary>The text of the filter box: keeps the templates whose key or name contains it, ignoring case.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? string.Empty))
            {
                Refilter();
            }
        }
    }

    /// <summary>The templates that the filter lets through, in the order of the input.</summary>
    public IReadOnlyList<WizardChoice> FilteredTemplates => _filtered;

    /// <summary>The chosen template; it stays while it is listed, else the first listed one. Setting null while templates are listed is ignored.</summary>
    public WizardChoice? SelectedTemplate
    {
        get => _template;
        set
        {
            if (_replacingList || (value is null && _filtered.Count > 0))
            {
                return;
            }

            if (SetProperty(ref _template, value))
            {
                FollowTemplate();
                Revalidate();
            }
        }
    }

    /// <summary>The new key: "&lt;template&gt;_copy" until the user types one, then the typed one.</summary>
    public string Key
    {
        get => _key;
        set
        {
            // The box giving back the value it shows is no typing.
            if (SetProperty(ref _key, value ?? string.Empty))
            {
                _keyTyped = true;
                Revalidate();
            }
        }
    }

    /// <summary>Why the input cannot be accepted; null when it can.</summary>
    public string? Error => _error;

    public bool CanAccept => _error is null;

    /// <summary>The answer as it stands; null while <see cref="Error"/> is set.</summary>
    public NewTraitAnswer? Result => _error is null && _template is { } template ? new NewTraitAnswer(template.Key, _key) : null;

    private void Refilter()
    {
        var filtered = _filter.Length == 0
            ? _input.Templates
            : _input.Templates.Where(t => t.Key.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || (t.Name?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        var selected = _template is { } current && filtered.Contains(current) ? current : filtered.Count > 0 ? filtered[0] : null;
        var templateChanged = !Equals(selected, _template);

        // The fields are set first, so that the list box, which clears its selection while it takes the new list, is not heard.
        _template = selected;
        _filtered = filtered;
        _replacingList = true;
        try
        {
            OnPropertyChanged(nameof(FilteredTemplates));
        }
        finally
        {
            _replacingList = false;
        }

        OnPropertyChanged(nameof(SelectedTemplate));
        if (templateChanged)
        {
            FollowTemplate();
        }

        Revalidate();
    }

    // The key follows the template until the user typed one.
    private void FollowTemplate()
    {
        if (!_keyTyped && _template is { } template)
        {
            SetProperty(ref _key, template.Key + KeySuffix, nameof(Key));
        }
    }

    private void Revalidate()
    {
        if (SetProperty(ref _error, Check(), nameof(Error)))
        {
            OnPropertyChanged(nameof(CanAccept));
        }

        OnPropertyChanged(nameof(Result));
    }

    private string? Check() => _template is null ? Texts.ObjectsWizardNoTemplate : _input.ValidateKey(_key);
}
