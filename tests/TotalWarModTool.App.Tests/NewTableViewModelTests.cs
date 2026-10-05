namespace TotalWarModTool.App.Tests;

public class NewTableViewModelTests
{
    private static readonly AppTexts Texts = new(new TotalWarModTool.Localization.UiText(Path.Combine(Path.GetTempPath(), "no-such-locales-folder")));

    private static readonly string[] Tables = ["a_tables", "unit_set_tables", "units_tables", "zebras_tables"];

    private static NewTableViewModel Create(IReadOnlyList<string>? tables = null, string suggestion = "mine", Func<NewTableInput, string?>? validate = null) =>
        new(Texts, tables ?? Tables, suggestion, validate ?? (_ => null));

    [Fact] public void The_view_model_starts_with_the_first_table_and_the_suggestion()
    {
        var seen = new List<NewTableInput>();
        var viewModel = Create(suggestion: "my_mod", validate: input =>
        {
            seen.Add(input);
            return null;
        });

        Assert.Equal(("a_tables", "my_mod", string.Empty), (viewModel.SelectedTable, viewModel.Name, viewModel.Filter));
        Assert.Equal(Tables, viewModel.FilteredTables);
        Assert.Equal(new NewTableInput("a_tables", "my_mod"), viewModel.Result);
        Assert.Equal(new NewTableInput("a_tables", "my_mod"), seen[^1]);                // the start state is checked at once
        Assert.Same(Texts, viewModel.Texts);
        Assert.Null(viewModel.Error);
        Assert.True(viewModel.CanAccept);
    }

    [Fact] public void The_filter_matches_anywhere_ignoring_case_and_an_empty_list_blocks_ok()
    {
        var viewModel = Create();
        var changed = new List<string?>(); viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.Filter = "UNIT";

        Assert.Equal(["unit_set_tables", "units_tables"], viewModel.FilteredTables);
        Assert.Contains(nameof(NewTableViewModel.FilteredTables), changed);
        Assert.Contains(nameof(NewTableViewModel.SelectedTable), changed);
        Assert.Equal("unit_set_tables", viewModel.SelectedTable);                          // the old selection is gone: the first one

        viewModel.Filter = "s_tab";                                                        // in the middle of the names

        Assert.Equal(["units_tables", "zebras_tables"], viewModel.FilteredTables);
        Assert.Equal("units_tables", viewModel.SelectedTable);

        viewModel.Filter = "no such table";

        Assert.Empty(viewModel.FilteredTables);
        Assert.Null(viewModel.SelectedTable);
        Assert.Equal((Texts.FileTableNoMatch, false), (viewModel.Error, viewModel.CanAccept));

        viewModel.Filter = string.Empty;

        Assert.Equal(Tables, viewModel.FilteredTables);
        Assert.Equal("a_tables", viewModel.SelectedTable);
        Assert.Equal((null, true), (viewModel.Error, viewModel.CanAccept));

        // An empty source list (no schema): the window says so and OK stays blocked, whatever is typed.
        var empty = Create([]);
        empty.Name = "mine";
        empty.Filter = "x";

        Assert.Empty(empty.FilteredTables);
        Assert.Null(empty.SelectedTable);
        Assert.Equal((Texts.FileNoSchema, false), (empty.Error, empty.CanAccept));
    }

    [Fact] public void The_selection_stays_while_the_table_is_still_in_the_filtered_list()
    {
        var viewModel = Create();
        viewModel.SelectedTable = "units_tables";

        viewModel.Filter = "unit";

        Assert.Equal("units_tables", viewModel.SelectedTable);                             // still listed: kept, although it is not the first

        viewModel.Filter = "units";

        Assert.Equal("units_tables", viewModel.SelectedTable);

        viewModel.Filter = "set";

        Assert.Equal("unit_set_tables", viewModel.SelectedTable);                          // gone from the list: the first one
    }

    [Fact] public void A_refused_name_blocks_ok_and_names_the_reason()
    {
        var seen = new List<NewTableInput>();
        var viewModel = Create(suggestion: "mine", validate: input =>
        {
            seen.Add(input);
            return input is { TableName: "units_tables", Name: "mine" } ? "A file with this name exists already" : null;
        });

        Assert.Equal((null, true), (viewModel.Error, viewModel.CanAccept));

        viewModel.SelectedTable = "units_tables";                                          // the check depends on the table as well

        Assert.Equal(new NewTableInput("units_tables", "mine"), seen[^1]);
        Assert.Equal(("A file with this name exists already", false), (viewModel.Error, viewModel.CanAccept));

        viewModel.Name = "other";

        Assert.Equal((null, true), (viewModel.Error, viewModel.CanAccept));
        Assert.Equal(new NewTableInput("units_tables", "other"), viewModel.Result);
    }

    [Fact] public void A_deselect_from_the_view_is_ignored_while_the_list_has_tables()
    {
        var viewModel = Create();
        viewModel.SelectedTable = "units_tables";
        var changed = new List<string?>(); viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.SelectedTable = null;                                                    // a Ctrl+click on the chosen table

        Assert.Equal("units_tables", viewModel.SelectedTable);
        Assert.Equal((null, true), (viewModel.Error, viewModel.CanAccept));                // not "no table matches"
        Assert.DoesNotContain(nameof(NewTableViewModel.Error), changed);

        // With a filter that lists tables the same holds; only a list without tables has no selection and says so.
        viewModel.Filter = "unit";
        viewModel.SelectedTable = null;

        Assert.Equal("units_tables", viewModel.SelectedTable);

        viewModel.Filter = "no such table";

        Assert.Null(viewModel.SelectedTable);
        Assert.Equal((Texts.FileTableNoMatch, false), (viewModel.Error, viewModel.CanAccept));

        viewModel.SelectedTable = null;

        Assert.Equal(Texts.FileTableNoMatch, viewModel.Error);
    }

    [Fact] public void The_name_check_is_not_asked_while_no_table_is_selected()
    {
        var asked = 0;
        var viewModel = Create(validate: _ =>
        {
            asked++;
            return null;
        });
        var before = asked;

        viewModel.Filter = "no such table";
        viewModel.Name = "other";

        Assert.Equal(before, asked);                                                        // there is no folder to check the name in
        Assert.Equal(new NewTableInput(string.Empty, "other"), viewModel.Result);
        Assert.False(viewModel.CanAccept);
    }
}
