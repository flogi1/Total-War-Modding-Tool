using System.Globalization;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>
/// Where a tab comes from. <see cref="Key"/> identifies the source (the path of a pack, or <c>project:&lt;guid D&gt;</c> for a mod project);
/// <see cref="Name"/> is shown in the tool tip. Both are data, not UI texts.
/// </summary>
public sealed record TabSource(string Key, string Name)
{
    /// <summary>The start of the key of a mod project. No path of a pack starts like this: a drive or root comes first.</summary>
    public const string ProjectPrefix = "project:";

    public static TabSource ForProject(Guid id, string name) => new(ProjectPrefix + id.ToString("D", CultureInfo.InvariantCulture), name);

    /// <summary>True when the key is the key of a mod project; <paramref name="id"/> is its id then.</summary>
    public static bool TryGetProjectId(string key, out Guid id)
    {
        id = Guid.Empty;
        return key is not null
            && key.StartsWith(ProjectPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(key.AsSpan(ProjectPrefix.Length), "D", out id);
    }

    public static implicit operator TabSource(PackSource pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return new TabSource(pack.Path, pack.Name);
    }
}
