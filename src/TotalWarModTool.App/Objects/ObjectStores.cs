using TotalWarModTool.Objects;
using TotalWarModTool.Tables;
using TotalWarModTool.Schema;

namespace TotalWarModTool.App;

/// <summary>
/// The stores of one schema, one game folder and one set of game table versions: tables, loc, the builder and the reference graph. The stores are thread-safe; the silent
/// check and the apply use them from a pool thread.
/// </summary>
internal sealed record ObjectStores(ResolvedSchema Schema, string InstallPath, GameTableVersions GameVersions, TableStore Tables, LocStore Loc, ObjectBuilder Builder, ReferenceGraph Graph)
{
    public static ObjectStores Create(ResolvedSchema schema, GameTableVersions gameVersions, string installPath)
    {
        var gameSchema = schema.Schema ?? throw new ArgumentException("The schema must be loaded.", nameof(schema));
        var graph = ReferenceGraph.Build(gameSchema, gameVersions);
        var tables = new TableStore(gameSchema, graph);
        var loc = new LocStore();
        return new ObjectStores(schema, installPath, gameVersions, tables, loc, new ObjectBuilder(graph, tables, loc), graph);
    }

    public void Clear()
    {
        Tables.Clear();
        Loc.Clear();
    }
}
