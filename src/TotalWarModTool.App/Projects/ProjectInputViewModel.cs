using System.Text;
using System.Windows.Input;

namespace TotalWarModTool.App;

/// <summary>
/// The model of the project dialog: name, folder and pack name, checked by the caller's <c>validate</c> on every change. The pack name
/// follows the name (lower case, spaces as underscores, plus .pack) until the user types into it; a pack name that the dialog starts with
/// is no user edit. The exception is a non-empty default pack name (the import, where it is the file name of the source, or a second
/// attempt after a refusal): it is kept as it is and never follows the name.
/// </summary>
public sealed class ProjectInputViewModel : ObservableObject
{
    private const string PackExtension = ".pack";

    private readonly Func<ProjectInput, string?> _validate;
    private string _name;
    private string _folder;
    private string _packName;
    private string? _error;

    // True while the pack name follows the name: the dialog started with an empty pack name and the user has not typed into it.
    private bool _followsName;

    /// <param name="info">Lines shown above the fields.</param>
    /// <param name="validate">Returns the text that says why the input cannot be accepted, or null.</param>
    /// <param name="browseFolder">Asks for a folder; null when the user cancels.</param>
    public ProjectInputViewModel(AppTexts texts, ProjectInput defaults, IReadOnlyList<string> info, Func<ProjectInput, string?> validate,
        Func<string?> browseFolder)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(browseFolder);

        Texts = texts;
        Info = info;
        InfoText = string.Join("\n", info);
        _validate = validate;
        _name = defaults.Name;
        _folder = defaults.Folder;
        _packName = defaults.PackName;
        _followsName = defaults.PackName.Length == 0;
        _error = validate(Result);
        BrowseFolderCommand = new RelayCommand(() =>
        {
            if (browseFolder() is { } chosen)
            {
                Folder = chosen;
            }
        });
    }

    public AppTexts Texts { get; }

    public IReadOnlyList<string> Info { get; }

    /// <summary><see cref="Info"/> as one text, one line each; empty when there is nothing to say.</summary>
    public string InfoText { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value ?? string.Empty))
            {
                return;
            }

            if (_followsName)
            {
                SetProperty(ref _packName, DerivePackName(_name), nameof(PackName));
            }

            Revalidate();
        }
    }

    public string Folder
    {
        get => _folder;
        set
        {
            if (SetProperty(ref _folder, value ?? string.Empty))
            {
                Revalidate();
            }
        }
    }

    public string PackName
    {
        get => _packName;
        set
        {
            if (SetProperty(ref _packName, value ?? string.Empty))
            {
                _followsName = false;
                Revalidate();
            }
        }
    }

    /// <summary>Why the input cannot be accepted; null when it can.</summary>
    public string? Error => _error;

    public bool CanAccept => _error is null;

    public ICommand BrowseFolderCommand { get; }

    /// <summary>The input as it stands.</summary>
    public ProjectInput Result => new(_name, _folder, _packName);

    private void Revalidate()
    {
        if (SetProperty(ref _error, _validate(Result), nameof(Error)))
        {
            OnPropertyChanged(nameof(CanAccept));
        }
    }

    // Lower case, spaces as underscores, only a-z 0-9 _ -, then .pack; empty when no character is left.
    private static string DerivePackName(string name)
    {
        var stem = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c == ' ')
            {
                stem.Append('_');
            }
            else if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')
            {
                stem.Append(c);
            }
        }

        return stem.Length == 0 ? string.Empty : stem + PackExtension;
    }
}
