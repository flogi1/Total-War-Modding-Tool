using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>
/// The model of the new building dialog (spec 5.8): a subculture, its templates with a filter over key and name, the new key, and the chain
/// (a new one, or a stage in the chain of the template after a chosen stage). The key follows the template ("&lt;template&gt;_copy") until the user
/// types one. The caller's ValidateKey checks the key on every change; with no template listed OK is blocked with a text.
/// </summary>
public sealed class NewBuildingViewModel : ObservableObject
{
    private const string KeySuffix = "_copy";

    private readonly NewBuildingInput _input;
    private WizardChoice? _subculture;
    private IReadOnlyList<WizardChoice> _templates;
    private string _filter = string.Empty;
    private IReadOnlyList<WizardChoice> _filtered;
    private WizardChoice? _template;
    private string _key = string.Empty;
    private bool _keyTyped;
    private bool _newChain = true;
    private IReadOnlyList<BuildingStage> _stages = [];
    private BuildingStage? _stage;
    private string? _error;

    // True while a list is replaced: the list box and the combo box clear their selection then and report that, which is not a choice of the user.
    private bool _replacingList;

    public NewBuildingViewModel(AppTexts texts, NewBuildingInput input)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(input);
        Texts = texts;
        _input = input;
        Subcultures = input.Catalog.Subcultures;
        _subculture = Subcultures.FirstOrDefault(s => string.Equals(s.Key, input.PreselectedSubculture, StringComparison.Ordinal))
            ?? (Subcultures.Count > 0 ? Subcultures[0] : null);
        _templates = TemplatesOfSubculture();
        _filtered = _templates;
        _template = _filtered.Count > 0 ? _filtered[0] : null;
        TemplateChanged();
        _error = Check();
    }

    public AppTexts Texts { get; }

    /// <summary>Every subculture of the catalog, in its order.</summary>
    public IReadOnlyList<WizardChoice> Subcultures { get; }

    /// <summary>The preselected subculture, else the first. Setting null is ignored.</summary>
    public WizardChoice? SelectedSubculture
    {
        get => _subculture;
        set
        {
            if (_replacingList || value is null || !SetProperty(ref _subculture, value))
            {
                return;
            }

            _templates = TemplatesOfSubculture();
            Refilter();
        }
    }

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

    /// <summary>The templates of the subculture that the filter lets through, in the order of the catalog.</summary>
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
                TemplateChanged();
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

    /// <summary>A new chain (the default), else a stage in the chain of the template. Without a chain it stays true.</summary>
    public bool NewChain
    {
        get => _newChain;
        set
        {
            if (SetProperty(ref _newChain, value || !CanChooseExistingChain))
            {
                OnPropertyChanged(nameof(ExistingChain));
                Revalidate();
            }
        }
    }

    /// <summary>The other radio button of <see cref="NewChain"/>: a stage in the existing chain.</summary>
    public bool ExistingChain
    {
        get => !_newChain;
        set => NewChain = !value;
    }

    /// <summary>The stages of the chain of the template; empty without a chain.</summary>
    public IReadOnlyList<BuildingStage> Stages => _stages;

    /// <summary>The stage the new building follows; the last stage when the template changes. Setting null while stages are listed is ignored.</summary>
    public BuildingStage? SelectedStage
    {
        get => _stage;
        set
        {
            if (_replacingList || (value is null && _stages.Count > 0))
            {
                return;
            }

            if (SetProperty(ref _stage, value))
            {
                Revalidate();
            }
        }
    }

    /// <summary>The chain of the template has stages to follow.</summary>
    public bool CanChooseExistingChain => _stages.Count > 0;

    /// <summary>Why the input cannot be accepted; null when it can.</summary>
    public string? Error => _error;

    public bool CanAccept => _error is null;

    /// <summary>The answer as it stands; null while <see cref="Error"/> is set.</summary>
    public NewBuildingAnswer? Result =>
        _error is null && _template is { } template ? new NewBuildingAnswer(template.Key, _key, _newChain ? null : _stage) : null;

    private IReadOnlyList<WizardChoice> TemplatesOfSubculture() => _subculture is { } subculture ? _input.Catalog.TemplatesOf(subculture.Key) : [];

    private void Refilter()
    {
        var filtered = _filter.Length == 0
            ? _templates
            : _templates.Where(t => t.Key.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || (t.Name?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        var selected = _template is { } current && filtered.Contains(current) ? current : filtered.Count > 0 ? filtered[0] : null;
        var templateChanged = !Equals(selected, _template);

        // The fields are set first, so that the list box, which clears its selection while it takes the new list, is not heard.
        _template = selected;
        _filtered = filtered;
        ReplacingList(nameof(FilteredTemplates));
        OnPropertyChanged(nameof(SelectedTemplate));
        if (templateChanged)
        {
            TemplateChanged();
        }

        Revalidate();
    }

    // The key follows the template until the user typed one; the stages are those of the template's chain, the last one chosen.
    private void TemplateChanged()
    {
        if (!_keyTyped && _template is { } template)
        {
            SetProperty(ref _key, template.Key + KeySuffix, nameof(Key));
        }

        var chain = _template is { } chosen ? _input.Catalog.ChainOf(chosen.Key) : null;
        _stages = chain is null ? [] : _input.Catalog.StagesOf(chain);
        _stage = _stages.Count > 0 ? _stages[^1] : null;
        ReplacingList(nameof(Stages));
        OnPropertyChanged(nameof(SelectedStage));
        OnPropertyChanged(nameof(CanChooseExistingChain));
        if (!CanChooseExistingChain && !_newChain)
        {
            _newChain = true;
            OnPropertyChanged(nameof(NewChain));
            OnPropertyChanged(nameof(ExistingChain));
        }
    }

    private void ReplacingList(string property)
    {
        _replacingList = true;
        try
        {
            OnPropertyChanged(property);
        }
        finally
        {
            _replacingList = false;
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
