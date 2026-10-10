using ICSharpCode.AvalonEdit.Document;
using TotalWarModTool.Localization;
using TotalWarModTool.Objects;
using TotalWarModTool.Schema;

namespace TotalWarModTool.App.Tests;

public class EditorHoverTests
{
    private const string Table = "building_levels_tables";
    private static readonly AppTexts Texts = new(new UiText(@"X:\none"));
    private static readonly TableSource Vanilla = new(SourceKind.Vanilla, "db.pack", @"D:\game\db.pack");
    private static readonly ObjectId Chain = new("building_chains_tables", ["chain1"]);

    // 1 head, 2 effect = v, 3 chain = chain1 (link), 4 missing = ghost (warning value).
    private static BlockText Block() => new(
    [
        new BlockLine(BlockLineKind.Head, [new("building_levels", SpanKind.TableName), new(" ", SpanKind.Normal), new("a", SpanKind.Key)], [], 0),
        new BlockLine(BlockLineKind.RootField, [new("effect", SpanKind.FieldName), new(" = ", SpanKind.Normal), new("v", SpanKind.Value)], [], 1),
        new BlockLine(BlockLineKind.RootField, [new("chain", SpanKind.FieldName), new(" = ", SpanKind.Normal), new("chain1", SpanKind.Link, Chain)], [], 1),
        new BlockLine(BlockLineKind.RootField, [new("missing", SpanKind.FieldName), new(" = ", SpanKind.Normal), new("ghost", SpanKind.Warning)], [], 1),
    ], []);

    private static ObjectBuildResult Result(bool withLoc)
    {
        var order = new LoadOrder([Vanilla], null, []);
        var root = new ObjectRow([new FieldValue("effect", FieldType.StringU8, "v", null, ReferenceResolution.None, null, null)],
            new RowProvenance(Vanilla, [], false, false), null, null);
        var view = new ObjectView(new ObjectId(Table, ["a"]), root, [], [], [], order, [Table]);
        var texts = withLoc
            ? new Dictionary<string, LocText> { ["building_chains_onscreen_name_chain1"] = new("Chain One", Vanilla) }
            : new Dictionary<string, LocText>();
        return new ObjectBuildResult(view, order, null, new Dictionary<string, string> { [Table] = "sig" }, "loc", new LocTable(texts, []));
    }

    private static (ObjectTabViewModel Tab, BlockLineMap Map, TextDocument Document) Create(bool withLoc = true, params BlockDiagnostic[] diagnostics)
    {
        var tab = new ObjectTabViewModel(Texts, _ => { }, [new ObjectRef(Table, ["a"], null)], 0, key => key);
        tab.ApplyResult(Result(withLoc));
        tab.CheckHandler = _ => new ObjectChanges([], [], diagnostics);
        tab.Text += " ";
        tab.Check();
        var block = Block();
        var document = new TextDocument(block.Text);
        return (tab, new BlockLineMap(document, block), document);
    }

    [Fact]
    public void A_line_with_an_error_tells_its_kind_and_detail()
    {
        var (tab, map, _) = Create(true, new BlockDiagnostic(2, DiagnosticSeverity.Error, DiagnosticKind.InvalidValue, "expected an integer"));

        Assert.Equal(Texts.DiagnosticText(DiagnosticKind.InvalidValue) + " expected an integer", EditorHover.ToolTipAt(tab, map, 2, 0, Texts));
        Assert.Null(EditorHover.ToolTipAt(tab, map, 1, 0, Texts));
    }

    [Fact]
    public void A_link_under_the_column_tells_the_display_name_or_the_table()
    {
        var (tab, map, _) = Create();
        Assert.Equal("Chain One", EditorHover.ToolTipAt(tab, map, 3, 12, Texts));

        var (bare, bareMap, _) = Create(withLoc: false);
        Assert.Equal("building_chains", EditorHover.ToolTipAt(bare, bareMap, 3, 12, Texts));
        Assert.Null(EditorHover.ToolTipAt(tab, map, 3, 3, Texts));
    }

    [Fact]
    public void A_changed_line_has_no_link_tool_tip()
    {
        var (tab, map, document) = Create();
        var line = document.GetLineByNumber(3);

        document.Replace(line.Offset, line.Length, "  chain = chain2");

        Assert.Null(EditorHover.ToolTipAt(tab, map, 3, 12, Texts));
        Assert.Null(EditorHover.LinkAt(map, 3, 12));
    }

    [Fact]
    public void A_warning_value_tells_target_unknown()
    {
        var (tab, map, _) = Create();

        Assert.Equal(Texts.ObjectsTargetUnknown, EditorHover.ToolTipAt(tab, map, 4, 14, Texts));
        Assert.Null(EditorHover.ToolTipAt(tab, map, 4, 3, Texts));
    }

    [Fact]
    public void LinkAt_finds_the_span_by_column_with_the_indent()
    {
        var (_, map, _) = Create();

        var link = EditorHover.LinkAt(map, 3, 12);
        Assert.Equal(("chain1", Chain), (link!.Text, link.Target));
        Assert.Equal("chain1", EditorHover.LinkAt(map, 3, 10)!.Text);
        Assert.Null(EditorHover.LinkAt(map, 3, 16));
        Assert.Null(EditorHover.LinkAt(map, 3, 4));
        Assert.Null(EditorHover.LinkAt(null, 3, 12));
        Assert.Null(EditorHover.LinkAt(map, 99, 0));
    }

    [Fact]
    public void Control_characters_of_a_detail_are_escaped()
    {
        var (tab, map, _) = Create(true, new BlockDiagnostic(2, DiagnosticSeverity.Error, DiagnosticKind.ReferenceNotFound, "a\nb"));

        var text = EditorHover.ToolTipAt(tab, map, 2, 0, Texts);

        Assert.DoesNotContain("a\nb", text);
        Assert.Contains("a\\u000Ab", text);
    }
}
