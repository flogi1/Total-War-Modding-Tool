using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Workspace;

/// <summary>Where a pack comes from. The order is the order of the catalog.</summary>
public enum PackOrigin
{
    Vanilla,
    DataFolderMod,
    Workshop,
}

/// <summary>
/// One pack of the catalog. <see cref="WorkshopId"/> is the name of the workshop sub folder and null for every other origin.
/// <see cref="Header"/> is null when <see cref="Problem"/> is not <see cref="PackProblem.None"/>; <see cref="Detail"/> is short
/// technical English text, not a UI text.
/// </summary>
public sealed record PackSource(
    string Path,
    string Name,
    PackOrigin Origin,
    string? WorkshopId,
    FileFingerprint? Fingerprint,
    PackHeader? Header,
    PackProblem Problem,
    string? Detail);

/// <summary><see cref="UsedManifest"/> is true when the vanilla packs were taken from the manifest file of the profile.</summary>
public sealed record PackCatalogResult(IReadOnlyList<PackSource> Packs, bool UsedManifest);
