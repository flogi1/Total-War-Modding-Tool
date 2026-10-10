using System.Diagnostics;
using TotalWarModTool.GameProfiles;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
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
        var block = ObjectText.RenderBlock(view, null);
        _output.WriteLine($"Build of wh_main_emp_empire with a fresh store: {watch.ElapsedMilliseconds} ms");
        PrintView(view);
        _output.WriteLine($"Render: {block.Lines.Count} lines");

        Assert.True(watch.ElapsedMilliseconds < 2000, $"Build took {watch.ElapsedMilliseconds} ms");
        Assert.True(view.Sections.Count >= 20, $"{view.Sections.Count} sections");
        Assert.True(block.Lines.Count > 100, $"{block.Lines.Count} lines");
        Assert.Contains(block.Sections, section => section.Rows.Count > ObjectText.DefaultCollapseThreshold);
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

    private static readonly Lazy<IReadOnlyDictionary<string, CategoryDefinition>> Categories = new(() =>
        CategoryDefinitions.LoadAll(Path.Combine(RepoRoot.Find(), "profiles", "warhammer_3", "objects")).ByRootTable);

    private static CategoryDefinition? CategoryOf(string table) => Categories.Value.GetValueOrDefault(table);

    // Renders the block of the object, parses it twice (the second run is warm) and diffs it against the view it came from.
    private void RoundTrip(ObjectId id, CategoryDefinition? category, ObjectBuilder? builder = null)
    {
        var context = Shared.Value;
        builder ??= NewBuilder(context);
        var view = builder.Build(id, context.Order, null, CancellationToken.None);
        var block = ObjectText.RenderBlock(view, category);

        var watch = Stopwatch.StartNew();
        ObjectTextParser.Parse(block.Text, view, category, context.Graph);
        var coldMilliseconds = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        var parsed = ObjectTextParser.Parse(block.Text, view, category, context.Graph);
        var warmMilliseconds = watch.Elapsed.TotalMilliseconds;
        var changes = ObjectDiff.Compute(view, parsed, category, context.Graph, null);

        PrintView(view);
        _output.WriteLine($"Block of {id}: {block.Lines.Count} lines, {block.Text.Length} characters; parse {coldMilliseconds:F1} ms cold, {warmMilliseconds:F1} ms warm");
        foreach (var diagnostic in changes.Diagnostics.Take(10))
        {
            _output.WriteLine($"  diagnostic line {diagnostic.Line} {diagnostic.Severity} {diagnostic.Kind}: {diagnostic.Detail}");
        }

        foreach (var row in changes.Rows.Take(10))
        {
            _output.WriteLine($"  unexpected {row.Kind} {row.Table} line {row.Line}: {string.Join(" | ", row.Cells)}");
        }

        Assert.NotNull(view.Root);
        Assert.Empty(changes.Diagnostics);
        Assert.True(changes.IsEmpty, $"{changes.Rows.Count} row changes and {changes.Texts.Count} text changes without an edit");
        Assert.True(warmMilliseconds < 200, $"Parse took {warmMilliseconds:F1} ms");
    }

    [RealDataFact] public void The_barracks_block_round_trips_without_changes() =>
        RoundTrip(Id("building_levels_tables", "wh_main_emp_barracks_2"), CategoryOf("building_levels_tables"));

    [RealDataFact] public void The_empire_block_round_trips_and_parses_fast() =>
        RoundTrip(Id("factions_tables", "wh_main_emp_empire"), CategoryOf("factions_tables"));

    [RealDataFact] public void The_empire_block_diffs_fast()
    {
        var context = Shared.Value;
        var builder = NewBuilder(context);
        var category = CategoryOf("factions_tables");
        var view = builder.Build(Id("factions_tables", "wh_main_emp_empire"), context.Order, null, CancellationToken.None);
        var block = ObjectText.RenderBlock(view, category);

        ObjectDiff.Compute(view, ObjectTextParser.Parse(block.Text, view, category, context.Graph), category, context.Graph, null);
        var watch = Stopwatch.StartNew();
        var parsed = ObjectTextParser.Parse(block.Text, view, category, context.Graph);
        var changes = ObjectDiff.Compute(view, parsed, category, context.Graph, null);
        watch.Stop();

        _output.WriteLine($"Parse and diff of wh_main_emp_empire ({block.Lines.Count} lines, warm): {watch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Empty(changes.Diagnostics);
        Assert.True(changes.IsEmpty);
        Assert.True(watch.Elapsed.TotalMilliseconds < 50, $"Parse and diff took {watch.Elapsed.TotalMilliseconds:F1} ms");
    }

    // The first trait that has level rows whose key is the trait key plus "_" (the sub-objects of the copy rule).
    private static ObjectId FirstTraitWithLevels(ObjectBuilder builder, TableStore store)
    {
        var context = Shared.Value;
        var traits = store.Get("character_traits_tables", context.Order, CancellationToken.None);
        foreach (var row in traits.Rows.Take(3000))
        {
            var key = CellText.Format(traits.Definition!.Fields[0].Type, row.Cells[0]);
            var id = Id("character_traits_tables", key);
            var view = builder.Build(id, context.Order, null, CancellationToken.None);
            if (view.Sections.Any(s => s.SourceTable == "character_trait_levels_tables"
                && s.Rows.Any(r => r.Fields.Any(f => f.IsKey && f.Text.StartsWith(key + "_", StringComparison.Ordinal)))))
            {
                return id;
            }
        }

        throw new InvalidOperationException("No trait with prefix-keyed levels among the first 3000 traits.");
    }

    [RealDataFact] public void A_trait_block_round_trips()
    {
        var context = Shared.Value;
        var store = new TableStore(context.Schema, context.Graph);
        var builder = NewBuilder(context, store: store);

        RoundTrip(FirstTraitWithLevels(builder, store), CategoryOf("character_traits_tables"), builder);
    }

    [RealDataFact] public void Copying_the_barracks_follows_the_copy_rule()
    {
        var context = Shared.Value;
        var builder = NewBuilder(context);
        var source = builder.Build(Id("building_levels_tables", "wh_main_emp_barracks_2"), context.Order, null, CancellationToken.None);
        var newId = Id("building_levels_tables", "twmt_test_barracks");

        var result = ObjectCopy.Rename(source, newId, id => builder.Build(id, context.Order, null, CancellationToken.None), CategoryOf, context.Graph);
        var copy = result.Objects[0];
        var empty = new ObjectView(newId, null, [], [], [], context.Order, []);
        var changes = ObjectDiff.Compute(empty, copy, CategoryOf("building_levels_tables"), context.Graph, null);

        _output.WriteLine($"Copy of {source.Id}: {result.Objects.Count} object(s), {copy.Sections.Count} sections, {copy.Texts.Count} root texts");
        foreach (var section in source.Sections.Where(s => s.Rows.Count > 0))
        {
            var copied = copy.Sections.FirstOrDefault(s => s.SourceTable == section.SourceTable && s.FieldName == section.FieldName);
            _output.WriteLine($"  {section.SourceTable}.{section.FieldName}: {section.Rows.Count} rows, {copied?.Rows.Count ?? 0} copied");
        }

        foreach (var note in result.Notes)
        {
            _output.WriteLine($"  note {note.Kind} {note.Table}: {note.Count}");
        }

        foreach (var group in changes.Rows.GroupBy(r => r.Table))
        {
            _output.WriteLine($"  insert {group.Key}: {group.Count()}");
        }

        foreach (var diagnostic in changes.Diagnostics.Take(10))
        {
            _output.WriteLine($"  diagnostic line {diagnostic.Line} {diagnostic.Severity} {diagnostic.Kind}: {diagnostic.Detail}");
        }

        Assert.Single(result.Objects);
        Assert.Contains(copy.Sections, s => s.SourceTable == "building_culture_variants_tables");
        Assert.Contains(copy.Sections, s => s.SourceTable == "building_effects_junction_tables");
        Assert.DoesNotContain(copy.Sections, s => s.SourceTable == "cai_construction_system_synergies_tables");
        Assert.Contains(result.Notes, n => n is { Kind: CopyNoteKind.ForeignKeyRows, Table: "technologies_tables" });
        Assert.All(changes.Rows, r => Assert.Equal(RowChangeKind.Insert, r.Kind));
        Assert.DoesNotContain(changes.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [RealDataFact] public void Copying_a_trait_copies_its_levels_as_sub_objects()
    {
        var context = Shared.Value;
        var store = new TableStore(context.Schema, context.Graph);
        var builder = NewBuilder(context, store: store);
        var traitId = FirstTraitWithLevels(builder, store);
        var source = builder.Build(traitId, context.Order, null, CancellationToken.None);
        var newId = Id("character_traits_tables", "twmt_test_" + traitId.KeyValues[0]);

        var result = ObjectCopy.Rename(source, newId, id => builder.Build(id, context.Order, null, CancellationToken.None), CategoryOf, context.Graph);

        var prefix = traitId.KeyValues[0] + "_";
        var levels = source.Sections.Where(s => s.SourceTable == "character_trait_levels_tables").SelectMany(s => s.Rows)
            .Count(r => r.Fields.Any(f => f.IsKey && f.Text.StartsWith(prefix, StringComparison.Ordinal)));
        _output.WriteLine($"Trait {traitId}: {levels} prefix level row(s), {result.Objects.Count} copied object(s)");
        foreach (var sub in result.Objects.Skip(1))
        {
            _output.WriteLine($"  {sub.Id}: {string.Join(", ", sub.Sections.Select(s => s.SourceTable + " " + s.Rows.Count))}");
        }

        foreach (var note in result.Notes)
        {
            _output.WriteLine($"  note {note.Kind} {note.Table}: {note.Count}");
        }

        Assert.Equal(1 + levels, result.Objects.Count);
        Assert.All(result.Objects.Skip(1), sub =>
        {
            Assert.Equal("character_trait_levels_tables", sub.Id.Table);
            Assert.StartsWith(newId.KeyValues[0] + "_", sub.Id.KeyValues[0], StringComparison.Ordinal);
        });
    }

    [RealDataFact] public void No_value_holds_a_control_character_or_the_dialect_escape()
    {
        var context = Shared.Value;
        var store = new TableStore(context.Schema, context.Graph, MergeTestCacheBytes);
        long cells = 0;
        long trailing = 0;
        long leading = 0;
        long escapeSequences = 0;
        long dialectEscapes = 0;
        long controlCharacters = 0;
        var examples = new List<string>();

        void Note(string kind, string table, string field, string value)
        {
            if (examples.Count < 5)
            {
                examples.Add($"{kind} {table}.{field}: '{CellText.ShortenForDetail(value)}'");
            }
        }

        foreach (var name in context.Schema.Tables.Keys)
        {
            var table = store.Get(name, context.Order, CancellationToken.None);
            if (table.Definition is null)
            {
                continue;
            }

            foreach (var row in table.Rows)
            {
                for (var i = 0; i < table.Definition.Fields.Count; i++)
                {
                    var field = table.Definition.Fields[i];
                    if (field.Type is not (FieldType.StringU8 or FieldType.StringU16 or FieldType.OptionalStringU8))
                    {
                        continue;
                    }

                    cells++;
                    var text = row.Cells[i] as string ?? string.Empty;
                    if (text.Length > 0 && text[^1] is ' ' or '\t')
                    {
                        trailing++;
                        Note("trailing", name, field.Name, text);
                    }

                    if (text.Length > 0 && text[0] == ' ')
                    {
                        leading++;
                    }

                    if (text.Contains("\\t", StringComparison.Ordinal) || text.Contains("\\n", StringComparison.Ordinal))
                    {
                        escapeSequences++;
                    }

                    // The dialect escape (two backslashes and a t or n): the block refuses it as a control character.
                    if (text.Contains("\\\\t", StringComparison.Ordinal) || text.Contains("\\\\n", StringComparison.Ordinal))
                    {
                        dialectEscapes++;
                        Note("dialect escape", name, field.Name, text);
                    }

                    if (text.Contains('\t') || text.Contains('\n'))
                    {
                        controlCharacters++;
                        Note("control", name, field.Name, text);
                    }
                }
            }
        }

        var loc = new LocStore().Get(context.Order, CancellationToken.None);
        var locKeys = loc.KeysWithPrefix(string.Empty);
        long locWithTab = 0;
        long locTrailing = 0;
        long locWithCarriageReturn = 0;
        foreach (var key in locKeys)
        {
            loc.TryGet(key, out var entry);
            if (entry!.Text.Contains('\t'))
            {
                locWithTab++;
                Note("loc tab", "loc", key, entry.Text);
            }

            if (entry.Text.Contains('\r'))
            {
                locWithCarriageReturn++;
                Note("loc cr", "loc", key, entry.Text);
            }

            if (entry.Text.Length > 0 && entry.Text[^1] == ' ')
            {
                locTrailing++;
            }
        }

        _output.WriteLine($"Text cells: {cells}; ending with space or tab: {trailing}; starting with a space: {leading}; holding a literal backslash-t or backslash-n: {escapeSequences}; holding a real tab or line break: {controlCharacters}");
        _output.WriteLine($"Loc texts: {locKeys.Count}; with a tab: {locWithTab}; with a carriage return: {locWithCarriageReturn}; ending with a space: {locTrailing}");
        foreach (var example in examples)
        {
            _output.WriteLine("  " + example);
        }

        // Assumption 4 as the spec first worded it ("no value ends with a space") does not hold: the block trims the end of a value and
        // ObjectDiff treats a stored value that only differs by that as unchanged, so the trailing count is a measurement, not an assertion.
        // What the block cannot carry at all would be a real tab or line break and the dialect escape inside a value.
        Assert.Equal(0, controlCharacters);
        Assert.Equal(0, dialectEscapes);
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
