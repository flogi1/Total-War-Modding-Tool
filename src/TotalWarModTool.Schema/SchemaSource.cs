namespace TotalWarModTool.Schema;

public enum SchemaOrigin { None, Shipped, Override }

/// <summary>
/// The schema that won plus both load attempts. An attempt is null when it was not made: <paramref name="OverrideAttempt"/> without an
/// override folder, <paramref name="ShippedAttempt"/> when the override already loaded.
/// </summary>
public sealed record ResolvedSchema(GameSchema? Schema, SchemaOrigin Origin, string? Path, DateTime? FileDateUtc,
    SchemaLoadResult? OverrideAttempt, SchemaLoadResult? ShippedAttempt);

public static class SchemaSource
{
    /// <summary>An override file that loads wins over the shipped file. Nothing is written anywhere.</summary>
    public static ResolvedSchema Resolve(string shippedFolder, string? overrideFolder, string fileName)
    {
        ArgumentNullException.ThrowIfNull(shippedFolder);
        ArgumentNullException.ThrowIfNull(fileName);

        SchemaLoadResult? overrideAttempt = null;
        if (!string.IsNullOrEmpty(overrideFolder))
        {
            var overridePath = System.IO.Path.Combine(overrideFolder, fileName);
            overrideAttempt = SchemaLoader.Load(overridePath);
            if (overrideAttempt.Schema is not null)
            {
                return new ResolvedSchema(overrideAttempt.Schema, SchemaOrigin.Override, overridePath,
                    File.GetLastWriteTimeUtc(overridePath), overrideAttempt, null);
            }
        }

        var shippedPath = System.IO.Path.Combine(shippedFolder, fileName);
        var shippedAttempt = SchemaLoader.Load(shippedPath);
        return shippedAttempt.Schema is not null
            ? new ResolvedSchema(shippedAttempt.Schema, SchemaOrigin.Shipped, shippedPath,
                File.GetLastWriteTimeUtc(shippedPath), overrideAttempt, shippedAttempt)
            : new ResolvedSchema(null, SchemaOrigin.None, null, null, overrideAttempt, shippedAttempt);
    }
}
