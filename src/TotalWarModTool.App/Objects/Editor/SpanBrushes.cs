using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>The theme token of every span kind, as the document view of 4.1 had it. Pure.</summary>
public static class SpanBrushes
{
    public static string ResourceKeyOf(SpanKind kind) => ThemePalette.ResourcePrefix + kind switch
    {
        SpanKind.ProvenanceVanilla => "ObjectVanilla",
        SpanKind.ProvenanceMod => "ObjectMod",
        SpanKind.ProvenanceProject => "ObjectProject",
        SpanKind.LocText => "ObjectLocText",
        SpanKind.Warning => "Warning",
        SpanKind.FieldName or SpanKind.Overridden or SpanKind.Duplicate or SpanKind.OlderLayout => "TextMuted",
        SpanKind.Link => "Accent",
        _ => "Text",
    };

    public static bool IsBold(SpanKind kind) => kind is SpanKind.TableName or SpanKind.Key or SpanKind.Title;

    public static bool IsStrikethrough(SpanKind kind) => kind == SpanKind.Overridden;
}
