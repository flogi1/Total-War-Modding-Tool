namespace TotalWarModTool.App.Tests;

public class NameInputViewModelTests
{
    private static readonly AppTexts Texts = new(new TotalWarModTool.Localization.UiText(Path.Combine(Path.GetTempPath(), "no-such-locales-folder")));

    private static NameInputViewModel Create(string suggestion = "mine", Func<string, string?>? validate = null, string label = "Name") =>
        new(Texts, label, suggestion, validate ?? (_ => null));

    [Fact] public void The_view_model_starts_with_the_suggestion_and_the_label()
    {
        var viewModel = Create("my_mod", label: "File name");

        Assert.Equal(("my_mod", "File name", "my_mod"), (viewModel.Name, viewModel.Label, viewModel.Result));
        Assert.Same(Texts, viewModel.Texts);
        Assert.Null(viewModel.Error);
        Assert.True(viewModel.CanAccept);
    }

    [Fact] public void A_refused_name_blocks_ok_and_names_the_reason()
    {
        var seen = new List<string>();
        var viewModel = Create("taken", name =>
        {
            seen.Add(name);
            return name == "taken" ? "A file with this name exists already" : null;
        });
        var changed = new List<string?>(); viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // The suggestion is checked at once.
        Assert.Equal(("A file with this name exists already", false), (viewModel.Error, viewModel.CanAccept));

        viewModel.Name = "free";

        Assert.Equal((null, true), (viewModel.Error, viewModel.CanAccept));
        Assert.Equal("free", viewModel.Result);
        Assert.Contains(nameof(NameInputViewModel.Error), changed);
        Assert.Contains(nameof(NameInputViewModel.CanAccept), changed);
        Assert.Equal("free", seen[^1]);

        viewModel.Name = "taken";

        Assert.Equal(("A file with this name exists already", false), (viewModel.Error, viewModel.CanAccept));
    }

    [Fact] public void The_name_is_taken_as_typed_and_a_missing_value_is_an_empty_name()
    {
        var seen = new List<string>();
        var viewModel = Create(" Mixed Case ", name =>
        {
            seen.Add(name);
            return null;
        });

        Assert.Equal(" Mixed Case ", viewModel.Result);                      // nothing is trimmed or normalised

        viewModel.Name = null!;

        Assert.Equal(string.Empty, viewModel.Name);
        Assert.Equal(string.Empty, seen[^1]);
    }
}
