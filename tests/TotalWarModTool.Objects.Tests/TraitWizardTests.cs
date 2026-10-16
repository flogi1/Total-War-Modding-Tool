using TotalWarModTool.Schema;
using static TotalWarModTool.Objects.Tests.BlockFixture;
using static TotalWarModTool.Objects.Tests.TestSchema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Objects.Tests;

public class TraitWizardTests
{
    private static readonly TraitWizardDefinition Definition = new("traits_tables", new TextRef("trait_levels_tables", "onscreen_name"));

    private static readonly GameSchema Schema = Build(("traits_tables", 1, [Field("key", key: true)]));

    private static LocTable Loc(params (string Key, string Text)[] texts) =>
        new(texts.ToDictionary(t => t.Key, t => new LocText(t.Text, Vanilla), StringComparer.Ordinal), []);

    /// <summary>The templates over a pack holding the trait table with the given keys.</summary>
    private static IReadOnlyList<WizardChoice> TemplatesOf(LocTable loc, params string[] traits)
    {
        using var tmp = new TempDir();
        var pack = TestSources.Vanilla(TestSources.WritePack(tmp.File("vanilla.pack"), Schema, ("traits_tables", 1, traits.Select(t => new object?[] { t }).ToArray())));
        var order = TestSources.Order(pack);
        var store = new TableStore(Schema, ReferenceGraph.Build(Schema, GameTableVersions.Empty));
        return TraitWizard.Templates(Definition, store.Get("traits_tables", order, CancellationToken.None), loc);
    }

    [Fact]
    public void Templates_are_named_by_the_text_of_their_first_level()
    {
        var templates = TemplatesOf(
            Loc(("trait_levels_onscreen_name_brave_2", "Very brave"), ("trait_levels_onscreen_name_brave_1", "Brave"),
                ("trait_levels_onscreen_name_wise_1", "alpha wise"), ("trait_levels_onscreen_name_bravery_1", "Wrong owner")),
            "brave", "wise");

        Assert.Equal(["wise", "brave"], templates.Select(t => t.Key));
        Assert.Equal(["alpha wise", "Brave"], templates.Select(t => t.Name));
    }

    [Fact]
    public void A_trait_without_level_text_is_listed_by_key()
    {
        var templates = TemplatesOf(Loc(("trait_levels_onscreen_name_b_1", "Zed")), "b", "c", "a", "c");

        Assert.Equal(["a", "c", "b"], templates.Select(t => t.Key));
        Assert.Equal([null, null, "Zed"], templates.Select(t => t.Name));
    }

    private static ParsedObject Object(string table, string key, params string[] texts) => new(new ObjectId(table, [key]), [new ParsedField("key", key, 0)],
        texts.Select(t => new ParsedText(t.StartsWith('d') ? "description" : "onscreen_name", t, 0)).ToList(), [], []);

    [Fact]
    public void Compose_appends_the_suffix_to_the_level_names()
    {
        var copy = new CopyResult(
            [Object("traits_tables", "t9", "Trait"), Object("trait_levels_tables", "t9_1", "Brave", "dFearless"), Object("trait_levels_tables", "t9_2", "Braver")],
            [new CopyNote(CopyNoteKind.ForeignKeyRows, "x_tables", 2)]);

        var composed = TraitWizard.Compose(Definition, copy, " (copy)");

        Assert.Equal(["Trait"], composed.Objects[0].Texts.Select(t => t.Text));
        Assert.Equal(["Brave (copy)", "dFearless"], composed.Objects[1].Texts.Select(t => t.Text));
        Assert.Equal(["Braver (copy)"], composed.Objects[2].Texts.Select(t => t.Text));
        Assert.Equal(copy.Notes, composed.Notes);
        Assert.Equal("Brave", copy.Objects[1].Texts[0].Text);
    }
}
