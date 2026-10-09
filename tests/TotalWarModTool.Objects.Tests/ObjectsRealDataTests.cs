using System.Diagnostics;
using TotalWarModTool.GameProfiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Workspace;
using Xunit.Abstractions;

namespace TotalWarModTool.Objects.Tests;

/// <summary>Checks the object view against the installed game. All tests only read; they print their measurements to the test output.</summary>
public class ObjectsRealDataTests
{
    private const long MergeTestCacheBytes = 600L * 1024 * 1024;

    private readonly ITestOutputHelper _output;

    public ObjectsRealDataTests(ITestOutputHelper output) => _output = output;

    private sealed record Context(GameSchema Schema, ReferenceGraph Graph, LoadOrder Order);

    private static readonly Lazy<Context> Shared = new(CreateContext);

    private static Context CreateContext()
    {
        var root = RepoRoot.Find();
        var schema = SchemaLoader.Load(Path.Combine(root, "schemas", "schema_wh3.ron")).Schema!;
        var profile = GameProfileLoader.LoadAll(Path.Combine(root, "profiles")).Profiles.Single(p => p.Id == "warhammer_3");
        var catalog = PackCatalog.Scan(profile, RealData.Wh3Dir!, RealData.Wh3Workshop, null, CancellationToken.None);
        var order = LoadOrderResolver.Resolve(new LoadOrderRequest(profile, RealData.Wh3Dir!, catalog, null, null, null));
        return new Context(schema, ReferenceGraph.Build(schema), order);
    }

    private static ObjectBuilder NewBuilder(Context context, long maxCacheBytes = TableStore.DefaultMaxCacheBytes, TableStore? store = null) =>
        new(context.Graph, store ?? new TableStore(context.Schema, context.Graph, maxCacheBytes), new LocStore());

    private static ObjectId Id(string table, string key) => new(table, [key]);

    private void PrintOrder(LoadOrder order)
    {
        _output.WriteLine($"Load order: {order.Sources.Count} sources ({order.Sources.Count(s => s.Kind == SourceKind.Mod)} mods), loc pack {order.LocPack?.Name ?? "none"}, {order.Warnings.Count} warnings");
        foreach (var warning in order.Warnings)
        {
            _output.WriteLine($"  load order warning {warning.Problem}: {warning.Name}: {warning.Detail}");
        }
    }

    private void PrintView(ObjectView view)
    {
        _output.WriteLine($"Object {view.Id}: root {(view.Root is null ? "missing" : "found")}, {view.Texts.Count} texts, {view.Sections.Count} sections, {view.Warnings.Count} warnings");
        foreach (var section in view.Sections)
        {
            _output.WriteLine($"  section {section.SourceTable}.{section.FieldName}: {section.Rows.Count} rows{(section.IsJunction ? " (junction)" : string.Empty)}");
        }

        foreach (var group in view.Warnings.GroupBy(w => w.Kind))
        {
            _output.WriteLine($"  warnings {group.Key}: {group.Count()}");
        }
    }

    [RealDataFact] public void The_barracks_have_effects_units_and_upgrades()
    {
        var context = Shared.Value;
        PrintOrder(context.Order);
        var builder = NewBuilder(context);

        var view = builder.Build(Id("building_levels_tables", "wh_main_emp_barracks_2"), context.Order, null, CancellationToken.None);
        PrintView(view);

        Assert.NotNull(view.Root);
        Assert.Contains(view.Sections, s => s.SourceTable == "building_effects_junction_tables");
        Assert.Contains(view.Sections, s => s.SourceTable == "building_units_allowed_tables");
        Assert.Contains(view.Sections, s => s.SourceTable == "building_upgrades_junction_tables" && s.FieldName == "from");
        Assert.Contains(view.Sections, s => s.SourceTable == "building_upgrades_junction_tables" && s.FieldName == "to");
        Assert.Contains(view.Texts, t => t.Key.StartsWith("building_culture_variants_name_", StringComparison.Ordinal));
        Assert.DoesNotContain(view.Warnings, w => w.Kind == ObjectWarningKind.RootNotFound);
        Assert.DoesNotContain(view.Warnings, w => w.Kind == ObjectWarningKind.TableNotInSchema);
    }

    [RealDataFact] public void The_empire_builds_in_under_two_seconds()
    {
        var context = Shared.Value;
        var builder = NewBuilder(context);

        var watch = Stopwatch.StartNew();
        var view = builder.Build(Id("factions_tables", "wh_main_emp_empire"), context.Order, null, CancellationToken.None);
        watch.Stop();
        var lines = ObjectText.Render(view, null, new RenderOptions(new HashSet<int>()));
        _output.WriteLine($"Build of wh_main_emp_empire with a fresh store: {watch.ElapsedMilliseconds} ms");
        PrintView(view);
        _output.WriteLine($"Render: {lines.Count} lines");

        Assert.True(watch.ElapsedMilliseconds < 2000, $"Build took {watch.ElapsedMilliseconds} ms");
        Assert.True(view.Sections.Count >= 20, $"{view.Sections.Count} sections");
        Assert.True(lines.Count > 100, $"{lines.Count} lines");
        Assert.Contains(lines, line => line.Spans.Any(span => span.Kind == SpanKind.Collapsed));
    }

    [RealDataFact] public void A_trait_has_a_display_name_somewhere()
    {
        var context = Shared.Value;
        var builder = NewBuilder(context);

        var view = builder.Build(Id("character_traits_tables", "wh_main_trait_all_personality_all_hates_dwarfs"), context.Order, null, CancellationToken.None);
        PrintView(view);

        var levels = view.Sections.Where(s => s.SourceTable == "character_trait_levels_tables").ToList();
        var levelNames = levels
            .SelectMany(s => s.Rows)
            .Where(row => row.RowId is not null)
            .Select(row => (row.RowId!, Name: builder.TryDisplayName(row.RowId!, context.Order, CancellationToken.None)))
            .ToList();
        foreach (var (rowId, name) in levelNames)
        {
            _output.WriteLine($"  level {rowId}: display name {(name is null ? "none" : "'" + name + "'")}");
        }

        _output.WriteLine($"Texts of the trait itself: {view.Texts.Count}; level rows with a display name: {levelNames.Count(l => l.Name is not null)} of {levelNames.Count}");
        foreach (var text in view.Texts.Take(10))
        {
            _output.WriteLine($"  text {text.Table}.{text.Column}: {text.Key}");
        }

        Assert.NotNull(view.Root);
        Assert.True(view.Texts.Count > 0 || levelNames.Any(l => l.Name is not null),
            "Neither the trait nor one of its level rows has a loc text.");
    }

    [RealDataFact] public void Every_table_of_the_load_order_merges_without_exception()
    {
        var context = Shared.Value;
        PrintOrder(context.Order);
        var store = new TableStore(context.Schema, context.Graph, MergeTestCacheBytes);
        var hasMods = context.Order.Sources.Any(s => s.Kind == SourceKind.Mod);

        var watch = Stopwatch.StartNew();
        var present = 0;
        long rows = 0;
        long olderLayoutRows = 0;
        var problemsByKind = new Dictionary<SourceProblemKind, int>();
        foreach (var name in context.Schema.Tables.Keys)
        {
            var table = store.Get(name, context.Order, CancellationToken.None);
            if (table.Rows.Count > 0 || table.Problems.Count > 0)
            {
                present++;
            }

            rows += table.Rows.Count;
            olderLayoutRows += table.Rows.Count(r => r.Provenance.OlderLayout);
            foreach (var problem in table.Problems)
            {
                problemsByKind[problem.Kind] = problemsByKind.GetValueOrDefault(problem.Kind) + 1;
            }
        }

        watch.Stop();
        _output.WriteLine($"Merged {context.Schema.Tables.Count} schema tables ({present} with rows or problems), {rows} rows in {watch.ElapsedMilliseconds} ms");
        _output.WriteLine($"Cache: {store.CachedTables} tables, {store.CachedBytes / (1024 * 1024)} MB; rows with an older layout: {olderLayoutRows}");
        foreach (var (kind, count) in problemsByKind.OrderBy(p => p.Key))
        {
            _output.WriteLine($"  problems {kind}: {count}");
        }

        if (problemsByKind.Count == 0)
        {
            _output.WriteLine("  no problems");
        }

        Assert.True(present > 0);
        Assert.True(rows > 0);
        if (!hasMods)
        {
            Assert.Equal(0, olderLayoutRows);
        }

        Assert.True(watch.ElapsedMilliseconds < 30_000, $"Merging took {watch.ElapsedMilliseconds} ms");
    }

    [RealDataFact] public void The_second_build_of_a_building_does_not_decode_again()
    {
        var context = Shared.Value;
        var builder = NewBuilder(context);

        var watch = Stopwatch.StartNew();
        var first = builder.Build(Id("building_levels_tables", "wh_main_emp_barracks_1"), context.Order, null, CancellationToken.None);
        var firstMilliseconds = watch.ElapsedMilliseconds;
        watch.Restart();
        var second = builder.Build(Id("building_levels_tables", "wh_main_emp_barracks_2"), context.Order, null, CancellationToken.None);
        watch.Stop();
        _output.WriteLine($"First build: {firstMilliseconds} ms, second build: {watch.ElapsedMilliseconds} ms");
        PrintView(first);
        PrintView(second);

        Assert.NotNull(first.Root);
        Assert.NotNull(second.Root);
        Assert.True(watch.ElapsedMilliseconds < 300, $"The second build took {watch.ElapsedMilliseconds} ms");
    }
}
