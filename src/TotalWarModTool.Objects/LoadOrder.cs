namespace TotalWarModTool.Objects;

public enum SourceKind
{
    Vanilla,
    Mod,
    Project,
}

/// <summary>One source of rows. Path is the pack file for Vanilla and Mod, the project folder for Project. Name is the pack file name or the project name.</summary>
public sealed record TableSource(SourceKind Kind, string Name, string Path);

public enum LoadOrderProblem
{
    UsedModsMissing,
    UsedModsUnreadable,
    ModPackMissing,
    LanguageFileUnreadable,
    LocPackMissing,
    RootPackAdded,
}

/// <summary><see cref="Detail"/> is short technical English, not a UI text. <see cref="Name"/> is the pack the warning is about, if any.</summary>
public sealed record LoadOrderWarning(LoadOrderProblem Problem, string? Name, string Detail);

/// <summary>Sources in load order (later wins). LocPack is the vanilla loc pack of the game language, null when none exists.</summary>
public sealed record LoadOrder(IReadOnlyList<TableSource> Sources, TableSource? LocPack, IReadOnlyList<LoadOrderWarning> Warnings);
