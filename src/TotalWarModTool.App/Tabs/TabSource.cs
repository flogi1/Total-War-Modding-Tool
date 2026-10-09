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

    /// <summary>The start of the key of an object tab: <c>object:&lt;table&gt;</c>. It is no path of a pack and no project key.</summary>
    public const string ObjectPrefix = "object:";

    /// <summary>The key of the mod project with this id.</summary>
    public static string KeyOf(Guid id) => ProjectPrefix + id.ToString("D", CultureInfo.InvariantCulture);

    public static TabSource ForProject(Guid id, string name) => new(KeyOf(id), name);

    /// <summary>The source of an object tab: its key names the table, <paramref name="name"/> (the UI text for "object") goes to the tool tip.</summary>
    public static TabSource ForObject(string table, string name) => new(ObjectPrefix + table, name);

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
