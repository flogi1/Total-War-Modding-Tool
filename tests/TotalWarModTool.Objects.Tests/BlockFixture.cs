using TotalWarModTool.Schema;

namespace TotalWarModTool.Objects.Tests;

/// <summary>
/// The mini game of the block tests: a building level with effects (keyed by effect), units (no key, a junction by shape, shown as a
/// sentence), upgrades (a junction with two references to the root), and a hidden table.
/// </summary>
internal static class BlockFixture
{
    public static readonly TableSource Vanilla = new(SourceKind.Vanilla, "db.pack", "db.pack");
    public static readonly TableSource Mod = new(SourceKind.Mod, "mod.pack", "mod.pack");
    public static readonly LoadOrder Order = new([Vanilla, Mod], null, []);

    public const string Root = "building_levels_tables";

    private static readonly (string Table, string Column) RootKey = (Root, "level_name");

    public static readonly ReferenceGraph Graph = ReferenceGraph.Build(TestSchema.Build(
        (Root, 1,
        [
            TestSchema.Field("level_name", key: true),
            TestSchema.Field("chain", reference: ("building_chains_tables", "key")),
            TestSchema.Field("create_cost", FieldType.I32),
            TestSchema.Field("note"),
        ]),
        ("effects_tables", 1,
        [
            TestSchema.Field("building", reference: RootKey),
            TestSchema.Field("effect", key: true),
            TestSchema.Field("scope"),
            TestSchema.Field("value", FieldType.F32),
        ]),
        ("units_tables", 1,
        [
            TestSchema.Field("building", reference: RootKey),
            TestSchema.Field("unit", reference: ("units_def_tables", "key")),
            TestSchema.Field("xp", FieldType.I32),
        ]),
        ("upgrades_tables", 1,
        [
            TestSchema.Field("from", reference: RootKey),
            TestSchema.Field("to", reference: RootKey),
        ]),
        ("cai_x_tables", 1, [TestSchema.Field("building", reference: RootKey), TestSchema.Field("key", key: true)]),
        ("variants_tables", 1,
        [
            TestSchema.Field("building", reference: RootKey),
            TestSchema.Field("culture", key: true),
            TestSchema.Field("variant", key: true),
            TestSchema.Field("icon"),
        ]),
        ("notes_tables", 1, [TestSchema.Field("building", reference: RootKey), TestSchema.Field("a"), TestSchema.Field("b")]),
        // Junction by shape (two references, four fields) that also has a key field of its own.
        ("perks_tables", 1,
        [
            TestSchema.Field("building", reference: RootKey),
            TestSchema.Field("perk", reference: ("perk_defs_tables", "key")),
            TestSchema.Field("scope", key: true),
            TestSchema.Field("rank", FieldType.I32),
        ]),
        ("bonus_tables", 1,
        [
            TestSchema.Field("building", key: true, reference: RootKey),
            TestSchema.Field("culture", key: true),
            TestSchema.Field("other", key: true),
            TestSchema.Field("amount", FieldType.I32),
            TestSchema.Field("note"),
        ]),
        ("building_chains_tables", 1, [TestSchema.Field("key", key: true)]),
        ("units_def_tables", 1, [TestSchema.Field("key", key: true)])));

    public static readonly CategoryDefinition Category = new(Root, "cat",
    [
        new SectionRule("units_tables", null, null, SectionMode.Sentence, "allows {unit}", false),
        new SectionRule("cai_x_tables", null, null, SectionMode.List, null, true),
    ]);

    public static ObjectId Id(string table, string key) => new(table, [key]);

    public static RowProvenance From(TableSource source, params TableSource[] overridden) => new(source, overridden, false, false);

    public static FieldValue Val(string name, string text, FieldType type = FieldType.StringU8, bool key = false, string? refTable = null,
        string? refColumn = null, ObjectId? target = null, string? loc = null) =>
        new(name, type, text, refTable is null ? null : new TableReference(refTable, refColumn ?? "key"),
            target is null ? ReferenceResolution.None : ReferenceResolution.Found, target, loc, key);

    /// <summary>level_name b1 (key), chain chain1 (found, "Chain One"), create_cost 100, note empty.</summary>
    public static ObjectRow RootRow(string key = "b1", string cost = "100", string note = "", string chain = "chain1") => new(
        [
            Val("level_name", key, key: true),
            Val("chain", chain, refTable: "building_chains_tables", target: Id("building_chains_tables", chain), loc: "Chain One"),
            Val("create_cost", cost, FieldType.I32),
            Val("note", note),
        ],
        From(Vanilla), Id(Root, key), null);

    public static ObjectRow EffectRow(string effect, string scope = "province", string value = "10", RowProvenance? provenance = null) => new(
        [
            Val("building", "b1", refTable: Root, refColumn: "level_name"),
            Val("effect", effect, key: true),
            Val("scope", scope),
            Val("value", value, FieldType.F32),
        ],
        provenance ?? From(Vanilla), Id("effects_tables", effect), null);

    /// <summary>A row of the keyless units table; unit is the other end (the table has two references).</summary>
    public static ObjectRow UnitRow(string unit, string xp = "2")
    {
        var other = Val("unit", unit, refTable: "units_def_tables");
        return new ObjectRow([Val("building", "b1", refTable: Root, refColumn: "level_name"), other, Val("xp", xp, FieldType.I32)], From(Vanilla), null, other);
    }

    public static ObjectRow UpgradeRow(string from, string to, string pointing = "from")
    {
        var fromField = Val("from", from, refTable: Root, refColumn: "level_name");
        var toField = Val("to", to, refTable: Root, refColumn: "level_name");
        return new ObjectRow([fromField, toField], From(Vanilla), null, pointing == "from" ? toField : fromField);
    }

    public static ObjectSection Section(string table, string field, bool junction, params ObjectRow[] rows) => new(table, field, junction, rows);

    public static ObjectView View(ObjectRow? root, IReadOnlyList<ObjectSection>? sections = null, IReadOnlyList<LocEntry>? texts = null,
        string key = "b1", IReadOnlyList<ObjectWarning>? warnings = null) =>
        new(Id(Root, key), root, texts ?? [], sections ?? [], warnings ?? [], Order, [Root]);

    /// <summary>The root with one effect e1, one unit u1 (xp 2), one upgrade to t1, a root text and a row text.</summary>
    public static ObjectView Full() => View(RootRow(),
        [
            Section("effects_tables", "building", false, EffectRow("e1")),
            Section("units_tables", "building", true, UnitRow("u1")),
            Section("upgrades_tables", "from", true, UpgradeRow("b1", "t1")),
        ],
        [
            new LocEntry("building_levels", "onscreen_name", "building_levels_onscreen_name_b1", "Barracks", Mod, Id(Root, "b1")),
            new LocEntry("effects", "description", "effects_description_e1", "More gold", Vanilla, Id("effects_tables", "e1")),
        ]);

    /// <summary>
    /// One of every kind of row and text: a list row with two key fields (one empty), a keyless list row, a sentence row, a junction row, a root
    /// text, a row text, an empty root field and a value that looks like syntax.
    /// </summary>
    public static ObjectView EveryKind()
    {
        var variantId = new ObjectId("variants_tables", ["c1", ""]);
        var variant = new ObjectRow(
            [
                Val("building", "b1", refTable: Root, refColumn: "level_name"),
                Val("culture", "c1", key: true),
                Val("variant", "", key: true),
                Val("icon", "i = 1  x"),
            ],
            From(Vanilla), variantId, null);
        var note = new ObjectRow([Val("building", "b1", refTable: Root, refColumn: "level_name"), Val("a", "x"), Val("b", "y")], From(Vanilla), null, null);
        return View(RootRow(note: "a = b  c"),
            [
                Section("variants_tables", "building", false, variant),
                Section("notes_tables", "building", false, note),
                Section("units_tables", "building", true, UnitRow("u1")),
                Section("upgrades_tables", "to", true, UpgradeRow("t1", "b1", "to")),
                Section("effects_tables", "building", false, EffectRow("e1")),
            ],
            [
                new LocEntry("building_levels", "onscreen_name", "building_levels_onscreen_name_b1", "He said \"hi\" \\ x", Mod, Id(Root, "b1")),
                new LocEntry("variants", "name", "variants_name_c1", "Line one\nLine two", Vanilla, variantId),
            ]);
    }

    public static string[] HeadAndRoot() =>
        ["building_levels b1", "  level_name = b1", "  chain = chain1", "  create_cost = 100", "  note = "];

    public static string Lines(params string[] lines) => string.Join("\n", lines);
}
