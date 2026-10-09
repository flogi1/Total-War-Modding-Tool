using TotalWarModTool.Objects;

namespace TotalWarModTool.App;

/// <summary>
/// What one build delivered. <paramref name="TableSignatures"/>: <c>TableStore.SignatureOf</c> per table of <c>View.Tables</c>;
/// <paramref name="LocSignature"/>: <c>LocStore.SignatureOf</c>; <paramref name="Loc"/>: the loc table of the build, for display names on hover.
/// </summary>
public sealed record ObjectBuildResult(ObjectView View, LoadOrder Order, CategoryDefinition? Category,
    IReadOnlyDictionary<string, string> TableSignatures, string LocSignature, LocTable Loc);
