namespace TotalWarModTool.App.Tests;

public class ProjectInputViewModelTests
{
    private static readonly AppTexts Texts = new(new TotalWarModTool.Localization.UiText(Path.Combine(Path.GetTempPath(), "no-such-locales-folder")));

    private static ProjectInputViewModel Create(ProjectInput? defaults = null, IReadOnlyList<string>? info = null, Func<ProjectInput, string?>? validate = null,
        Func<string?>? browse = null) =>
        new(Texts, defaults ?? new ProjectInput("", "", ""), info ?? [], validate ?? (_ => null), browse ?? (() => null));

    [Fact] public void The_pack_name_follows_the_name_until_the_user_edits_it()
    {
        var viewModel = Create();
        var changed = new List<string?>(); viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.Name = "My Mod 2";

        Assert.Equal("my_mod_2.pack", viewModel.PackName);
        Assert.Contains(nameof(ProjectInputViewModel.PackName), changed);

        viewModel.Name = "A-B  c!";

        Assert.Equal("a-b__c.pack", viewModel.PackName);

        viewModel.PackName = "x.pack";
        viewModel.Name = "Other";

        Assert.Equal("x.pack", viewModel.PackName);
        Assert.Equal(new ProjectInput("Other", "", "x.pack"), viewModel.Result);
    }

    [Fact] public void A_name_without_usable_characters_gives_an_empty_pack_name()
    {
        var viewModel = Create();

        viewModel.Name = "!!!";

        Assert.Equal(string.Empty, viewModel.PackName);

        viewModel.Name = "Mod";

        Assert.Equal("mod.pack", viewModel.PackName);
    }

    [Fact] public void A_pack_name_that_was_given_as_a_default_follows_only_when_it_is_the_derived_one()
    {
        var derived = Create(new ProjectInput("source", "", "source.pack"));
        var custom = Create(new ProjectInput("source", "", "Custom.pack"));
        var empty = Create(new ProjectInput("", "", ""));

        derived.Name = "Renamed";
        custom.Name = "Renamed";
        empty.Name = "Renamed";

        Assert.Equal("renamed.pack", derived.PackName);
        Assert.Equal("Custom.pack", custom.PackName);
        Assert.Equal("renamed.pack", empty.PackName);
        Assert.Equal(new ProjectInput("source", "", "source.pack"), Create(new ProjectInput("source", "", "source.pack")).Result);
    }

    [Fact] public void The_error_text_comes_from_the_validation_and_blocks_accept()
    {
        var seen = new List<ProjectInput>();
        var viewModel = Create(validate: input =>
        {
            seen.Add(input);
            return input.Name.Length == 0 ? "Enter a name" : input.PackName == "bad.pack" ? "Bad pack name" : null;
        });
        var changed = new List<string?>(); viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal(("Enter a name", false), (viewModel.Error, viewModel.CanAccept));          // asked at once, for the defaults

        viewModel.Name = "Mod";

        Assert.Equal((null, true), (viewModel.Error, viewModel.CanAccept));
        Assert.Contains(nameof(ProjectInputViewModel.Error), changed);
        Assert.Contains(nameof(ProjectInputViewModel.CanAccept), changed);
        Assert.Equal(new ProjectInput("Mod", "", "mod.pack"), seen[^1]);

        viewModel.PackName = "bad.pack";

        Assert.Equal(("Bad pack name", false), (viewModel.Error, viewModel.CanAccept));

        viewModel.Folder = @"C:\x";

        Assert.Equal(new ProjectInput("Mod", @"C:\x", "bad.pack"), seen[^1]);
    }

    [Fact] public void Browse_takes_the_folder_that_was_chosen_and_keeps_it_when_cancelled()
    {
        string? answer = @"C:\chosen";
        var viewModel = Create(new ProjectInput("Mod", @"C:\before", "mod.pack"), browse: () => answer);

        viewModel.BrowseFolderCommand.Execute(null);

        Assert.Equal(@"C:\chosen", viewModel.Folder);

        answer = null;
        viewModel.BrowseFolderCommand.Execute(null);

        Assert.Equal(@"C:\chosen", viewModel.Folder);
    }

    [Fact] public void The_info_lines_are_shown_as_one_text()
    {
        Assert.Equal("one\ntwo", Create(info: ["one", "two"]).InfoText);
        Assert.Equal(string.Empty, Create().InfoText);
        Assert.Equal(["one"], Create(info: ["one"]).Info);
    }
}
