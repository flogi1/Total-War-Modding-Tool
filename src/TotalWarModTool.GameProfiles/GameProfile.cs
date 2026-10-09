namespace TotalWarModTool.GameProfiles;

/// <summary>Everything the read libraries need to know about one Total War game. All values come from a profile file.</summary>
public sealed record GameProfile(
    string Id,
    string DisplayName,
    int SteamAppId,
    string InstallFolderName,
    string DataFolder,
    string PackFormat,
    string SchemaFile,
    IReadOnlyList<string> VanillaPackTypes,
    string? VanillaManifestFile,
    /// <summary>Language code of data/language.txt (upper case) to loc pack file name, for example "DE" to "local_ge.pack". Never null after loading.</summary>
    IReadOnlyDictionary<string, string>? LocPacks = null,
    /// <summary>Loc pack when the language is unknown or language.txt is missing; null means no texts.</summary>
    string? DefaultLocPack = null);

/// <summary>A profile file that was skipped, with a short technical reason.</summary>
public sealed record ProfileProblem(string FileName, string Detail);

public sealed record ProfileLoadResult(IReadOnlyList<GameProfile> Profiles, IReadOnlyList<ProfileProblem> Problems);
