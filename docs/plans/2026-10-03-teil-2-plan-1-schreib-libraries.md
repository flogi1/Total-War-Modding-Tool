# Teil 2 Plan 1: Schreib-Libraries – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Die Lese-Libraries bekommen ihr Gegenstück: Tabellen und Loc-Dateien binär kodieren, beide als TSV im RPFM-Dialekt schreiben und lesen, und ein PFH5-Pack als Stream schreiben.

**Architecture:** `Tables` bekommt `DbTableEncoder`, `LocEncoder` und `TableTsv`; `PackFiles` bekommt `PackWriter`. Alles ist UI-frei, loggt nicht und kennt keine UI-Texte. Die Encoder und `TableTsv` arbeiten nur im Speicher. `PackWriter` schreibt genau die Datei, deren Pfad er bekommt; temp-Datei und Umbenennen übernimmt in Plan 2 das Modul `Projects`. Die App bleibt in diesem Plan unverändert.

**Tech Stack:** C# 12, .NET 8, xUnit 2.9.2. Kein neues NuGet-Paket.

**Spec:** `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md`. Formatfakten in 3.1 bis 3.4, Dialekt in 5.3, Pack-Format in 5.7. Dazu `docs/specs/2026-10-02-fundament.md` 3.2 und 3.4 (Binärformate) und `CLAUDE.md`.

**Branch:** `feature/teil-2-schreib-libraries` (von `master`, Stand `8282336` oder neuer). Baseline vor Task 1: `dotnet test TotalWarModTool.slnx` mit beiden Umgebungsvariablen ergibt 665 bestandene Tests, 0 übersprungen, Build ohne Warnung.

**Echte Daten für Tests:** `TWMT_WH3_DIR` (Installationsordner von WH3) und `TWMT_RPFM_CLI` (voller Pfad zu `rpfm_cli.exe`), nur für den Testlauf gesetzt. Ohne sie werden die Tests auf echten Daten übersprungen. Wer eine Task umsetzt, lässt sie **mit** beiden Variablen laufen und nennt im Bericht, wie viele dieser Tests liefen.

## Global Constraints

- Code, Bezeichner, Code-Kommentare und Commit-Messages Englisch.
- Kein neues NuGet-Paket. Build-Einstellungen nur in `Directory.Build.props`. `TreatWarningsAsErrors` ist an.
- Referenzen bleiben: `Tables` → `Schema`. `PackFiles` referenziert kein eigenes Projekt. Keine der beiden Libraries referenziert `Logging`, `Localization`, `Storage`, `Settings`, `Operations` oder die App.
- **Bestehende öffentliche Signaturen bleiben.** `DecodedTable`, `LocRow`, `PackReader` und alle vorhandenen Typen ändern sich nicht; es kommt nur Neues dazu.
- **Ein Modell.** Encoder und `TableTsv` nutzen das Zeilenmodell des Dekoders: `object?[]` in Feldreihenfolge mit `bool`, `short`, `int`, `long`, `float`, `double`, `string` (Farbe als Hex, Texte, optionaler Text leer wenn abwesend).
- **Diagnosen statt Exceptions für Daten.** `TableTsv.Read*` und `PackWriter` melden Probleme als Ergebnis (Enum plus `Detail`, kurzer technischer Text auf Englisch, kein UI-Text). Nach außen dringen nur `OperationCanceledException` und Argumentfehler. Die Encoder bekommen ein schon geprüftes Modell und werfen `ArgumentException` bei einem Modellfehler.
- Kein `catch (Exception)`. Gefangen werden benannte Typen.
- **Schreiben nur dorthin, wohin der Aufrufer zeigt.** `PackWriter.Write` schreibt genau `targetPath`. Tests schreiben nur in `TempDir`. Nichts in diesem Plan schreibt in den Spielordner, den Workshop-Ordner oder den RPFM-Ordner.
- Kein Zurücklassen halber Dateien: Scheitert `PackWriter.Write` oder wird es abgebrochen, existiert `targetPath` danach nicht.
- Abbruch: Jede Schleife über Einträge und über Kopierblöcke prüft das `CancellationToken`.
- Invariante Kultur für alle Zahlen im Text. Keine Zeit-Assertions. Zeit kommt als Parameter herein, nie aus `DateTime.Now`.
- Keine statischen Singletons, kein veränderlicher statischer Zustand.
- Test-first: failing Test → RED prüfen → implementieren → GREEN → Commit. Die ganze Suite bleibt grün.
- Synthetische Tests bauen ihre Bytes mit `TableBytes` und `PackBuilder`. Tests auf echten Daten prüfen Invarianten statt fester Zahlen und tragen `[RealDataFact]` oder `[RpfmFact]`.
- Ein Commit pro Task, nach GREEN. Vor jedem Commit `git add -A`. Die Attribution-Zeile der laufenden Sitzung gehört ans Ende der Commit-Message.
- Die Tests in diesem Plan legen Namen und Erwartungen fest. Scheitert ein Test nur an einem Compiler-Detail, wird er minimal angepasst, ohne die Erwartung zu ändern.
- Subagenten lesen und schreiben nichts unter `%APPDATA%\TotalWarModTool` und `%LocalAppData%\TotalWarModTool`.

## Review Focus

Diese Fälle nennt die Spec nicht einzeln, sie treffen Nutzer aber zuerst. Jeder hat einen Test in der genannten Task.

1. **TSV von außen angefasst** (Git mit `autocrlf`, Editor, Excel): `\r\n`, BOM am Anfang, fehlender Zeilenumbruch am Ende, Spalten umsortiert. Erwartung: wird gelesen. → Task 2 (`Read_accepts_crlf_bom_missing_final_newline_and_any_column_order`).
2. **TSV von Hand kaputt gemacht** (Zelle gelöscht, Buchstabe in Zahlenspalte, Leerzeile am Ende). Erwartung: Diagnose mit der richtigen Zeilennummer, nie eine Exception, nie ein halbes Ergebnis. → Task 2 (`Read_reports_the_problem_with_its_line`).
3. **Grenzwerte der Typen** (`int.MinValue`, `long.MaxValue`, `NaN`, `Infinity`, `-0`, sehr kleine Kommazahlen, Text mit 65.535 Bytes und einer mehr). Erwartung: exakter Round-Trip oder Diagnose. → Task 1 (`String_longer_than_65535_bytes_throws`), Task 2 (`Extreme_values_round_trip`, `Read_reports_the_problem_with_its_line`).
4. **Zeichen außerhalb von ASCII** (Umlaute, CJK, Emoji) in Texten und in Pack-Pfaden; Länge zählt in Bytes bzw. UTF-16-Einheiten, nicht in Zeichen. Erwartung: exakter Round-Trip. → Task 1 (`Encodes_every_supported_field_type_like_the_decoder_reads_it`), Task 3 (`Written_pack_is_read_back`).
5. **Quelle ändert sich während des Schreibens** (Datei schrumpft, wächst oder ist gesperrt, Platte voll, Abbruch). Erwartung: Diagnose, keine halbe Pack-Datei. → Task 3 (`A_source_with_the_wrong_length_fails_and_leaves_no_file`, `Cancel_leaves_no_file`).

---

### Task 1: `DbTableEncoder`, `LocEncoder`, `DbTableDecoder.ReadGuid`

**Files:**
- Create: `src/TotalWarModTool.Tables/DbTableEncoder.cs`, `src/TotalWarModTool.Tables/LocEncoder.cs`
- Modify: `src/TotalWarModTool.Tables/DbTableDecoder.cs` (nur neue Methode `ReadGuid`)
- Test: `tests/TotalWarModTool.Tables.Tests/DbTableEncoderTests.cs`, `LocEncoderTests.cs`, `EncoderRealDataTests.cs`

**Interfaces:**
- Consumes: `DbTableDecoder.Decode`, `LocDecoder.Decode`, `DecodedTable`, `LocRow`, `TableDefinition`, `FieldType`; Testhelfer `TableBytes`, `RealData`, `RepoRoot`.
- Produces:

```csharp
namespace TotalWarModTool.Tables;

public static class DbTableEncoder
{
    /// rows: the model of DbTableDecoder. guid null writes no GUID block. Throws ArgumentException for a model error.
    public static byte[] Encode(TableDefinition definition, int version, string? guid, IReadOnlyList<object?[]> rows);
}

public static class LocEncoder
{
    public static byte[] Encode(IReadOnlyList<LocRow> rows);   // ArgumentException when a key or text has more than 65535 UTF-16 units
}

public static class DbTableDecoder
{
    /// The GUID text of the header; null when the data has no GUID block or the block is cut off. Never throws for the data.
    public static string? ReadGuid(byte[] data);
}
```

Festlegungen (Spec 3.1):
- Kopf in dieser Reihenfolge: GUID-Block (`FD FE FC FF`, u16 Zahl der UTF-16-Einheiten, Text) nur wenn `guid` nicht `null` ist (ein leerer Text ist erlaubt); Versionsblock (`FC FD FE FF`, i32) nur wenn `version != 0`; ein Byte `01`; u32 Zeilenzahl.
- Zellen: `Boolean` ein Byte 0/1; `I16`, `I32`, `I64`, `F32`, `F64` little-endian; `ColourRgb` Hex-Text (1 bis 8 Ziffern) als u32; `StringU8` u16 Bytezahl plus UTF-8; `StringU16` u16 Einheitenzahl plus UTF-16LE; `OptionalStringU8` leerer Text → ein Byte `00`, sonst `01` plus `StringU8`.
- `ArgumentException` mit Zeile und Feldname bei: Zeile mit falscher Länge, Zelle mit falschem CLR-Typ oder `null`, Feld vom Typ `Unsupported`, Text über 65.535 Bytes (U8) bzw. Einheiten (U16), Farbe kein Hex.
- Loc: Marker `FF FE 4C 4F 43 00`, i32 Version 1, u32 Zeilenzahl, pro Zeile Schlüssel und Text als `StringU16`, ein Byte 0/1.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class DbTableEncoderTests
{
    private const string TestGuid = "46e87609-af6e-4857-bea4-f29b4fbd55fd";
    private static FieldDefinition F(string name, FieldType type) => new(name, type, type.ToString(), false, null);
    private static GameSchema Schema(int version, params FieldDefinition[] fields) =>
        new(new Dictionary<string, IReadOnlyList<TableDefinition>> { ["t_tables"] = [new TableDefinition(version, fields)] });

    private static readonly FieldDefinition[] AllTypes =
    [
        F("key", FieldType.StringU8), F("flag", FieldType.Boolean), F("small", FieldType.I16), F("count", FieldType.I32),
        F("big", FieldType.I64), F("ratio", FieldType.F32), F("exact", FieldType.F64), F("tint", FieldType.ColourRgb),
        F("wide", FieldType.StringU16), F("maybe", FieldType.OptionalStringU8),
    ];

    [Fact] public void Encodes_every_supported_field_type_like_the_decoder_reads_it()
    {
        var data = new TableBytes().Guid(TestGuid).Version(3).Rows(2)
            .Str8("ünit_日本_😀").Bool(true).I16(short.MinValue).I32(int.MaxValue).I64(long.MinValue).F32(0.1f).F64(double.Epsilon).Colour(0x004080FF).Str16("wide 😀").OptStr8("yes")
            .Str8("").Bool(false).I16(7).I32(-1).I64(-9).F32(float.NaN).F64(double.NegativeInfinity).Colour(0x01FFFFFF).Str16("").OptStr8(null)
            .Build();
        var t = DbTableDecoder.Decode(data, "t_tables", Schema(3, AllTypes));
        Assert.Equal(TableDecodeStatus.Ok, t.Status);
        Assert.Equal(data, DbTableEncoder.Encode(t.Definition!, t.Version, TestGuid, t.Rows));
    }

    [Fact] public void Version_0_writes_no_version_marker_and_null_guid_no_guid_block()
    {
        var def = new TableDefinition(0, [F("n", FieldType.I32)]);
        Assert.Equal(new TableBytes().Rows(1).I32(5).Build(), DbTableEncoder.Encode(def, 0, null, [[5]]));
        Assert.Equal(new TableBytes().Guid("").Rows(0).Build(), DbTableEncoder.Encode(def, 0, "", []));
    }

    [Fact] public void Empty_optional_text_is_written_as_absent()
    {
        var def = new TableDefinition(1, [F("maybe", FieldType.OptionalStringU8)]);
        Assert.Equal(new TableBytes().Version(1).Rows(1).OptStr8(null).Build(), DbTableEncoder.Encode(def, 1, null, [[""]]));
    }

    [Fact] public void String_longer_than_65535_bytes_throws()
    {
        var def = new TableDefinition(1, [F("s", FieldType.StringU8)]);
        Assert.Equal(TableDecodeStatus.Ok, DbTableDecoder.Decode(DbTableEncoder.Encode(def, 1, null, [[new string('a', 65535)]]), "t_tables", Schema(1, def.Fields.ToArray())).Status);
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [[new string('a', 65536)]]));
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [[new string('ü', 32768)]]));   // 65536 bytes
        var wide = new TableDefinition(1, [F("s", FieldType.StringU16)]);
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(wide, 1, null, [[new string('a', 65536)]]));
    }

    [Fact] public void Model_errors_throw()
    {
        var def = new TableDefinition(1, [F("n", FieldType.I32), F("c", FieldType.ColourRgb)]);
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [[5]]));                    // row too short
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [["5", "000000"]]));       // wrong CLR type
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [[5, null]]));
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(def, 1, null, [[5, "GG"]]));
        var unsupported = new TableDefinition(1, [new FieldDefinition("x", FieldType.Unsupported, "SequenceU32", false, null)]);
        Assert.Throws<ArgumentException>(() => DbTableEncoder.Encode(unsupported, 1, null, []));
    }

    [Fact] public void ReadGuid_reads_the_header_guid_or_null()
    {
        Assert.Equal(TestGuid, DbTableDecoder.ReadGuid(new TableBytes().Guid(TestGuid).Version(3).Rows(0).Build()));
        Assert.Equal("", DbTableDecoder.ReadGuid(new TableBytes().Guid("").Rows(0).Build()));
        Assert.Null(DbTableDecoder.ReadGuid(new TableBytes().Version(3).Rows(0).Build()));
        Assert.Null(DbTableDecoder.ReadGuid([0xFD, 0xFE, 0xFC, 0xFF, 0x24]));
        Assert.Null(DbTableDecoder.ReadGuid([]));
    }
}

public class LocEncoderTests
{
    [Fact] public void Encodes_rows_like_the_decoder_reads_them()
    {
        LocRow[] rows = [new("key_ü", "Text 日本 😀\twith tab", true), new("", "", false)];
        var decoded = LocDecoder.Decode(LocEncoder.Encode(rows));
        Assert.Equal(LocDecodeStatus.Ok, decoded.Status);
        Assert.Equal(rows, decoded.Rows);
    }

    [Fact] public void Writes_the_exact_bytes()
    {
        var expected = new TableBytes().Raw(0xFF, 0xFE, 0x4C, 0x4F, 0x43, 0x00).I32(1).Raw(1, 0, 0, 0).Str16("k").Str16("t").Bool(true).Build();
        Assert.Equal(expected, LocEncoder.Encode([new LocRow("k", "t", true)]));
    }

    [Fact] public void Too_long_text_throws() =>
        Assert.Throws<ArgumentException>(() => LocEncoder.Encode([new LocRow("k", new string('a', 65536), false)]));
}

public class EncoderRealDataTests
{
    [RealDataFact] public void Every_table_of_db_pack_encodes_back_to_its_bytes()
    {
        var schema = SchemaLoader.Load(Path.Combine(RepoRoot.Find(), "schemas", "schema_wh3.ron")).Schema!;
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "db.pack")).Index!;
        int same = 0;
        foreach (var e in index.Entries)
        {
            if (!DbTablePath.TryGetTableName(e.Path, out var table)) continue;
            var bytes = PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!;
            var t = DbTableDecoder.Decode(bytes, table, schema);
            if (t.Status != TableDecodeStatus.Ok) continue;
            Assert.True(bytes.AsSpan().SequenceEqual(DbTableEncoder.Encode(t.Definition!, t.Version, DbTableDecoder.ReadGuid(bytes), t.Rows)), e.Path);
            same++;
        }
        Assert.True(same > 1000);
    }

    [RealDataFact] public void Every_loc_of_local_en_pack_encodes_back_to_its_bytes()
    {
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "local_en.pack")).Index!;
        int same = 0;
        foreach (var e in index.Entries.Where(e => e.Path.EndsWith(".loc", StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!;
            var d = LocDecoder.Decode(bytes);
            Assert.True(d.Status == LocDecodeStatus.Ok, e.Path);
            Assert.True(bytes.AsSpan().SequenceEqual(LocEncoder.Encode(d.Rows)), e.Path);
            same++;
        }
        Assert.True(same > 100);
    }
}
```

- [ ] **Step 2: RED prüfen**

Run: `dotnet test tests/TotalWarModTool.Tables.Tests`
Expected: Build schlägt fehl (`DbTableEncoder`, `LocEncoder`, `ReadGuid` fehlen).

- [ ] **Step 3: `DbTableEncoder.Encode`, `LocEncoder.Encode`, `DbTableDecoder.ReadGuid` implementieren**

`ReadGuid` nutzt `ByteCursor` und die vorhandene Konstante `GuidMarker`. Die Encoder schreiben in einen `MemoryStream`.

- [ ] **Step 4: GREEN prüfen**

Run: `dotnet test TotalWarModTool.slnx` (mit beiden Umgebungsvariablen)
Expected: alle Tests bestanden, 0 übersprungen, 0 Warnungen.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: encode db tables and loc files"
```

---

### Task 2: `TableTsv`

**Files:**
- Create: `src/TotalWarModTool.Tables/TableTsv.cs`, `src/TotalWarModTool.Tables/TsvModel.cs`
- Modify: `tests/Shared/RealData.cs` (zwei Helfer für `rpfm_cli`)
- Test: `tests/TotalWarModTool.Tables.Tests/TableTsvWriteTests.cs`, `TableTsvReadTests.cs`, `TableTsvLocTests.cs`, `TableTsvRealDataTests.cs`

**Interfaces:**
- Consumes: Task 1 (`DbTableEncoder.Encode`, `LocEncoder.Encode`, `DbTableDecoder.ReadGuid`); `GameSchema.Find`, `GameSchema.Tables`.
- Produces:

```csharp
namespace TotalWarModTool.Tables;

public enum TsvProblem
{
    None, MissingMetaLine, InvalidMetaLine, TableNotInSchema, VersionNotInSchema, UnsupportedFieldType,
    MissingColumn, UnknownColumn, DuplicateColumn, WrongCellCount, InvalidValue,
}

/// Line is 1-based; Detail is short technical English, not a UI text.
public sealed record TsvDiagnosis(TsvProblem Problem, int Line, string Detail);

/// Text is null when the table cannot be written without loss; Detail then says why (row, field).
public sealed record TsvWriteResult(string? Text, string? Detail);

public sealed record TsvTable(string TableName, int Version, string PackPath, TableDefinition Definition, IReadOnlyList<object?[]> Rows);
public sealed record TsvTableResult(TsvTable? Table, TsvDiagnosis? Problem);

public sealed record TsvLoc(string PackPath, IReadOnlyList<LocRow> Rows);
public sealed record TsvLocResult(TsvLoc? Loc, TsvDiagnosis? Problem);

public static class TableTsv
{
    public static TsvWriteResult Write(string tableName, int version, string packPath, TableDefinition definition, IReadOnlyList<object?[]> rows);
    public static TsvTableResult Read(string text, GameSchema schema);
    public static TsvWriteResult WriteLoc(string packPath, IReadOnlyList<LocRow> rows);
    public static TsvLocResult ReadLoc(string text);
}
```

In `tests/Shared/RealData.cs`:

```csharp
/// Creates an empty pack with rpfm_cli.
public static void RpfmCreatePack(string packPath);
/// Adds a folder from disk to a pack with rpfm_cli; TSV files are imported to binary with the schema.
public static void RpfmAddFolder(string packPath, string folderOnDisk, string folderInPack, string schemaPathForTsv);
```

Aufrufe: `--game warhammer_3 pack create --pack-path <pack>` und `--game warhammer_3 pack add --pack-path <pack> --tsv-to-binary <schema> --folder-path "<folderOnDisk>;<folderInPack>"`.

Festlegungen (Spec 3.3 und 5.3):

**Form**
- Zeile 1: Spaltennamen. Zeile 2: `#<tableName>;<version>;<packPath>`, danach so viele Tabs, dass die Zeile gleich viele Zellen hat wie Zeile 1. Danach eine Zeile pro Eintrag. Jede Zeile endet mit `\n`, auch die letzte. Trenner ist der Tab.
- Loc: Zeile 1 `key`, `text`, `tooltip`; Zeile 2 `#Loc;1;<packPath>` plus zwei Tabs.
- `Read*` nimmt den Text der Datei: Ein BOM-Zeichen (`﻿`) am Anfang wird übergangen. Zeilen trennt `\n`; ein `\r` direkt davor gehört zum Zeilenende. Genau ein Zeilenende am Schluss darf fehlen oder da sein. Jede weitere leere Zeile ist eine Datenzeile (und damit meist `WrongCellCount`).

**Zellen**
- `Boolean`: `true`, `false`; gelesen ohne Rücksicht auf Groß-/Kleinschreibung.
- `I16`, `I32`, `I64`: invariante Kultur, optionales Vorzeichen, sonst nur Ziffern.
- `F32`, `F64`: geschrieben mit `ToString("R", CultureInfo.InvariantCulture)`, gelesen mit `NumberStyles.Float`.
- `ColourRgb`: geschrieben wie im Modell; gelesen 1 bis 8 Hex-Ziffern, ins Modell als `X6` in Großbuchstaben.
- Texte: geschrieben wird ein Tab als `\\t` und ein Zeilenumbruch (`\n`) als `\\n` (zwei Backslashes und der Buchstabe). Gelesen wird umgekehrt. Ein einzelner Backslash bleibt.
- Texte beim Lesen: `StringU8` und `OptionalStringU8` höchstens 65.535 UTF-8-Bytes, `StringU16` und Loc höchstens 65.535 UTF-16-Einheiten, sonst `InvalidValue`.

**Farbgruppen.** Drei Zahlenfelder (`I16`, `I32`, `I64`, `F32`, `F64`) bilden eine Gruppe und erscheinen als eine Spalte mit `RRGGBB`. Die Gruppen ergeben sich so (Regel aus dem Spike, gegen `rpfm_cli` geprüft):

```csharp
// For each field i in order and for each naming ("r","g","b") then ("red","green","blue"):
//   prefix = "" when the name equals r; name without r when it ends with "_" + r (the prefix keeps its trailing "_"); else no candidate.
//   g and b are the fields named prefix + g and prefix + b. All three must exist, be numeric, and not be in an earlier group.
//   Column name: prefix + "hex", or "unnamed colour group_N" for the empty prefix (N counts from 1 in order of discovery).
```

- Geschrieben: zuerst alle Felder ohne Gruppe in Schema-Reihenfolge, dann die Gruppen in der Reihenfolge ihrer Entdeckung. Ein Kanal ist nur schreibbar, wenn sein Wert ganzzahlig ist und in 0 bis 255 liegt.
- Gelesen: genau 6 Hex-Ziffern; jeder Kanal geht als Wert im Typ seines Feldes ins Modell.

**Nicht verlustfrei schreibbar** (`TsvWriteResult.Text` ist `null`, `Detail` nennt Zeile und Feld): ein Kanal außerhalb 0 bis 255 oder nicht ganzzahlig; ein Text, der `\r` enthält; ein Text, der wörtlich `\\t` oder `\\n` enthält; ein Feld vom Typ `Unsupported`. `packPath` und `tableName` mit Tab, Zeilenumbruch oder `;` im Tabellennamen: `ArgumentException`.

**Lesen, Reihenfolge der Prüfungen** (das erste Problem beendet das Lesen; `Table` ist dann `null`):
1. Weniger als zwei Zeilen oder Zeile 2 beginnt nicht mit `#` → `MissingMetaLine` (Zeile 2).
2. Erste Zelle der Zeile 2 ohne `#` ergibt nicht `<name>;<ganze Zahl>;<pfad>` (geteilt an den ersten zwei `;`) → `InvalidMetaLine` (Zeile 2).
3. Tabelle nicht im Schema → `TableNotInSchema`; Version nicht im Schema → `VersionNotInSchema`; Feld `Unsupported` → `UnsupportedFieldType` (alle Zeile 2).
4. Spaltenname doppelt → `DuplicateColumn`; Spalte, die weder Feld ohne Gruppe noch Gruppenspalte ist → `UnknownColumn`; erwartete Spalte fehlt → `MissingColumn` (alle Zeile 1, `Detail` nennt den Namen).
5. Datenzeile mit anderer Zellenzahl als Zeile 1 → `WrongCellCount` (ihre Zeile).
6. Wert passt nicht → `InvalidValue` (ihre Zeile, `Detail` nennt Spalte und Wert, den Wert auf 50 Zeichen gekürzt).

`ReadLoc`: Zeile 2 muss `#Loc;1;<pfad>` sein (`InvalidMetaLine` sonst), Spalten genau `key`, `text`, `tooltip` in beliebiger Reihenfolge, sonst wie oben.

- [ ] **Step 1: Failing Tests schreiben**

Gemeinsame Helfer in den drei synthetischen Testklassen:

```csharp
private static FieldDefinition F(string name, FieldType type) => new(name, type, type.ToString(), false, null);
private static GameSchema Schema(string table, int version, params FieldDefinition[] fields) =>
    new(new Dictionary<string, IReadOnlyList<TableDefinition>> { [table] = [new TableDefinition(version, fields)] });
```

```csharp
public class TableTsvWriteTests
{
    [Fact] public void Writes_header_meta_line_and_rows()
    {
        var def = new TableDefinition(3, [F("key", FieldType.StringU8), F("flag", FieldType.Boolean), F("n", FieldType.I32), F("tint", FieldType.ColourRgb), F("maybe", FieldType.OptionalStringU8)]);
        var r = TableTsv.Write("units_tables", 3, "db/units_tables/mod", def, [["unit_a", true, -5, "4080FF", "yes"], ["ünit", false, 0, "000000", ""]]);
        Assert.Null(r.Detail);
        Assert.Equal("key\tflag\tn\ttint\tmaybe\n#units_tables;3;db/units_tables/mod\t\t\t\t\nunit_a\ttrue\t-5\t4080FF\tyes\nünit\tfalse\t0\t000000\t\n", r.Text);
    }

    [Fact] public void Floats_use_the_shortest_exact_form()
    {
        var def = new TableDefinition(0, [F("a", FieldType.F32), F("b", FieldType.F64)]);
        var text = TableTsv.Write("t_tables", 0, "db/t_tables/x", def, [[0.1f, 0.1], [2.0000002E-06f, 1.0], [375f, -0.5]]).Text!;
        Assert.Equal("a\tb\n#t_tables;0;db/t_tables/x\t\n0.1\t0.1\n2.0000002E-06\t1\n375\t-0.5\n", text);
    }

    [Fact] public void Tab_and_line_break_are_escaped_and_a_backslash_stays()
    {
        var def = new TableDefinition(0, [F("s", FieldType.StringU8)]);
        var text = TableTsv.Write("t_tables", 0, "p", def, [["a\tb\nc"], [@"ui\new\table.png"]]).Text!;
        Assert.Equal("s\n#t_tables;0;p\na\\\\tb\\\\nc\nui\\new\\table.png\n", text);
    }

    [Fact] public void Colour_groups_become_one_hex_column_at_the_end()
    {
        var def = new TableDefinition(1, [F("name", FieldType.StringU8), F("col_r", FieldType.I32), F("col_g", FieldType.I32), F("col_b", FieldType.I32),
            F("r", FieldType.F64), F("g", FieldType.F64), F("b", FieldType.F64), F("bg_red", FieldType.I16), F("bg_green", FieldType.I16), F("bg_blue", FieldType.I16), F("other", FieldType.I32)]);
        var text = TableTsv.Write("t_tables", 1, "p", def, [["x", 255, 128, 0, 1.0, 2.0, 3.0, (short)16, (short)32, (short)48, 7]]).Text!;
        Assert.Equal("name\tother\tcol_hex\tunnamed colour group_1\tbg_hex\n#t_tables;1;p\t\t\t\t\nx\t7\tFF8000\t010203\t102030\n", text);
    }

    [Fact] public void An_incomplete_or_non_numeric_triple_stays_separate()
    {
        var def = new TableDefinition(1, [F("col_r", FieldType.I32), F("col_g", FieldType.I32), F("x_r", FieldType.StringU8), F("x_g", FieldType.I32), F("x_b", FieldType.I32)]);
        Assert.StartsWith("col_r\tcol_g\tx_r\tx_g\tx_b\n", TableTsv.Write("t_tables", 1, "p", def, []).Text!);
    }

    [Theory]
    [InlineData(256)] [InlineData(-1)]
    public void A_channel_outside_0_to_255_cannot_be_written(int channel)
    {
        var def = new TableDefinition(1, [F("r", FieldType.I32), F("g", FieldType.I32), F("b", FieldType.I32)]);
        var r = TableTsv.Write("t_tables", 1, "p", def, [[1, 2, 3], [channel, 0, 0]]);
        Assert.Null(r.Text);
        Assert.Contains("row 1", r.Detail);
    }

    [Fact] public void A_fractional_channel_cannot_be_written()
    {
        var def = new TableDefinition(1, [F("r", FieldType.F64), F("g", FieldType.F64), F("b", FieldType.F64)]);
        Assert.Null(TableTsv.Write("t_tables", 1, "p", def, [[0.5, 0.0, 0.0]]).Text);
    }

    [Theory]
    [InlineData("a\rb")] [InlineData(@"a\\tb")] [InlineData(@"a\\nb")]
    public void A_text_the_dialect_cannot_carry_cannot_be_written(string value)
    {
        var def = new TableDefinition(1, [F("s", FieldType.StringU8)]);
        var r = TableTsv.Write("t_tables", 1, "p", def, [[value]]);
        Assert.Null(r.Text);
        Assert.False(string.IsNullOrWhiteSpace(r.Detail));
    }
}

public class TableTsvReadTests
{
    private static readonly FieldDefinition[] Fields =
    [
        F("key", FieldType.StringU8), F("flag", FieldType.Boolean), F("small", FieldType.I16), F("n", FieldType.I32), F("big", FieldType.I64),
        F("ratio", FieldType.F32), F("exact", FieldType.F64), F("tint", FieldType.ColourRgb), F("wide", FieldType.StringU16), F("maybe", FieldType.OptionalStringU8),
        F("col_r", FieldType.I32), F("col_g", FieldType.F64), F("col_b", FieldType.I16),
    ];
    private static readonly GameSchema TestSchema = Schema("t_tables", 2, Fields);
    private static readonly TableDefinition Def = TestSchema.Find("t_tables", 2)!;

    [Fact] public void Reads_what_Write_wrote()
    {
        object?[][] rows =
        [
            ["a\tb\nc", true, (short)-2, 70000, 5_000_000_000L, 0.1f, 0.1, "4080FF", "wide 😀", "yes", 255, 128.0, (short)0],
            [@"ui\new\table.png", false, (short)7, -1, -9L, 2.0000002E-06f, -0.5, "1FFFFFF", "", "", 0, 0.0, (short)255],
        ];
        var text = TableTsv.Write("t_tables", 2, "db/t_tables/mod", Def, rows).Text!;
        var read = TableTsv.Read(text, TestSchema);
        Assert.Null(read.Problem);
        Assert.Equal(("t_tables", 2, "db/t_tables/mod"), (read.Table!.TableName, read.Table.Version, read.Table.PackPath));
        Assert.Same(Def, read.Table.Definition);
        Assert.Equal(rows, read.Table.Rows);
    }

    [Fact] public void Extreme_values_round_trip()
    {
        var def = new TableDefinition(0, [F("a", FieldType.I16), F("b", FieldType.I32), F("c", FieldType.I64), F("d", FieldType.F32), F("e", FieldType.F64)]);
        var schema = Schema("x_tables", 0, def.Fields.ToArray());
        object?[][] rows =
        [
            [short.MinValue, int.MinValue, long.MinValue, float.NaN, double.PositiveInfinity],
            [short.MaxValue, int.MaxValue, long.MaxValue, float.Epsilon, double.MaxValue],
            [(short)0, 0, 0L, -0f, double.NegativeInfinity],
        ];
        var read = TableTsv.Read(TableTsv.Write("x_tables", 0, "p", def, rows).Text!, schema);
        var back = DbTableEncoder.Encode(def, 0, null, read.Table!.Rows);
        Assert.Equal(DbTableEncoder.Encode(def, 0, null, rows), back);   // compares bits, also for NaN and -0
    }

    [Fact] public void Read_accepts_crlf_bom_missing_final_newline_and_any_column_order()
    {
        var schema = Schema("t_tables", 1, F("a", FieldType.I32), F("s", FieldType.StringU8), F("r", FieldType.I32), F("g", FieldType.I32), F("b", FieldType.I32), F("on", FieldType.Boolean));
        var text = "﻿unnamed colour group_1\ton\ts\ta\r\n#t_tables;1;p\t\t\t\r\n0A0B0C\tTRUE\tx\t5\r\nffffff\tFalse\t\t-1";
        var read = TableTsv.Read(text, schema);
        Assert.Null(read.Problem);
        Assert.Equal(new object?[][] { [5, "x", 10, 11, 12, true], [-1, "", 255, 255, 255, false] }, read.Table!.Rows);
    }

    [Fact] public void An_empty_table_has_no_rows()
    {
        var read = TableTsv.Read(TableTsv.Write("t_tables", 2, "p", Def, []).Text!, TestSchema);
        Assert.Null(read.Problem);
        Assert.Empty(read.Table!.Rows);
    }

    private const string Head = "a\ts\n";
    private static readonly GameSchema Small = Schema("t_tables", 1, F("a", FieldType.I16), F("s", FieldType.StringU8));

    [Theory]
    [InlineData("", TsvProblem.MissingMetaLine, 2)]
    [InlineData("a\ts\n", TsvProblem.MissingMetaLine, 2)]
    [InlineData("a\ts\n1\tx\n", TsvProblem.MissingMetaLine, 2)]
    [InlineData("a\ts\n#t_tables;1\t\n", TsvProblem.InvalidMetaLine, 2)]
    [InlineData("a\ts\n#t_tables;one;p\t\n", TsvProblem.InvalidMetaLine, 2)]
    [InlineData("a\ts\n#other_tables;1;p\t\n", TsvProblem.TableNotInSchema, 2)]
    [InlineData("a\ts\n#t_tables;9;p\t\n", TsvProblem.VersionNotInSchema, 2)]
    [InlineData("a\ts\ta\n#t_tables;1;p\t\t\n", TsvProblem.DuplicateColumn, 1)]
    [InlineData("a\ts\tzzz\n#t_tables;1;p\t\t\n", TsvProblem.UnknownColumn, 1)]
    [InlineData("a\n#t_tables;1;p\n", TsvProblem.MissingColumn, 1)]
    [InlineData("a\ts\n#t_tables;1;p\t\n1\tx\n2\n", TsvProblem.WrongCellCount, 4)]
    [InlineData("a\ts\n#t_tables;1;p\t\n1\tx\ty\n", TsvProblem.WrongCellCount, 3)]
    [InlineData("a\ts\n#t_tables;1;p\t\n1\tx\n\n", TsvProblem.WrongCellCount, 4)]
    [InlineData("a\ts\n#t_tables;1;p\t\n1\tx\nabc\tx\n", TsvProblem.InvalidValue, 4)]
    [InlineData("a\ts\n#t_tables;1;p\t\n70000\tx\n", TsvProblem.InvalidValue, 3)]
    [InlineData("a\ts\n#t_tables;1;p\t\n1.5\tx\n", TsvProblem.InvalidValue, 3)]
    [InlineData("a\ts\n#t_tables;1;p\t\n\tx\n", TsvProblem.InvalidValue, 3)]
    public void Read_reports_the_problem_with_its_line(string text, TsvProblem problem, int line)
    {
        var read = TableTsv.Read(text, Small);
        Assert.Null(read.Table);
        Assert.Equal((problem, line), (read.Problem!.Problem, read.Problem.Line));
        Assert.False(string.IsNullOrWhiteSpace(read.Problem.Detail));
    }

    [Theory]
    [InlineData(FieldType.Boolean, "yes")] [InlineData(FieldType.I32, "1e3")] [InlineData(FieldType.I64, "9223372036854775808")]
    [InlineData(FieldType.F32, "1,5")] [InlineData(FieldType.F64, "abc")] [InlineData(FieldType.ColourRgb, "GG0000")]
    [InlineData(FieldType.ColourRgb, "")] [InlineData(FieldType.ColourRgb, "123456789")]
    public void Read_rejects_a_value_that_does_not_fit_the_type(FieldType type, string value)
    {
        var read = TableTsv.Read($"v\n#t_tables;1;p\n{value}\n", Schema("t_tables", 1, F("v", type)));
        Assert.Equal((TsvProblem.InvalidValue, 3), (read.Problem!.Problem, read.Problem.Line));
    }

    [Fact] public void Read_rejects_a_bad_channel_value_a_too_long_text_and_an_unsupported_field()
    {
        var colours = Schema("t_tables", 1, F("r", FieldType.I32), F("g", FieldType.I32), F("b", FieldType.I32));
        Assert.Equal(TsvProblem.InvalidValue, TableTsv.Read("unnamed colour group_1\n#t_tables;1;p\nFF80\n", colours).Problem!.Problem);
        var text = Schema("t_tables", 1, F("s", FieldType.StringU8));
        Assert.Null(TableTsv.Read($"s\n#t_tables;1;p\n{new string('a', 65535)}\n", text).Problem);
        Assert.Equal(TsvProblem.InvalidValue, TableTsv.Read($"s\n#t_tables;1;p\n{new string('ü', 32768)}\n", text).Problem!.Problem);
        var unsupported = Schema("t_tables", 1, new FieldDefinition("x", FieldType.Unsupported, "SequenceU32", false, null));
        var problem = TableTsv.Read("x\n#t_tables;1;p\n", unsupported).Problem!;
        Assert.Equal((TsvProblem.UnsupportedFieldType, 2), (problem.Problem, problem.Line));
    }
}

public class TableTsvLocTests
{
    [Fact] public void Writes_and_reads_loc_rows()
    {
        LocRow[] rows = [new("key_a", "Text\twith tab\nand break", true), new("key_ü", "", false)];
        var text = TableTsv.WriteLoc("text/db/mod.loc", rows).Text!;
        Assert.Equal("key\ttext\ttooltip\n#Loc;1;text/db/mod.loc\t\t\nkey_a\tText\\\\twith tab\\\\nand break\ttrue\nkey_ü\t\tfalse\n", text);
        var read = TableTsv.ReadLoc(text);
        Assert.Null(read.Problem);
        Assert.Equal("text/db/mod.loc", read.Loc!.PackPath);
        Assert.Equal(rows, read.Loc.Rows);
    }

    [Fact] public void A_text_with_carriage_return_cannot_be_written() =>
        Assert.Null(TableTsv.WriteLoc("p", [new LocRow("k", "a\rb", false)]).Text);

    [Theory]
    [InlineData("key\ttext\ttooltip\n#Loc;2;p\t\t\n", TsvProblem.InvalidMetaLine, 2)]
    [InlineData("key\ttext\ttooltip\n#units_tables;1;p\t\t\n", TsvProblem.InvalidMetaLine, 2)]
    [InlineData("key\ttext\n#Loc;1;p\t\n", TsvProblem.MissingColumn, 1)]
    [InlineData("key\ttext\ttooltip\textra\n#Loc;1;p\t\t\t\n", TsvProblem.UnknownColumn, 1)]
    [InlineData("key\ttext\ttooltip\n#Loc;1;p\t\t\nk\tt\n", TsvProblem.WrongCellCount, 3)]
    [InlineData("key\ttext\ttooltip\n#Loc;1;p\t\t\nk\tt\tmaybe\n", TsvProblem.InvalidValue, 3)]
    public void ReadLoc_reports_the_problem_with_its_line(string text, TsvProblem problem, int line)
    {
        var read = TableTsv.ReadLoc(text);
        Assert.Null(read.Loc);
        Assert.Equal((problem, line), (read.Problem!.Problem, read.Problem.Line));
    }
}

public class TableTsvRealDataTests
{
    private static string SchemaPath => Path.Combine(RepoRoot.Find(), "schemas", "schema_wh3.ron");

    [RealDataFact] public void Every_table_of_db_pack_round_trips_through_tsv_or_is_refused()
    {
        var schema = SchemaLoader.Load(SchemaPath).Schema!;
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "db.pack")).Index!;
        int same = 0, refused = 0;
        foreach (var e in index.Entries)
        {
            if (!DbTablePath.TryGetTableName(e.Path, out var table)) continue;
            var bytes = PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!;
            var t = DbTableDecoder.Decode(bytes, table, schema);
            if (t.Status != TableDecodeStatus.Ok) continue;
            var written = TableTsv.Write(table, t.Version, e.Path, t.Definition!, t.Rows);
            if (written.Text is null) { refused++; Assert.False(string.IsNullOrWhiteSpace(written.Detail), e.Path); continue; }
            var read = TableTsv.Read(written.Text, schema);
            Assert.True(read.Problem is null, $"{e.Path}: {read.Problem}");
            Assert.True(bytes.AsSpan().SequenceEqual(DbTableEncoder.Encode(read.Table!.Definition, read.Table.Version, DbTableDecoder.ReadGuid(bytes), read.Table.Rows)), e.Path);
            same++;
        }
        Assert.True(same > 1000);
        Assert.True(refused <= same / 100, $"{refused} tables refused");
    }

    [RealDataFact] public void Every_loc_of_local_en_pack_round_trips_through_tsv_or_is_refused()
    {
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "local_en.pack")).Index!;
        int same = 0, refused = 0;
        foreach (var e in index.Entries.Where(e => e.Path.EndsWith(".loc", StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!;
            var written = TableTsv.WriteLoc(e.Path, LocDecoder.Decode(bytes).Rows);
            if (written.Text is null) { refused++; continue; }
            var read = TableTsv.ReadLoc(written.Text);
            Assert.True(read.Problem is null, $"{e.Path}: {read.Problem}");
            Assert.True(bytes.AsSpan().SequenceEqual(LocEncoder.Encode(read.Loc!.Rows)), e.Path);
            same++;
        }
        Assert.True(same > 100);
        Assert.True(refused <= same / 100, $"{refused} loc files refused");
    }

    [RpfmFact] public void Rpfm_cli_imports_our_tsv_with_equal_content()
    {
        using var dir = new TempDir();
        var schema = SchemaLoader.Load(SchemaPath).Schema!;
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "db.pack")).Index!;
        var expected = new Dictionary<string, DecodedTable>();
        foreach (var e in index.Entries)
        {
            if (!DbTablePath.TryGetTableName(e.Path, out var table)) continue;
            var t = DbTableDecoder.Decode(PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!, table, schema);
            if (t.Status != TableDecodeStatus.Ok || TableTsv.Write(table, t.Version, e.Path, t.Definition!, t.Rows).Text is not { } text) continue;
            var file = Path.Combine(dir.Path, "tsv", e.Path.Replace('/', Path.DirectorySeparatorChar) + ".tsv");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text, new UTF8Encoding(false));
            expected[e.Path] = t;
        }

        var pack = Path.Combine(dir.Path, "built.pack");
        RealData.RpfmCreatePack(pack);
        RealData.RpfmAddFolder(pack, Path.Combine(dir.Path, "tsv", "db"), "db", SchemaPath);
        var built = PackReader.ReadIndex(pack).Index!;
        foreach (var (path, t) in expected)
        {
            var entry = built.Find(path);
            Assert.True(entry is not null, $"{path} is missing in the pack rpfm_cli built");
            var theirs = DbTableDecoder.Decode(PackReader.ReadEntry(built, entry!, CancellationToken.None).Bytes!, t.TableName, schema);
            Assert.True(theirs.Status == TableDecodeStatus.Ok && theirs.Version == t.Version, path);
            Assert.True(DbTableEncoder.Encode(t.Definition!, t.Version, null, t.Rows).AsSpan().SequenceEqual(
                DbTableEncoder.Encode(theirs.Definition!, theirs.Version, null, theirs.Rows)), path);
        }
        Assert.True(expected.Count > 1000);
    }
}
```

- [ ] **Step 2: RED prüfen**

Run: `dotnet test tests/TotalWarModTool.Tables.Tests`
Expected: Build schlägt fehl (`TableTsv` fehlt).

- [ ] **Step 3: `TsvModel.cs` (Typen), `TableTsv` und die zwei Helfer in `RealData` implementieren**

Die Spaltenzuordnung (Felder ohne Gruppe, Gruppen) berechnet eine private Funktion einmal pro Definition; `Write` und `Read` nutzen dieselbe.

- [ ] **Step 4: GREEN prüfen**

Run: `dotnet test TotalWarModTool.slnx` (mit beiden Umgebungsvariablen)
Expected: alle Tests bestanden, 0 übersprungen, 0 Warnungen. Schlägt `Rpfm_cli_imports_our_tsv_with_equal_content` für einzelne Tabellen fehl, liegt die Ursache in der Gruppenregel oder in einer Zellenform; die Tabelle und die abweichende Zelle im Bericht nennen, nicht den Test lockern.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: read and write tables and loc files as tsv in the rpfm dialect"
```

---

### Task 3: `PackWriter`

**Files:**
- Create: `src/TotalWarModTool.PackFiles/PackWriter.cs`, `src/TotalWarModTool.PackFiles/PackWriteModel.cs`
- Modify: `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md` (Abschnitt 5.4, Unterabschnitt „Festlegungen aus Plan 1“)
- Test: `tests/TotalWarModTool.PackFiles.Tests/PackWriterTests.cs`, `PackWriterRealDataTests.cs`

**Interfaces:**
- Consumes: `PackReader.ReadIndex`, `PackReader.ReadEntry` (nur in Tests); Testhelfer `PackBuilder`, `TempDir`, `RealData`.
- Produces:

```csharp
namespace TotalWarModTool.PackFiles;

/// Path uses '/' or '\'. Open is called once, when the entry is written; the stream must give exactly Length bytes.
public sealed record PackWriteEntry(string Path, long Length, Func<Stream> Open);

public enum PackWriteProblem
{
    None, InvalidPath, PathTooLong, DuplicatePath, InvalidDependency, EntryTooLarge, IndexTooLarge,
    SourceUnreadable, SourceChanged, TargetUnwritable,
}

/// Detail is short technical English, not a UI text; it names the entry path where one is involved.
public sealed record PackWriteResult(PackWriteProblem Problem, string? Detail);

public static class PackWriter
{
    /// Pure: checks paths, dependencies and the limits of the format. Opens nothing.
    public static PackWriteResult Validate(IReadOnlyList<string> dependencies, IReadOnlyList<PackWriteEntry> entries);

    /// Validates, then writes a PFH5 mod pack to targetPath (created or replaced). progress gets the number of entries written.
    /// On a problem or a cancel targetPath does not exist afterwards. Throws only OperationCanceledException and argument errors.
    public static PackWriteResult Write(string targetPath, IReadOnlyList<string> dependencies, IReadOnlyList<PackWriteEntry> entries,
        DateTimeOffset buildTime, IProgress<int>? progress, CancellationToken cancellationToken);
}
```

Festlegungen (Spec 3.4 und 5.7):
- Kopf, 28 Bytes: `PFH5`, u32 `3`, u32 Zahl der Abhängigkeiten, u32 Größe des Abhängigkeits-Index, u32 Zahl der Einträge, u32 Größe des Eintrags-Index, u32 `buildTime` als Unix-Sekunden.
- Abhängigkeits-Index: jeder Name als UTF-8 plus ein Null-Byte, in gegebener Reihenfolge.
- Eintrags-Index pro Eintrag: u32 Länge, ein Byte `00` (unkomprimiert), Pfad mit `\` als UTF-8, ein Null-Byte. Danach die Daten in Index-Reihenfolge.
- Reihenfolge: ordinal (`StringComparer.Ordinal`) nach dem Pfad in der `\`-Form.
- `Validate`, erstes Problem gewinnt, in dieser Reihenfolge je Liste:
  - Abhängigkeit leer oder mit Null-Zeichen → `InvalidDependency`.
  - Pfad leer, mit Null-Zeichen, mit führendem oder schließendem Trenner oder mit leerem Segment → `InvalidPath`.
  - Pfad über 65.535 UTF-8-Bytes → `PathTooLong`.
  - Zwei Pfade gleich ohne Rücksicht auf Groß-/Kleinschreibung und Trenner → `DuplicatePath` (`Detail` nennt beide).
  - `Length` über `uint.MaxValue` → `EntryTooLarge`. Negatives `Length` → `ArgumentException`.
  - Abhängigkeits- oder Eintrags-Index über `uint.MaxValue` Bytes → `IndexTooLarge`.
- `Write`:
  - `cancellationToken` vor dem Anlegen der Datei, vor jedem Eintrag und vor jedem Kopierblock prüfen.
  - Quelle lässt sich nicht öffnen oder lesen (`IOException`, `UnauthorizedAccessException`) → `SourceUnreadable`.
  - Quelle liefert weniger oder mehr als `Length` Bytes → `SourceChanged`.
  - Ziel lässt sich nicht anlegen oder schreiben (`IOException`, `UnauthorizedAccessException`, `DirectoryNotFoundException`) → `TargetUnwritable`.
  - In allen drei Fällen und bei Abbruch wird die Zieldatei gelöscht (`finally`). Scheitert `Validate`, wird sie nie angelegt und eine vorhandene Datei bleibt unberührt.
  - Jeder geöffnete Quell-Stream wird wieder geschlossen.
  - Nie mehr als ein Kopierpuffer im Speicher; kein Eintrag wird ganz geladen.

Spec-Nachtrag (im selben Commit): In 5.4 einen Unterabschnitt „Festlegungen aus Plan 1“ ergänzen mit vier Punkten: (1) Die Encoder nehmen das Modell des Dekoders; die GUID liest `DbTableDecoder.ReadGuid`, der Import prüft damit byte-genau. (2) `TableTsv.Write` verweigert auch Texte mit `\r`. (3) `TableTsv.Read*` meldet nur das erste Problem. (4) `PackWriter.Write` schreibt genau den übergebenen Pfad; temp-Datei und Umbenennen macht `Projects`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class PackWriterTests
{
    private static readonly DateTimeOffset BuildTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private static PackWriteEntry E(string path, params byte[] bytes) => new(path, bytes.Length, () => new MemoryStream(bytes));

    [Fact] public void Written_pack_is_read_back()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        var big = new byte[5_000_000];
        new Random(1).NextBytes(big);
        var entries = new[] { E("text/db/mod.loc", 1, 2, 3), E(@"db\units_tables\ünit_日本", 9), E("db/Units_tables/a"), E("script/big.bin", big) };
        var result = PackWriter.Write(target, ["first.pack", "second.pack"], entries, BuildTime, null, CancellationToken.None);
        Assert.Equal(PackWriteProblem.None, result.Problem);

        var index = PackReader.ReadIndex(target).Index!;
        Assert.Equal((PackType.Mod, false, false), (index.Header.Type, index.Header.HasIndexTimestamps, index.Header.HasEncryptedIndex));
        Assert.Equal(["first.pack", "second.pack"], index.Dependencies);
        Assert.Equal(["db/Units_tables/a", "db/units_tables/ünit_日本", "script/big.bin", "text/db/mod.loc"], index.Entries.Select(e => e.Path));
        Assert.All(index.Entries, e => Assert.False(e.IsCompressed));
        Assert.Equal(big, PackReader.ReadEntry(index, index.Find("script/big.bin")!, CancellationToken.None).Bytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, PackReader.ReadEntry(index, index.Find("text/db/mod.loc")!, CancellationToken.None).Bytes);
        Assert.Empty(PackReader.ReadEntry(index, index.Find("db/Units_tables/a")!, CancellationToken.None).Bytes!);
    }

    [Fact] public void Writes_the_exact_bytes_of_the_format()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        PackWriter.Write(target, ["dep.pack"], [E("b/y", 7, 8), E("a/x", 5)], BuildTime, null, CancellationToken.None);
        var expected = new PackBuilder().AddDependency("dep.pack").AddEntry(@"a\x", [5]).AddEntry(@"b\y", [7, 8]).Build();
        BitConverter.GetBytes((uint)1_700_000_000).CopyTo(expected, 24);
        Assert.Equal(expected, File.ReadAllBytes(target));
    }

    [Fact] public void An_empty_pack_is_valid()
    {
        using var dir = new TempDir();
        var target = dir.File("empty.pack");
        Assert.Equal(PackWriteProblem.None, PackWriter.Write(target, [], [], BuildTime, null, CancellationToken.None).Problem);
        Assert.Empty(PackReader.ReadIndex(target).Index!.Entries);
    }

    [Theory]
    [InlineData("")] [InlineData("/a")] [InlineData("a/")] [InlineData("a//b")] [InlineData(@"a\\b")] [InlineData("a\0b")]
    public void Validate_rejects_an_invalid_path(string path) =>
        Assert.Equal(PackWriteProblem.InvalidPath, PackWriter.Validate([], [E(path)]).Problem);

    [Fact] public void Validate_reports_limits_duplicates_and_bad_dependencies()
    {
        Assert.Equal(PackWriteProblem.None, PackWriter.Validate(["a.pack"], [E(new string('a', 65535))]).Problem);
        Assert.Equal(PackWriteProblem.PathTooLong, PackWriter.Validate([], [E(new string('a', 65536))]).Problem);
        Assert.Equal(PackWriteProblem.PathTooLong, PackWriter.Validate([], [E(new string('ü', 32768))]).Problem);
        var duplicate = PackWriter.Validate([], [E("db/T/x"), E("other"), E(@"DB\t\X")]);
        Assert.Equal(PackWriteProblem.DuplicatePath, duplicate.Problem);
        Assert.Contains("db/T/x", duplicate.Detail!.Replace('\\', '/'));
        Assert.Equal(PackWriteProblem.InvalidDependency, PackWriter.Validate([""], []).Problem);
        Assert.Equal(PackWriteProblem.InvalidDependency, PackWriter.Validate(["a\0b"], []).Problem);
        Assert.Equal(PackWriteProblem.EntryTooLarge, PackWriter.Validate([], [new PackWriteEntry("a", (long)uint.MaxValue + 1, () => Stream.Null)]).Problem);
        Assert.Equal(PackWriteProblem.None, PackWriter.Validate([], [new PackWriteEntry("a", uint.MaxValue, () => Stream.Null)]).Problem);
        Assert.Throws<ArgumentException>(() => PackWriter.Validate([], [new PackWriteEntry("a", -1, () => Stream.Null)]));
    }

    [Fact] public void A_validation_problem_leaves_an_existing_target_untouched()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        File.WriteAllBytes(target, [1, 2, 3]);
        Assert.Equal(PackWriteProblem.InvalidPath, PackWriter.Write(target, [], [E("")], BuildTime, null, CancellationToken.None).Problem);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
    }

    [Theory]
    [InlineData(2)] [InlineData(4)]
    public void A_source_with_the_wrong_length_fails_and_leaves_no_file(int declared)
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        var entries = new[] { E("a", 1), new PackWriteEntry("b", declared, () => new MemoryStream([1, 2, 3])) };
        var result = PackWriter.Write(target, [], entries, BuildTime, null, CancellationToken.None);
        Assert.Equal(PackWriteProblem.SourceChanged, result.Problem);
        Assert.Contains("b", result.Detail);
        Assert.False(File.Exists(target));
    }

    [Fact] public void An_unreadable_source_fails_closes_streams_and_leaves_no_file()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        var first = new MemoryStream([1]);
        var entries = new[] { new PackWriteEntry("a", 1, () => first), new PackWriteEntry("b", 1, () => throw new IOException("locked")) };
        var result = PackWriter.Write(target, [], entries, BuildTime, null, CancellationToken.None);
        Assert.Equal(PackWriteProblem.SourceUnreadable, result.Problem);
        Assert.False(File.Exists(target));
        Assert.False(first.CanRead);   // disposed
    }

    [Fact] public void A_missing_target_folder_is_reported()
    {
        using var dir = new TempDir();
        var result = PackWriter.Write(Path.Combine(dir.Path, "missing", "mod.pack"), [], [E("a", 1)], BuildTime, null, CancellationToken.None);
        Assert.Equal(PackWriteProblem.TargetUnwritable, result.Problem);
    }

    [Fact] public void Cancel_leaves_no_file()
    {
        using var dir = new TempDir();
        var target = dir.File("mod.pack");
        using var cts = new CancellationTokenSource();
        var entries = new[] { E("a", 1), new PackWriteEntry("b", 1, () => { cts.Cancel(); return new MemoryStream([2]); }), E("c", 3) };
        Assert.ThrowsAny<OperationCanceledException>(() => PackWriter.Write(target, [], entries, BuildTime, null, cts.Token));
        Assert.False(File.Exists(target));
    }

    [Fact] public void Progress_counts_the_entries_written()
    {
        using var dir = new TempDir();
        var seen = new List<int>();
        PackWriter.Write(dir.File("mod.pack"), [], [E("a", 1), E("b", 2), E("c", 3)], BuildTime, new SyncProgress(seen.Add), CancellationToken.None);
        Assert.Equal([1, 2, 3], seen);
    }

    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}

public class PackWriterRealDataTests
{
    [RealDataFact] public void A_rebuilt_vanilla_pack_has_the_same_content()
    {
        using var dir = new TempDir();
        var source = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "local_en.pack")).Index!;
        var entries = source.Entries.Select(e => { var b = PackReader.ReadEntry(source, e, CancellationToken.None).Bytes!; return new PackWriteEntry(e.Path, b.Length, () => new MemoryStream(b)); }).ToList();
        var target = dir.File("rebuilt.pack");
        Assert.Equal(PackWriteProblem.None, PackWriter.Write(target, source.Dependencies, entries, DateTimeOffset.UnixEpoch, null, CancellationToken.None).Problem);
        var rebuilt = PackReader.ReadIndex(target).Index!;
        Assert.Equal(source.Entries.Count, rebuilt.Entries.Count);
        foreach (var e in source.Entries)
            Assert.True(PackReader.ReadEntry(source, e, CancellationToken.None).Bytes!.AsSpan().SequenceEqual(
                PackReader.ReadEntry(rebuilt, rebuilt.Find(e.Path)!, CancellationToken.None).Bytes), e.Path);
    }

    [RpfmFact] public void Rpfm_cli_extracts_a_pack_we_wrote()
    {
        using var dir = new TempDir();
        var source = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "local_en.pack")).Index!;
        var picked = source.Entries.Take(50).ToList();
        var bytes = picked.ToDictionary(e => e.Path, e => PackReader.ReadEntry(source, e, CancellationToken.None).Bytes!);
        var target = dir.File("ours.pack");
        PackWriter.Write(target, [], picked.Select(e => new PackWriteEntry(e.Path, bytes[e.Path].Length, () => new MemoryStream(bytes[e.Path]))).ToList(),
            DateTimeOffset.UnixEpoch, null, CancellationToken.None);
        var outDir = Path.Combine(dir.Path, "out");
        RealData.RpfmExtractFolder(target, "/", outDir);
        foreach (var (path, expected) in bytes)
            Assert.Equal(expected, File.ReadAllBytes(Path.Combine(outDir, path.Replace('/', Path.DirectorySeparatorChar))));
    }
}
```

- [ ] **Step 2: RED prüfen**

Run: `dotnet test tests/TotalWarModTool.PackFiles.Tests`
Expected: Build schlägt fehl (`PackWriter` fehlt).

- [ ] **Step 3: `PackWriteModel.cs` (Typen) und `PackWriter` implementieren, Spec-Nachtrag schreiben**

- [ ] **Step 4: GREEN prüfen**

Run: `dotnet test TotalWarModTool.slnx` (mit beiden Umgebungsvariablen)
Expected: alle Tests bestanden, 0 übersprungen, 0 Warnungen.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: write pfh5 mod packs as a stream"
```

---

## Abschluss

Kein nutzersichtbares Verhalten ändert sich: kein Eintrag im Changelog, keine manuelle Checkliste. Nach Task 3 folgt das Review des ganzen Branches, danach Plan 2 (`Projects`), der auf `TableTsv`, den Encodern und `PackWriter` aufbaut.
