namespace TotalWarModTool.Tables;

public static class DbTablePath
{
    private const string DbFolder = "db";

    /// <summary>"db/units_tables/data__" gives "units_tables". False (and an empty name) for anything that is not db/&lt;table&gt;/&lt;file&gt;.</summary>
    public static bool TryGetTableName(string entryPath, out string tableName)
    {
        ArgumentNullException.ThrowIfNull(entryPath);

        var parts = entryPath.Split('/');
        if (parts.Length == 3
            && string.Equals(parts[0], DbFolder, StringComparison.OrdinalIgnoreCase)
            && parts[1].Length > 0
            && parts[2].Length > 0)
        {
            tableName = parts[1];
            return true;
        }

        tableName = string.Empty;
        return false;
    }
}
