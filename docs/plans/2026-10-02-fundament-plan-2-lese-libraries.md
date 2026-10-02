# Fundament Plan 2: Lese-Libraries – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fünf UI-freie Libraries, die ein Total-War-Spiel finden, seine Pack-Dateien katalogisieren, einzelne Einträge lesen und dekomprimieren und DB-Tabellen sowie Loc-Dateien mit dem RPFM-Schema dekodieren.

**Architecture:** `GameProfiles`, `PackFiles`, `Schema`, `Tables` und `Workspace` sind reine Lese-Libraries auf `net8.0`. Sie schreiben nichts, loggen nichts und kennen keine UI-Texte: Jedes Problem kommt als Ergebnis zurück (Enum plus technisches Detail). Die App aus Plan 1 bleibt in diesem Plan unverändert; Plan 3 verdrahtet die Libraries.

**Tech Stack:** C# 12, .NET 8, xUnit 2.9.2, ein neues NuGet-Paket `ZstdSharp.Port` 0.8.8 (MIT, vom Nutzer am 2026-10-02 freigegeben).

**Spec:** `docs/specs/2026-10-02-fundament.md`. Formatfakten stehen in 3.2 (Pack), 3.3 (Schema), 3.4 (Tabellen), 3.8 (zstd, LZ4, Loc, Manifest). Schnittstellen in 4.2 bis 4.5. Projektregeln: `CLAUDE.md`.

**Branch:** `feature/fundament-lese-libraries` (von `master`, Stand `43508a9` oder neuer). Baseline vor Task 1: `dotnet test TotalWarModTool.slnx` ergibt 184 bestandene Tests, Build ohne Warnung.

**Echte Daten für Tests:** zwei Umgebungsvariablen, gesetzt auf dem Rechner des Nutzers.
- `TWMT_WH3_DIR`: Installationsordner von WH3 (der Ordner, der `data` enthält).
- `TWMT_RPFM_CLI`: voller Pfad zu `rpfm_cli.exe`.
Ohne die Variablen werden die Tests auf echten Daten übersprungen. Wer eine Task umsetzt, lässt sie **mit** gesetzten Variablen laufen und nennt im Bericht, wie viele dieser Tests liefen.

## Global Constraints

- Code, Bezeichner und Code-Kommentare Englisch. Commit-Messages Englisch.
- Alle neuen Libraries zielen auf `net8.0`. Build-Einstellungen nur in `Directory.Build.props`, Paketversionen nur in `Directory.Packages.props`.
- Einziges neues NuGet-Paket: `ZstdSharp.Port` 0.8.8, nur in `TotalWarModTool.PackFiles`. Kein weiteres Paket.
- `TreatWarningsAsErrors` ist an. Ein Build mit Warnungen gilt als fehlgeschlagen.
- Referenzen: `GameProfiles`, `PackFiles`, `Schema` referenzieren kein eigenes Projekt. `Tables` → `Schema`. `Workspace` → `GameProfiles`, `PackFiles`, `Operations`. Keine dieser Libraries referenziert `Logging`, `Localization`, `Storage`, `Settings` oder die App.
- **Nur lesen.** Produktcode dieses Plans öffnet Dateien ausschließlich lesend, mit `FileShare.ReadWrite | FileShare.Delete`. Er legt nichts an und ändert nichts.
- **Nie ein ganzes Pack laden.** Gelesen werden 28 Header-Bytes, der Index und einzelne Einträge.
- **Diagnosen statt Exceptions.** Kaputte, abgeschnittene oder feindliche Dateien führen nie zu einer Exception nach außen, nie zu halben Ergebnissen und nie zu einer Speicheranforderung, die eine Zahl aus der Datei ungeprüft übernimmt. Nach außen dringen nur `OperationCanceledException` (Abbruch) und Argumentfehler.
- **Diagnose = Enum + `Detail`.** `Detail` ist ein kurzer technischer Text auf Englisch (wie eine Exception-Meldung), kein UI-Text. In diesem Plan entstehen keine UI-Texte und keine Einträge in `locales/`.
- **Abbruch.** Jede Schleife über Packs, Einträge oder Blöcke prüft das `CancellationToken` mindestens einmal pro Durchlauf.
- Keine statischen Singletons, kein veränderlicher statischer Zustand.
- Kein `catch (Exception)`. Gefangen werden benannte Typen (`IOException`, `UnauthorizedAccessException`, `FormatException`, `ArgumentException` aus Dekodern usw.) und in eine Diagnose übersetzt.
- Kein Spielwissen im Code: Ordnernamen, App-ID, Schema-Datei, Manifest-Name und Vanilla-Typen kommen aus dem `GameProfile`. Formatkonstanten von `PFH5`, Tabellen und Loc sind kein Spielwissen.
- Test-first: failing Test → RED prüfen → implementieren → GREEN → Commit. Die ganze Suite (`dotnet test TotalWarModTool.slnx`) bleibt grün. Keine Zeit-Assertions.
- Synthetische Tests bauen ihre Bytes mit den Testhelfern (`PackBuilder`, `TableBytes`) und schreiben nur in `TempDir`.
- Tests auf echten Daten prüfen Invarianten statt fester Zahlen und tragen `[RealDataFact]` oder `[RpfmFact]`.
- Ein Commit pro Task, nach GREEN. Vor jedem Commit `git add -A`. Die Attribution-Zeile der laufenden Sitzung gehört ans Ende der Commit-Message.
- Die Tests in diesem Plan legen Namen und Erwartungen fest. Scheitert ein Test nur an einem Compiler-Detail, wird er minimal angepasst, ohne die Erwartung zu ändern.
- `tests/TotalWarModTool.App.Tests` und der Produktcode aus Plan 1 bleiben unangetastet.

## Review Focus

Diese Fälle nennt die Spec nicht einzeln, sie treffen Nutzer aber zuerst. Jeder hat einen Test in der genannten Task.

1. **Mod für eine ältere Spielversion** (gleiche Versionsnummer, anderes Layout; gemessen bei 9 echten Tabellen). Erwartung: Status `Failed` mit Detail, keine Exception, keine halb gelesenen Zeilen. → Task 5 (`Wrong_layout_fails_without_partial_rows`, echte Daten: `Every_table_of_db_pack_is_ok_or_has_a_diagnosis`).
2. **Feindliche oder kaputte Zahlen in Dateiköpfen** (4 Milliarden Einträge, Zeilen oder Zeichen; Größen-Präfix lügt). Erwartung: Diagnose, kein Speicherfresser, kein Absturz. → Task 2 (`Huge_entry_count_is_inconsistent_not_a_crash`), Task 3 (`Size_prefix_above_the_limit_is_too_large`, `Wrong_size_prefix_is_corrupt`), Task 5 (`Huge_row_count_fails_without_allocating`).
3. **Pack ändert sich, während das Tool offen ist** (Steam-Update, Mod in RPFM neu gespeichert). Erwartung: `PackChanged` statt Datenmüll. → Task 3 (`Changed_pack_is_reported`).
4. **Steam liegt anders als auf dem Entwicklerrechner** (mehrere Bibliotheken, Bibliothek auf abgestecktem Laufwerk, Pfade mit doppelten Backslashes in der VDF-Datei). Erwartung: Spiel wird gefunden oder sauber nicht gefunden. → Task 1 (`Find_skips_missing_libraries`, `ReadLibraryFolders_unescapes_paths`).
5. **Ungewöhnliche Pfade in Packs** (Umlaute, japanische Zeichen, gemischte Schreibweise, doppelte Pfade). Erwartung: Index liest sie, Suche findet sie ohne Rücksicht auf Groß- und Kleinschreibung, bei Duplikaten gewinnt der erste. → Task 2 (`Paths_are_normalized_and_found_case_insensitively`).

---

### Task 1: `GameProfiles`

**Files:**
- Create: `src/TotalWarModTool.GameProfiles/TotalWarModTool.GameProfiles.csproj`, `GameProfile.cs`, `GameProfileLoader.cs`, `GameLocator.cs`, `ISteamLocationSource.cs`
- Create: `profiles/warhammer_3.json`
- Test: `tests/TotalWarModTool.GameProfiles.Tests/…csproj`, `GameProfileLoaderTests.cs`, `GameLocatorTests.cs`

**Interfaces:**
- Consumes: Testhelfer `TempDir`, `RepoRoot` aus `tests/Shared` (Plan 1).
- Produces:

```csharp
namespace TotalWarModTool.GameProfiles;

public sealed record GameProfile(
    string Id, string DisplayName, int SteamAppId, string InstallFolderName, string DataFolder,
    string PackFormat, string SchemaFile, IReadOnlyList<string> VanillaPackTypes, string? VanillaManifestFile);

public sealed record ProfileProblem(string FileName, string Detail);
public sealed record ProfileLoadResult(IReadOnlyList<GameProfile> Profiles, IReadOnlyList<ProfileProblem> Problems);

public static class GameProfileLoader
{
    public const int SchemaVersion = 1;
    public static ProfileLoadResult LoadAll(string profilesFolder);   // *.json, top level only, profiles sorted by Id
}

public interface ISteamLocationSource { string? GetSteamPath(); }     // real implementation (registry) comes with plan 3

public sealed record GameLocation(string InstallPath, string DataPath, string? WorkshopPath);

public static class GameLocator
{
    public static GameLocation? Find(GameProfile profile, ISteamLocationSource steam);
    public static GameLocation? FromInstallPath(GameProfile profile, string installPath);   // null when the data folder is missing
    public static IReadOnlyList<string> ReadLibraryFolders(string steamPath);
}
```

Festlegungen:
- Profildatei: JSON-Objekt mit `schemaVersion`, `id`, `displayName`, `steamAppId`, `installFolderName`, `dataFolder`, `packFormat`, `schemaFile`, `vanillaPackTypes`, optional `vanillaManifestFile`.
- Übersprungen mit `ProfileProblem` (nie eine Exception): kein gültiges JSON, kein Objekt, `schemaVersion` fehlt oder ist nicht 1, ein Pflichtfeld fehlt oder ist leer, `steamAppId` ≤ 0, `vanillaPackTypes` leer, Datei nicht lesbar, `id` schon von einer früheren Datei (Dateinamen ordinal sortiert) vergeben. Fehlt der Ordner: leeres Ergebnis ohne Problem.
- `ReadLibraryFolders`: liest `<steamPath>\steamapps\libraryfolders.vdf`, nimmt jeden Wert hinter `"path"`, ersetzt `\\` durch `\`. Das Ergebnis enthält immer zuerst `steamPath` selbst, ohne Duplikate (Vergleich ohne Groß- und Kleinschreibung). Fehlt die Datei oder ist sie nicht lesbar: nur `steamPath`.
- `Find`: `steam.GetSteamPath()` ist `null` oder der Ordner fehlt → `null`. Sonst die erste Bibliothek, in der `<lib>\steamapps\common\<InstallFolderName>\<DataFolder>` existiert. Bibliotheken, deren Ordner fehlt, werden übergangen.
- `FromInstallPath`: `DataPath` = `<installPath>\<DataFolder>`; fehlt er → `null`. `WorkshopPath` = `<lib>\steamapps\workshop\content\<SteamAppId>`, wobei `<lib>` drei Ebenen über `installPath` liegt und dessen Elternkette `steamapps\common` heißt; existiert der Ordner nicht oder passt die Kette nicht → `null`.
- `profiles/warhammer_3.json`:

```json
{
  "schemaVersion": 1,
  "id": "warhammer_3",
  "displayName": "Total War: WARHAMMER III",
  "steamAppId": 1142710,
  "installFolderName": "Total War WARHAMMER III",
  "dataFolder": "data",
  "packFormat": "PFH5",
  "schemaFile": "schema_wh3.ron",
  "vanillaPackTypes": ["boot", "release", "patch"],
  "vanillaManifestFile": "manifest.txt"
}
```

- [ ] **Step 1: Failing Tests schreiben**

Gemeinsamer Helfer in beiden Testdateien:

```csharp
private static readonly GameProfile TestGame = new("testgame", "Test Game", 42, "Test Game", "packs", "PFH5", "schema_test.ron", ["release"], null);
private const string ValidJson = """
    {"schemaVersion":1,"id":"testgame","displayName":"Test Game","steamAppId":42,"installFolderName":"Test Game",
     "dataFolder":"packs","packFormat":"PFH5","schemaFile":"schema_test.ron","vanillaPackTypes":["release"]}
    """;
```

```csharp
public class GameProfileLoaderTests
{
    [Fact] public void Loads_a_valid_profile_without_manifest()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("testgame.json"), ValidJson);
        var result = GameProfileLoader.LoadAll(dir.Path);
        Assert.Empty(result.Problems);
        var p = Assert.Single(result.Profiles);
        Assert.Equal(("testgame", "Test Game", 42, "Test Game", "packs", "PFH5", "schema_test.ron"),
            (p.Id, p.DisplayName, p.SteamAppId, p.InstallFolderName, p.DataFolder, p.PackFormat, p.SchemaFile));
        Assert.Equal(["release"], p.VanillaPackTypes);
        Assert.Null(p.VanillaManifestFile);
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("[1]")]
    [InlineData("""{"id":"x"}""")]                                                     // schemaVersion missing
    [InlineData("""{"schemaVersion":2,"id":"x","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]
    [InlineData("""{"schemaVersion":1,"id":"","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":0,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":1,"installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":[]}""")]
    [InlineData("""{"schemaVersion":1,"id":"x","displayName":"X","steamAppId":"one","installFolderName":"X","dataFolder":"d","packFormat":"PFH5","schemaFile":"s","vanillaPackTypes":["release"]}""")]
    public void Skips_a_broken_profile_and_keeps_the_good_one(string broken)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a_broken.json"), broken);
        File.WriteAllText(dir.File("b_good.json"), ValidJson);
        var result = GameProfileLoader.LoadAll(dir.Path);
        Assert.Equal("testgame", Assert.Single(result.Profiles).Id);
        var problem = Assert.Single(result.Problems);
        Assert.Equal("a_broken.json", problem.FileName);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact] public void Duplicate_id_keeps_the_first_file()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.json"), ValidJson);
        File.WriteAllText(dir.File("b.json"), ValidJson.Replace("Test Game\",\"steamAppId", "Other\",\"steamAppId"));
        var result = GameProfileLoader.LoadAll(dir.Path);
        Assert.Equal("Test Game", Assert.Single(result.Profiles).DisplayName);
        Assert.Equal("b.json", Assert.Single(result.Problems).FileName);
    }

    [Fact] public void Missing_folder_gives_an_empty_result()
    {
        var result = GameProfileLoader.LoadAll(@"X:\does\not\exist");
        Assert.Empty(result.Profiles); Assert.Empty(result.Problems);
    }

    [Fact] public void Shipped_profiles_load_without_problems()
    {
        var result = GameProfileLoader.LoadAll(Path.Combine(RepoRoot.Find(), "profiles"));
        Assert.Empty(result.Problems);
        var wh3 = Assert.Single(result.Profiles, p => p.Id == "warhammer_3");
        Assert.Equal((1142710, "Total War WARHAMMER III", "data", "PFH5", "schema_wh3.ron", "manifest.txt"),
            (wh3.SteamAppId, wh3.InstallFolderName, wh3.DataFolder, wh3.PackFormat, wh3.SchemaFile, wh3.VanillaManifestFile));
    }
}

public class GameLocatorTests
{
    private sealed class FakeSteam(string? path) : ISteamLocationSource { public string? GetSteamPath() => path; }

    // Creates <root>\steamapps\common\Test Game\packs and returns the install path.
    private static string Install(string libraryRoot, bool withWorkshop)
    {
        var install = Path.Combine(libraryRoot, "steamapps", "common", "Test Game");
        Directory.CreateDirectory(Path.Combine(install, "packs"));
        if (withWorkshop) Directory.CreateDirectory(Path.Combine(libraryRoot, "steamapps", "workshop", "content", "42"));
        return install;
    }

    [Fact] public void ReadLibraryFolders_unescapes_paths()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "steamapps"));
        File.WriteAllText(Path.Combine(dir.Path, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + dir.Path.Replace(@"\", @"\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\Steam Library\"\n\t}\n}\n");
        Assert.Equal([dir.Path, @"D:\Steam Library"], GameLocator.ReadLibraryFolders(dir.Path));
    }

    [Fact] public void ReadLibraryFolders_without_file_returns_only_the_steam_path()
    {
        using var dir = new TempDir();
        Assert.Equal([dir.Path], GameLocator.ReadLibraryFolders(dir.Path));
    }

    [Fact] public void Find_locates_the_game_in_a_second_library_with_workshop()
    {
        using var steam = new TempDir(); using var library = new TempDir();
        Directory.CreateDirectory(Path.Combine(steam.Path, "steamapps"));
        File.WriteAllText(Path.Combine(steam.Path, "steamapps", "libraryfolders.vdf"),
            "\"path\" \"" + library.Path.Replace(@"\", @"\\") + "\"");
        var install = Install(library.Path, withWorkshop: true);
        var location = GameLocator.Find(TestGame, new FakeSteam(steam.Path));
        Assert.Equal(new GameLocation(install, Path.Combine(install, "packs"),
            Path.Combine(library.Path, "steamapps", "workshop", "content", "42")), location);
    }

    [Fact] public void Find_skips_missing_libraries()
    {
        using var steam = new TempDir();
        Directory.CreateDirectory(Path.Combine(steam.Path, "steamapps"));
        File.WriteAllText(Path.Combine(steam.Path, "steamapps", "libraryfolders.vdf"), "\"path\" \"Q:\\\\Unplugged\"");
        var install = Install(steam.Path, withWorkshop: false);
        var location = GameLocator.Find(TestGame, new FakeSteam(steam.Path));
        Assert.Equal((install, (string?)null), (location!.InstallPath, location.WorkshopPath));
    }

    [Theory]
    [InlineData(false)]   // no steam at all
    [InlineData(true)]    // steam present, game not installed
    public void Find_returns_null_when_the_game_is_not_there(bool steamExists)
    {
        using var steam = new TempDir();
        Assert.Null(GameLocator.Find(TestGame, new FakeSteam(steamExists ? steam.Path : null)));
    }

    [Fact] public void FromInstallPath_rejects_a_folder_without_data()
    {
        using var dir = new TempDir();
        Assert.Null(GameLocator.FromInstallPath(TestGame, dir.Path));
    }

    [Fact] public void FromInstallPath_outside_a_steam_library_has_no_workshop()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "packs"));
        Assert.Equal(new GameLocation(dir.Path, Path.Combine(dir.Path, "packs"), null), GameLocator.FromInstallPath(TestGame, dir.Path));
    }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.GameProfiles.Tests` – Expected: Build-Fehler, Typen fehlen.
- [ ] **Step 3: Implementieren.** Projekte der Solution hinzufügen (`dotnet sln TotalWarModTool.slnx add`).
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` – Expected: alles bestanden, 0 Warnungen.
- [ ] **Step 5: Commit** – `git commit -m "feat: add game profiles and Steam-based game locator"`

---

### Task 2: `PackFiles` – Header und Index

**Files:**
- Create: `src/TotalWarModTool.PackFiles/TotalWarModTool.PackFiles.csproj`, `PackModel.cs` (Enums und Records), `FileFingerprint.cs`, `PackIndex.cs`, `PackReader.cs`
- Create: `tests/Shared/PackBuilder.cs`, `tests/Shared/RealData.cs`
- Modify: `README.md` (Abschnitt zu den beiden Umgebungsvariablen)
- Test: `tests/TotalWarModTool.PackFiles.Tests/…csproj`, `PackReaderIndexTests.cs`, `PackReaderRealDataTests.cs`

**Interfaces:**
- Consumes: `TempDir` (Plan 1).
- Produces:

```csharp
namespace TotalWarModTool.PackFiles;

public enum PackType { Unknown = -1, Boot = 0, Release = 1, Patch = 2, Mod = 3, Movie = 4 }
public enum PackProblem { None, NotFound, Unreadable, Truncated, UnknownFormat, EncryptedIndex, InconsistentIndex }

public sealed record FileFingerprint(string Path, long Length, DateTime LastWriteTimeUtc)
{
    public static FileFingerprint? TryRead(string path);   // null when the file is missing or not accessible
}

// Counts and sizes are the unsigned 32-bit values of the file, widened to long.
public sealed record PackHeader(string Magic, PackType Type, bool HasIndexTimestamps, bool HasEncryptedIndex,
    bool HasEncryptedData, long DependencyCount, long DependencyIndexSize, long EntryCount, long EntryIndexSize);
public sealed record PackHeaderResult(PackHeader? Header, PackProblem Problem, string? Detail);

public sealed record PackEntry(string Path, long Offset, long StoredSize, bool IsCompressed);

public sealed class PackIndex
{
    public FileFingerprint Fingerprint { get; }
    public PackHeader Header { get; }
    public IReadOnlyList<string> Dependencies { get; }
    public IReadOnlyList<PackEntry> Entries { get; }
    public PackEntry? Find(string path);   // accepts '/' and '\', ignores case, first match wins
}
public sealed record PackIndexResult(PackIndex? Index, PackProblem Problem, string? Detail);

public static class PackReader
{
    public const int HeaderSize = 28;
    public static PackHeaderResult ReadHeader(string path);
    public static PackIndexResult ReadIndex(string path);
}
```

```csharp
// tests/Shared/PackBuilder.cs — builds PFH5 bytes for tests (BCL only)
public sealed class PackBuilder
{
    public PackBuilder(string magic = "PFH5", uint packType = 3);
    public PackBuilder WithFlags(uint flags);                                  // OR-ed into the bitmask
    public PackBuilder AddDependency(string packName);
    public PackBuilder AddEntry(string path, byte[] storedBytes, bool compressed = false);   // path is written as given
    public byte[] Build();
    public string WriteTo(string filePath);                                    // writes Build(), returns filePath
    public static byte[] Stored(byte[] frame, int uncompressedSize);           // 4-byte little-endian size + frame
    public static byte[] Hex(string hex);                                      // "04 22 4D 18" -> bytes, spaces and '|' ignored
}

// tests/Shared/RealData.cs — access to real game data for invariant and parity tests (BCL only)
public static class RealData
{
    public static string? Wh3Dir { get; }        // TWMT_WH3_DIR when set and <dir>\data exists, else null
    public static string? RpfmCli { get; }       // TWMT_RPFM_CLI when set and the file exists, else null
    public static string Wh3Data { get; }        // <Wh3Dir>\data
    public static string? Wh3Workshop { get; }   // <library>\steamapps\workshop\content\1142710 when it exists
    // Runs rpfm_cli (no window, stdin closed, 180 s timeout kills the process tree, throws on a non-zero exit code).
    public static void RpfmExtractFolder(string packPath, string folderInPack, string destDir, string? schemaPathForTsv = null);
    public static void RpfmExtractFiles(string packPath, IEnumerable<string> filesInPack, string destDir);
}
public sealed class RealDataFactAttribute : FactAttribute { }   // sets Skip when RealData.Wh3Dir is null
public sealed class RpfmFactAttribute : FactAttribute { }       // sets Skip when Wh3Dir or RpfmCli is null
```

`rpfm_cli`-Aufruf: `--game warhammer_3 pack extract --pack-path <pack> [--tables-as-tsv <schema>] --folder-path "<folder>;<dest>"` bzw. je Datei `--file-path "<file>;<dest>"`. Der Ordner `/` meint das ganze Pack.

Format (Spec 3.2, an 665 echten Packs gemessen):

```text
Header, 28 bytes, little endian:
   0  4  magic "PFH5" (ASCII)
   4  4  bitmask: low 4 bits = pack type; 0x10 encrypted data; 0x40 index has timestamps;
                  0x80 encrypted index; 0x100 extended header
   8  4  dependency count
  12  4  dependency index size in bytes
  16  4  entry count
  20  4  entry index size in bytes
  24  4  timestamp (ignored)
Dependency index: <count> names, each UTF-8, zero-terminated.
Entry index, per entry: u32 stored size; [u32 timestamp, only with 0x40]; u8 compressed (0/1);
                        path UTF-8, zero-terminated, backslashes as separators.
Data: starts at 28 + dependency index size + entry index size; entries follow each other in index order, no padding.
```

Festlegungen:
- `ReadHeader` liest genau 28 Bytes. `ReadIndex` liest Header, Abhängigkeiten und Index, sonst nichts.
- Pack-Typ außerhalb 0–4 → `PackType.Unknown` (kein Problem).
- Probleme: Datei fehlt → `NotFound`. `IOException` / `UnauthorizedAccessException` → `Unreadable`. Kürzer als 28 Bytes, oder Header plus beide Indexgrößen länger als die Datei → `Truncated`. Kennung nicht `PFH5` oder Bit `0x100` gesetzt → `UnknownFormat` (`Detail` nennt die Kennung). Bit `0x80` → `EncryptedIndex` (bei `ReadIndex`; `ReadHeader` liefert den Header ohne Problem). Index endet vor oder nach dem letzten Eintrag, ein Pfad ohne Nullbyte, oder Datenanfang plus Summe der Größen ungleich Dateilänge → `InconsistentIndex`.
- Die Eintragsliste wächst mit den tatsächlich gelesenen Einträgen. `EntryCount` aus dem Header dient nie als Vorab-Kapazität.
- Pfade: `\` wird zu `/`. `Find` normalisiert sein Argument genauso und vergleicht ordinal ohne Groß- und Kleinschreibung.
- `Offset` ist die absolute Position in der Datei.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class PackReaderIndexTests
{
    private static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Fact] public void ReadHeader_returns_type_flags_and_counts()
    {
        using var dir = new TempDir();
        var path = new PackBuilder(packType: 1).WithFlags(0x40).AddDependency("base.pack")
            .AddEntry(@"db\units_tables\data__", B("abc")).WriteTo(dir.File("a.pack"));
        var r = PackReader.ReadHeader(path);
        Assert.Equal(PackProblem.None, r.Problem);
        Assert.Equal(("PFH5", PackType.Release, true, false, false, 1L, 1L), (r.Header!.Magic, r.Header.Type,
            r.Header.HasIndexTimestamps, r.Header.HasEncryptedIndex, r.Header.HasEncryptedData, r.Header.DependencyCount, r.Header.EntryCount));
    }

    [Theory]
    [InlineData(0u)]      // without timestamps
    [InlineData(0x40u)]   // with timestamps
    public void ReadIndex_lists_entries_with_absolute_offsets(uint flags)
    {
        using var dir = new TempDir();
        var path = new PackBuilder().WithFlags(flags).AddDependency("first.pack").AddDependency("second.pack")
            .AddEntry(@"db\units_tables\data__", B("abc"))
            .AddEntry(@"script\main.lua", B("hello"), compressed: true).WriteTo(dir.File("a.pack"));
        var r = PackReader.ReadIndex(path);
        Assert.Equal(PackProblem.None, r.Problem);
        var index = r.Index!;
        Assert.Equal(["first.pack", "second.pack"], index.Dependencies);
        Assert.Equal(2, index.Entries.Count);
        var first = index.Entries[0]; var second = index.Entries[1];
        Assert.Equal(("db/units_tables/data__", 3L, false), (first.Path, first.StoredSize, first.IsCompressed));
        Assert.Equal(("script/main.lua", 5L, true), (second.Path, second.StoredSize, second.IsCompressed));
        Assert.Equal(first.Offset + 3, second.Offset);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(B("abc"), bytes[(int)first.Offset..(int)(first.Offset + 3)]);
        Assert.Equal(new FileInfo(path).Length, index.Fingerprint.Length);
    }

    [Fact] public void Paths_are_normalized_and_found_case_insensitively()
    {
        using var dir = new TempDir();
        var path = new PackBuilder()
            .AddEntry(@"db\Ünits_tables\!モッド", B("1"))
            .AddEntry(@"DB\ünits_TABLES\!モッド", B("22")).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        Assert.Equal("db/Ünits_tables/!モッド", index.Entries[0].Path);
        Assert.Same(index.Entries[0], index.Find(@"DB\ünits_tables/!モッド"));   // first of two duplicates
        Assert.Null(index.Find("db/other"));
    }

    [Fact] public void Empty_pack_has_no_entries()
    {
        using var dir = new TempDir();
        var r = PackReader.ReadIndex(new PackBuilder().WriteTo(dir.File("a.pack")));
        Assert.Equal((PackProblem.None, 0), (r.Problem, r.Index!.Entries.Count));
    }

    [Fact] public void Missing_file_is_NotFound()
    {
        Assert.Equal(PackProblem.NotFound, PackReader.ReadHeader(@"X:\none.pack").Problem);
        Assert.Equal(PackProblem.NotFound, PackReader.ReadIndex(@"X:\none.pack").Problem);
    }

    [Fact] public void Locked_file_is_Unreadable()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().WriteTo(dir.File("a.pack"));
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(PackProblem.Unreadable, PackReader.ReadIndex(path).Problem);
    }

    [Fact] public void File_shorter_than_the_header_is_Truncated()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(dir.File("a.pack"), new byte[10]);
        Assert.Equal(PackProblem.Truncated, PackReader.ReadHeader(dir.File("a.pack")).Problem);
    }

    [Theory]
    [InlineData("PFH4", 0u)]
    [InlineData("ABCD", 0u)]
    [InlineData("PFH5", 0x100u)]   // extended header
    public void Other_formats_are_UnknownFormat(string magic, uint flags)
    {
        using var dir = new TempDir();
        var path = new PackBuilder(magic).WithFlags(flags).WriteTo(dir.File("a.pack"));
        var r = PackReader.ReadIndex(path);
        Assert.Equal(PackProblem.UnknownFormat, r.Problem);
        Assert.Contains(magic, r.Detail);
    }

    [Fact] public void Encrypted_index_is_reported_by_ReadIndex_only()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().WithFlags(0x80).AddEntry("a", B("x")).WriteTo(dir.File("a.pack"));
        Assert.Equal((PackProblem.None, true), (PackReader.ReadHeader(path).Problem, PackReader.ReadHeader(path).Header!.HasEncryptedIndex));
        Assert.Equal(PackProblem.EncryptedIndex, PackReader.ReadIndex(path).Problem);
    }

    [Fact] public void Index_size_beyond_the_file_is_Truncated()
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddEntry("a", B("x")).Build();
        BitConverter.GetBytes(0x7FFFFFFFu).CopyTo(bytes, 20);          // entry index size
        File.WriteAllBytes(dir.File("a.pack"), bytes);
        Assert.Equal(PackProblem.Truncated, PackReader.ReadIndex(dir.File("a.pack")).Problem);
    }

    [Fact] public void Huge_entry_count_is_inconsistent_not_a_crash()
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddEntry("a", B("x")).Build();
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(bytes, 16);          // entry count
        File.WriteAllBytes(dir.File("a.pack"), bytes);
        Assert.Equal(PackProblem.InconsistentIndex, PackReader.ReadIndex(dir.File("a.pack")).Problem);
    }

    [Theory]
    [InlineData(1)]    // one byte too many
    [InlineData(-1)]   // one byte missing
    public void Data_that_does_not_end_at_the_file_end_is_InconsistentIndex(int delta)
    {
        using var dir = new TempDir();
        var bytes = new PackBuilder().AddEntry("a", B("xyz")).Build();
        File.WriteAllBytes(dir.File("a.pack"), delta > 0 ? [.. bytes, 0] : bytes[..^1]);
        Assert.Equal(PackProblem.InconsistentIndex, PackReader.ReadIndex(dir.File("a.pack")).Problem);
    }

    [Fact] public void Unknown_pack_type_is_not_a_problem()
    {
        using var dir = new TempDir();
        var r = PackReader.ReadHeader(new PackBuilder(packType: 9).WriteTo(dir.File("a.pack")));
        Assert.Equal((PackProblem.None, PackType.Unknown), (r.Problem, r.Header!.Type));
    }

    [Fact] public void Fingerprint_changes_when_the_file_changes()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().AddEntry("a", B("x")).WriteTo(dir.File("a.pack"));
        var before = FileFingerprint.TryRead(path)!;
        new PackBuilder().AddEntry("a", B("xx")).WriteTo(path);
        Assert.NotEqual(before, FileFingerprint.TryRead(path));
        Assert.Null(FileFingerprint.TryRead(dir.File("missing.pack")));
    }
}

public class PackReaderRealDataTests
{
    [RealDataFact] public void Every_pack_in_the_data_folder_has_a_consistent_index()
    {
        var packs = Directory.GetFiles(RealData.Wh3Data, "*.pack");
        Assert.NotEmpty(packs);
        Assert.All(packs, p => { var r = PackReader.ReadIndex(p); Assert.True(r.Problem == PackProblem.None, $"{Path.GetFileName(p)}: {r.Problem} {r.Detail}"); });
    }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.PackFiles.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren** (`PackBuilder` und `RealData` zuerst, dann `PackReader`). `README.md`: kurzer englischer Abschnitt „Tests on real game data“ mit den beiden Variablen und dem Hinweis, dass die Tests ohne sie übersprungen werden.
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` mit gesetztem `TWMT_WH3_DIR`. Expected: alles bestanden, der Test auf echten Daten lief (nicht übersprungen).
- [ ] **Step 5: Commit** – `git commit -m "feat: read pack headers and indexes"`

---

### Task 3: `PackFiles` – Einträge lesen und dekomprimieren

**Files:**
- Create: `src/TotalWarModTool.PackFiles/EntryContent.cs`, `Lz4Frame.cs`, `EntryReader.cs` (interne Leselogik hinter `PackReader.ReadEntry`)
- Create: `THIRD-PARTY-NOTICES.md`
- Modify: `src/TotalWarModTool.PackFiles/PackReader.cs`, `TotalWarModTool.PackFiles.csproj`, `Directory.Packages.props`
- Test: `tests/TotalWarModTool.PackFiles.Tests/Lz4FrameTests.cs`, `PackReaderEntryTests.cs`, `PackReaderEntryRealDataTests.cs`

**Interfaces:**
- Consumes: `PackIndex`, `PackEntry`, `FileFingerprint`, `PackReader`, `PackBuilder`, `RealData` (Task 2).
- Produces:

```csharp
namespace TotalWarModTool.PackFiles;

public enum CompressionFormat { None, Zstd, Lz4, Lzma, Unknown }
public enum EntryProblem { None, PackChanged, Unreadable, Encrypted, UnsupportedCompression, Corrupt, TooLarge }
public sealed record EntryContent(byte[]? Bytes, CompressionFormat Format, long UncompressedSize, EntryProblem Problem, string? Detail);

public static class Lz4Frame
{
    // Decodes one LZ4 frame into a buffer of exactly expectedSize bytes. False with an error text for any malformed input.
    public static bool TryDecode(ReadOnlySpan<byte> frame, int expectedSize, out byte[] data, out string? error);
}

public static partial class PackReader   // added member
{
    public const long DefaultMaxEntryBytes = 268_435_456;
    // Throws OperationCanceledException when the token is cancelled. Every other failure is an EntryProblem.
    public static EntryContent ReadEntry(PackIndex index, PackEntry entry, CancellationToken cancellationToken, long maxBytes = DefaultMaxEntryBytes);
}
```

Format eines komprimierten Eintrags (Spec 3.2, 3.8):

```text
Stored bytes of a compressed entry:
   0  4  uncompressed size (u32)
   4  …  frame; the first 4 bytes decide the format:
         28 B5 2F FD  -> zstd frame
         04 22 4D 18  -> LZ4 frame
         5D …         -> LZMA (not supported)
         anything else -> unknown

LZ4 frame:
   magic 04 22 4D 18
   FLG   bits 7-6 version (must be 01); bit 5 block independence; bit 4 block checksum;
         bit 3 content size present; bit 2 content checksum; bit 0 dictionary id present
   BD    block maximum size (not needed for decoding)
   [8 bytes content size, if bit 3] [4 bytes dictionary id, if bit 0]
   HC    1 byte header checksum (skipped, not verified)
   blocks: u32 size; high bit set = block is stored uncompressed; 0 = end mark;
           block data; [4 bytes block checksum, if bit 4]
   [4 bytes content checksum, if bit 2] (skipped, not verified)

LZ4 block = sequences: token (high nibble literal length, low nibble match length - 4);
   a length nibble of 15 is extended by following bytes, each added, until a byte below 255;
   literals; 2-byte little-endian offset (1..bytes produced so far, counted over the whole output,
   so matches may reach into earlier blocks); match copy may overlap its own output.
   The last sequence of a block ends after its literals.
```

Festlegungen:
- Reihenfolge in `ReadEntry`: Token prüfen → Fingerprint der Datei mit `index.Fingerprint` vergleichen (ungleich oder Datei weg → `PackChanged`) → `index.Header.HasEncryptedData` → `Encrypted` → Größenprüfung → lesen → dekomprimieren → Token erneut prüfen.
- Unkomprimiert: `StoredSize > maxBytes` → `TooLarge`. Sonst die Bytes, Format `None`.
- Komprimiert mit `StoredSize` 0: leeres Ergebnis ohne Problem. `StoredSize` 1 bis 8: `Corrupt`.
- Komprimiert: Zuerst nur 8 Bytes lesen (Größe und Kennung). Größe über `maxBytes` → `TooLarge`, ohne den Rest zu lesen. LZMA oder unbekannt → `UnsupportedCompression` mit dem Format. Erst dann den ganzen Eintrag lesen.
- zstd: Zielpuffer in der genannten Größe, `new ZstdSharp.Decompressor().Unwrap(source, destination)`. Wirft das Paket (`ZstdSharp.ZstdException`), oder weicht die gelieferte Länge ab → `Corrupt`.
- LZ4: `Lz4Frame.TryDecode`. Jede Abweichung (falsche Kennung, Version nicht 1, Lesen über das Ende, Offset 0 oder größer als die bisherige Ausgabe, Ausgabe über `expectedSize` hinaus, am Ende nicht genau `expectedSize`, Bytes nach dem Frame) → `false`, in `ReadEntry` → `Corrupt`. Kein Zugriff außerhalb der Puffer, auch nicht bei feindlichen Längen.
- `UncompressedSize` ist bei jedem Ergebnis gesetzt, sobald sie bekannt ist (auch bei `TooLarge` und `UnsupportedCompression`).
- `IOException` / `UnauthorizedAccessException` beim Lesen → `Unreadable`.
- `Directory.Packages.props`: `<PackageVersion Include="ZstdSharp.Port" Version="0.8.8" />`.
- `THIRD-PARTY-NOTICES.md` (Englisch): Abschnitt „ZstdSharp.Port 0.8.8“ mit `https://github.com/oleg-st/ZstdSharp`, Zeile „Copyright Oleg Stepanischev 2026“ und dem vollständigen MIT-Lizenztext.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class Lz4FrameTests
{
    private static byte[] H(string hex) => PackBuilder.Hex(hex);
    private static string Text(byte[] b) => System.Text.Encoding.ASCII.GetString(b);

    // one block, literals "abc" and an overlapping match of 9 bytes at offset 3
    private const string Simple = "04 22 4D 18 60 40 00 | 06 00 00 00 | 35 61 62 63 03 00 | 00 00 00 00";
    // two dependent blocks, the second block's match reaches into the first; content checksum present
    private const string Dependent = "04 22 4D 18 44 40 00 | 04 00 00 00 | 30 61 62 63 | 03 00 00 00 | 02 03 00 | 00 00 00 00 | DE AD BE EF";
    // extended literal length (15 + 5 = 20), then an uncompressed block "xyz"
    private const string LongLiteralsAndRawBlock = "04 22 4D 18 60 70 00 | 16 00 00 00 | F0 05 30 31 32 33 34 35 36 37 38 39 41 42 43 44 45 46 47 48 49 4A | 03 00 00 80 | 78 79 7A | 00 00 00 00";

    [Theory]
    [InlineData(Simple, "abcabcabcabc")]
    [InlineData(Dependent, "abcabcabc")]
    [InlineData(LongLiteralsAndRawBlock, "0123456789ABCDEFGHIJxyz")]
    public void Decodes_valid_frames(string frame, string expected)
    {
        Assert.True(Lz4Frame.TryDecode(H(frame), expected.Length, out var data, out var error), error);
        Assert.Equal(expected, Text(data));
    }

    [Theory]
    [InlineData("28 B5 2F FD 60 40 00 | 00 00 00 00", 0)]                                     // wrong magic
    [InlineData("04 22 4D 18 A0 40 00 | 00 00 00 00", 0)]                                     // version 2
    [InlineData("04 22 4D 18 60 40 00 | 03 00 00 00 | 00 00 00 | 00 00 00 00", 4)]            // offset 0
    [InlineData("04 22 4D 18 60 40 00 | 04 00 00 00 | 10 61 05 00 | 00 00 00 00", 5)]         // offset beyond the output
    [InlineData("04 22 4D 18 60 40 00 | 06 00 00 00 | 35 61 62", 12)]                         // cut inside a block
    [InlineData("04 22 4D 18 60 40 00 | 06 00 00 00 | 35 61 62 63 03 00", 12)]                // end mark missing
    [InlineData("04 22 4D 18 60 40 00 | FF FF FF 7F | 35 61 62 63 03 00 | 00 00 00 00", 12)]  // block size beyond the frame
    [InlineData(Simple, 13)]                                                                  // produces less than expected
    [InlineData(Simple, 11)]                                                                  // produces more than expected
    [InlineData(Simple + " | 00", 12)]                                                        // bytes after the frame
    public void Rejects_malformed_frames(string frame, int expectedSize)
    {
        Assert.False(Lz4Frame.TryDecode(H(frame), expectedSize, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}

public class PackReaderEntryTests
{
    private static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    private static byte[] Zstd(byte[] data) { using var c = new ZstdSharp.Compressor(); return c.Wrap(data).ToArray(); }
    private const string Lz4Simple = "04 22 4D 18 60 40 00 | 06 00 00 00 | 35 61 62 63 03 00 | 00 00 00 00";

    private static (PackIndex Index, PackEntry Entry) One(TempDir dir, byte[] stored, bool compressed, uint flags = 0)
    {
        var path = new PackBuilder().WithFlags(flags).AddEntry("a/file.txt", stored, compressed).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        return (index, index.Entries[0]);
    }

    [Fact] public void Reads_an_uncompressed_entry()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, B("hello"), compressed: false);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal((EntryProblem.None, CompressionFormat.None, 5L), (c.Problem, c.Format, c.UncompressedSize));
        Assert.Equal(B("hello"), c.Bytes);
    }

    [Fact] public void Reads_the_second_of_two_entries()
    {
        using var dir = new TempDir();
        var path = new PackBuilder().AddEntry("one", B("first")).AddEntry("two", B("second")).WriteTo(dir.File("a.pack"));
        var index = PackReader.ReadIndex(path).Index!;
        Assert.Equal(B("second"), PackReader.ReadEntry(index, index.Find("two")!, CancellationToken.None).Bytes);
    }

    [Fact] public void Decompresses_zstd()
    {
        using var dir = new TempDir();
        var data = B(string.Concat(Enumerable.Repeat("total war ", 500)));
        var (index, entry) = One(dir, PackBuilder.Stored(Zstd(data), data.Length), compressed: true);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal((EntryProblem.None, CompressionFormat.Zstd, (long)data.Length), (c.Problem, c.Format, c.UncompressedSize));
        Assert.Equal(data, c.Bytes);
    }

    [Fact] public void Decompresses_lz4()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, PackBuilder.Stored(PackBuilder.Hex(Lz4Simple), 12), compressed: true);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal((EntryProblem.None, CompressionFormat.Lz4), (c.Problem, c.Format));
        Assert.Equal(B("abcabcabcabc"), c.Bytes);
    }

    [Theory]
    [InlineData("5D 00 00 80 00 FF FF FF FF", CompressionFormat.Lzma)]
    [InlineData("01 02 03 04 05 06 07 08 09", CompressionFormat.Unknown)]
    public void Other_compression_is_unsupported(string frame, CompressionFormat format)
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, PackBuilder.Stored(PackBuilder.Hex(frame), 100), compressed: true);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal((EntryProblem.UnsupportedCompression, format, 100L), (c.Problem, c.Format, c.UncompressedSize));
        Assert.Null(c.Bytes);
    }

    [Theory]
    [InlineData(13)]   // prefix claims more than the frame yields
    [InlineData(11)]   // prefix claims less
    public void Wrong_size_prefix_is_corrupt(int claimed)
    {
        using var dir = new TempDir();
        var (lz4Index, lz4Entry) = One(dir, PackBuilder.Stored(PackBuilder.Hex(Lz4Simple), claimed), compressed: true);
        Assert.Equal(EntryProblem.Corrupt, PackReader.ReadEntry(lz4Index, lz4Entry, CancellationToken.None).Problem);
        using var dir2 = new TempDir();
        var (zIndex, zEntry) = One(dir2, PackBuilder.Stored(Zstd(B("abcabcabcabc")), claimed), compressed: true);
        Assert.Equal(EntryProblem.Corrupt, PackReader.ReadEntry(zIndex, zEntry, CancellationToken.None).Problem);
    }

    [Fact] public void Damaged_zstd_frame_is_corrupt()
    {
        using var dir = new TempDir();
        var frame = Zstd(B(string.Concat(Enumerable.Repeat("total war ", 500))));
        var (index, entry) = One(dir, PackBuilder.Stored(frame[..(frame.Length / 2)], 5000), compressed: true);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal(EntryProblem.Corrupt, c.Problem); Assert.Null(c.Bytes);
    }

    [Theory]
    [InlineData(0, EntryProblem.None)]
    [InlineData(5, EntryProblem.Corrupt)]
    public void Tiny_compressed_entries(int storedLength, EntryProblem expected)
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, new byte[storedLength], compressed: true);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal(expected, c.Problem);
        if (expected == EntryProblem.None) Assert.Empty(c.Bytes!);
    }

    [Fact] public void Size_prefix_above_the_limit_is_too_large()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, PackBuilder.Stored(PackBuilder.Hex(Lz4Simple), int.MaxValue), compressed: true);
        var c = PackReader.ReadEntry(index, entry, CancellationToken.None);
        Assert.Equal((EntryProblem.TooLarge, (long)int.MaxValue), (c.Problem, c.UncompressedSize));
        using var dir2 = new TempDir();
        var (plainIndex, plainEntry) = One(dir2, B("hello"), compressed: false);
        Assert.Equal(EntryProblem.TooLarge, PackReader.ReadEntry(plainIndex, plainEntry, CancellationToken.None, maxBytes: 4).Problem);
    }

    [Fact] public void Encrypted_data_is_reported()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, B("hello"), compressed: false, flags: 0x10);
        Assert.Equal(EntryProblem.Encrypted, PackReader.ReadEntry(index, entry, CancellationToken.None).Problem);
    }

    [Fact] public void Changed_pack_is_reported()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, B("hello"), compressed: false);
        new PackBuilder().AddEntry("a/file.txt", B("hello!!")).WriteTo(dir.File("a.pack"));     // same path, other length
        Assert.Equal(EntryProblem.PackChanged, PackReader.ReadEntry(index, entry, CancellationToken.None).Problem);
        File.Delete(dir.File("a.pack"));
        Assert.Equal(EntryProblem.PackChanged, PackReader.ReadEntry(index, entry, CancellationToken.None).Problem);
    }

    [Fact] public void Cancelled_token_throws()
    {
        using var dir = new TempDir();
        var (index, entry) = One(dir, B("hello"), compressed: false);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PackReader.ReadEntry(index, entry, cts.Token));
    }
}

public class PackReaderEntryRealDataTests
{
    private static readonly string[] Relevant = [".loc", ".lua", ".xml", ".txt", ".json", ".csv", ".tsv"];
    private static bool IsRelevant(PackEntry e) =>
        e.Path.StartsWith("db/", StringComparison.OrdinalIgnoreCase) || Relevant.Contains(Path.GetExtension(e.Path).ToLowerInvariant());

    [RpfmFact] public void Zstd_entries_of_db_pack_match_rpfm_cli()
    {
        using var dir = new TempDir();
        var pack = Path.Combine(RealData.Wh3Data, "db.pack");
        RealData.RpfmExtractFolder(pack, "db", dir.Path);
        var index = PackReader.ReadIndex(pack).Index!;
        Assert.NotEmpty(index.Entries);
        Assert.All(index.Entries, e =>
        {
            var c = PackReader.ReadEntry(index, e, CancellationToken.None);
            Assert.True(c.Problem == EntryProblem.None, $"{e.Path}: {c.Problem} {c.Detail}");
            Assert.True(c.Bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dir.Path, e.Path))), e.Path);
        });
    }

    [RealDataFact] public void Every_relevant_compressed_entry_in_the_data_folder_decompresses()
    {
        var formats = new HashSet<CompressionFormat>();
        foreach (var pack in Directory.GetFiles(RealData.Wh3Data, "*.pack"))
        {
            var index = PackReader.ReadIndex(pack).Index!;
            if (index.Header.HasEncryptedData) continue;
            foreach (var e in index.Entries.Where(e => e.IsCompressed && IsRelevant(e)))
            {
                var c = PackReader.ReadEntry(index, e, CancellationToken.None);
                Assert.True(c.Problem == EntryProblem.None, $"{Path.GetFileName(pack)}:{e.Path}: {c.Problem} {c.Detail}");
                Assert.Equal(c.UncompressedSize, c.Bytes!.LongLength);
                formats.Add(c.Format);
            }
        }
        Assert.Contains(CompressionFormat.Zstd, formats);
        Assert.Contains(CompressionFormat.Lz4, formats);
    }

    [RpfmFact] public void Lz4_samples_match_rpfm_cli()
    {
        foreach (var pack in Directory.GetFiles(RealData.Wh3Data, "*.pack"))
        {
            var index = PackReader.ReadIndex(pack).Index!;
            var samples = index.Entries.Where(e => e.IsCompressed && IsRelevant(e))
                .Select(e => (Entry: e, Content: PackReader.ReadEntry(index, e, CancellationToken.None)))
                .Where(x => x.Content.Format == CompressionFormat.Lz4).Take(5).ToList();
            if (samples.Count == 0) continue;
            using var dir = new TempDir();
            RealData.RpfmExtractFiles(pack, samples.Select(s => s.Entry.Path), dir.Path);
            Assert.All(samples, s => Assert.True(s.Content.Bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dir.Path, s.Entry.Path))), s.Entry.Path));
            return;
        }
        Assert.Fail("no LZ4 entry found in the data folder");
    }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.PackFiles.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren.** `PackReader` wird `partial` oder bekommt die Methode direkt; die Leselogik liegt in `EntryReader.cs`.
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` mit beiden Variablen gesetzt. Expected: alles bestanden, die drei Tests auf echten Daten liefen.
- [ ] **Step 5: Commit** – `git commit -m "feat: read and decompress pack entries (zstd, LZ4)"`

---

### Task 4: `Schema`

**Files:**
- Create: `src/TotalWarModTool.Schema/TotalWarModTool.Schema.csproj`, `Ron.cs` (Werttypen und `RonParser`), `GameSchema.cs` (Modell), `SchemaLoader.cs`, `SchemaSource.cs`
- Create: `schemas/schema_wh3.ron`, `schemas/LICENSE`, `schemas/README.md`
- Modify: `THIRD-PARTY-NOTICES.md`
- Test: `tests/TotalWarModTool.Schema.Tests/…csproj`, `RonParserTests.cs`, `SchemaLoaderTests.cs`, `SchemaSourceTests.cs`, `ShippedSchemaTests.cs`

**Interfaces:**
- Consumes: `TempDir`, `RepoRoot`.
- Produces:

```csharp
namespace TotalWarModTool.Schema;

public abstract record RonValue;
public sealed record RonString(string Value) : RonValue;
public sealed record RonNumber(string Raw) : RonValue;
public sealed record RonBool(bool Value) : RonValue;
public sealed record RonIdent(string Name, RonValue? Payload) : RonValue;                    // None, Some(x), StringU8, SequenceU32((…))
public sealed record RonStruct(IReadOnlyDictionary<string, RonValue> Fields) : RonValue;      // (name: value, …)
public sealed record RonTuple(IReadOnlyList<RonValue> Items) : RonValue;                      // (value, …)
public sealed record RonMap(IReadOnlyList<KeyValuePair<RonValue, RonValue>> Entries) : RonValue;   // { key: value, … }
public sealed record RonList(IReadOnlyList<RonValue> Items) : RonValue;                       // [value, …]
public sealed record RonError(int Line, int Column, string Message);                          // both 1-based

public static class RonParser
{
    public static bool TryParse(string text, out RonValue? value, out RonError? error);
}

public enum FieldType { Boolean, F32, F64, I16, I32, I64, ColourRgb, StringU8, StringU16, OptionalStringU8, Unsupported }
public sealed record TableReference(string Table, string Column);
public sealed record FieldDefinition(string Name, FieldType Type, string RawType, bool IsKey, TableReference? Reference);
public sealed record TableDefinition(int Version, IReadOnlyList<FieldDefinition> Fields);

public sealed class GameSchema
{
    public GameSchema(IReadOnlyDictionary<string, IReadOnlyList<TableDefinition>> tables);
    public IReadOnlyDictionary<string, IReadOnlyList<TableDefinition>> Tables { get; }   // key lookup ignores case
    public TableDefinition? Find(string tableName, int version);
}

public enum SchemaProblem { None, NotFound, Unreadable, InvalidSyntax, InvalidStructure }
public sealed record SchemaLoadResult(GameSchema? Schema, SchemaProblem Problem, string? Detail);
public static class SchemaLoader { public static SchemaLoadResult Load(string path); }

public enum SchemaOrigin { None, Shipped, Override }
public sealed record ResolvedSchema(GameSchema? Schema, SchemaOrigin Origin, string? Path, DateTime? FileDateUtc,
    SchemaLoadResult? OverrideAttempt, SchemaLoadResult? ShippedAttempt);
public static class SchemaSource
{
    public static ResolvedSchema Resolve(string shippedFolder, string? overrideFolder, string fileName);
}
```

Festlegungen:
- RON-Teilmenge: Leerraum und `//`-Kommentare; Texte in `"…"` mit den Escapes `\"`, `\\`, `\n`, `\t`, `\r`, `\0`, `\'`, `\u{hex}`; Zahlen (Ziffern, `-`, `.`, Exponent) als Rohtext; `true` / `false`; Bezeichner, optional gefolgt von einer Klammer als Nutzlast; `(…)` ist ein Struct, wenn der erste Inhalt `name:` ist, sonst ein Tupel; leeres `()` ist ein leeres Struct; `{…}` Map; `[…]` Liste; Komma am Ende erlaubt.
- Der Parser wirft nie. Fehler → `false` mit Zeile, Spalte und Meldung. Text nach dem Wurzelwert ist ein Fehler. Verschachtelung tiefer als 200 ist ein Fehler (kein Stapelüberlauf).
- `SchemaLoader`: Wurzel ist ein Struct mit `definitions` (Map: Tabellenname → Liste von Structs mit `version` und `fields`). Feld: `name` (Text), `field_type` (Bezeichner), `is_key` (optional, Standard `false`), `is_reference` (optional; `Some((tabelle, spalte))`). Alles andere wird ignoriert. Fehlt `definitions` oder passt die Form nicht → `InvalidStructure` mit dem Namen der Tabelle im `Detail`.
- `field_type`: `Boolean`, `F32`, `F64`, `I16`, `I32`, `I64`, `StringU8`, `StringU16`, `OptionalStringU8` wörtlich; `ColourRGB` → `ColourRgb`; jeder andere Name (auch mit Nutzlast) → `Unsupported`. `RawType` behält den Namen aus der Datei.
- `Find`: Tabellenname ohne Groß- und Kleinschreibung, Version exakt; bei mehreren Definitionen gleicher Version die erste.
- `SchemaSource.Resolve`: Ist `overrideFolder` gesetzt, wird `<overrideFolder>\<fileName>` geladen. Gelingt das → `Override`. Sonst `<shippedFolder>\<fileName>` → `Shipped`. Gelingt beides nicht → `Origin None`, `Schema null`. Beide Versuche stehen im Ergebnis (`null`, wenn nicht versucht). `FileDateUtc` ist die letzte Änderung der benutzten Datei.
- `schemas/schema_wh3.ron` und `schemas/LICENSE`: unveränderte Kopien aus dem RPFM-Schema-Ordner des Nutzers (`%APPDATA%\FrodoWazEre\rpfm\config\schemas\`). `schemas/README.md` (Englisch): Quelle `https://github.com/Frodo45127/rpfm-schemas`, Stand Commit `5d841c5`, `schema_wh3.ron` zuletzt geändert in `1d5ce11` (2026-09-24, „Updated wh3 schemas for patch 9.0“), übernommen am 2026-10-02, Lizenz MIT.
- `THIRD-PARTY-NOTICES.md`: Abschnitt „RPFM schemas“ mit der Quelle, „Copyright (c) 2020 Ismael Gutiérrez González“ und Verweis auf `schemas/LICENSE`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class RonParserTests
{
    private static RonValue Parse(string text) { Assert.True(RonParser.TryParse(text, out var v, out var e), e?.Message); return v!; }

    [Fact] public void Parses_structs_tuples_maps_and_lists()
    {
        var root = (RonStruct)Parse("""( a: 1, b: "x", c: [true, false,], d: { "k": -2.5, }, e: ("p", "q"), f: (), )""");
        Assert.Equal(new RonNumber("1"), root.Fields["a"]);
        Assert.Equal(new RonString("x"), root.Fields["b"]);
        Assert.Equal([new RonBool(true), new RonBool(false)], ((RonList)root.Fields["c"]).Items);
        var entry = Assert.Single(((RonMap)root.Fields["d"]).Entries);
        Assert.Equal(new RonString("k"), entry.Key);
        Assert.Equal(new RonNumber("-2.5"), entry.Value);
        Assert.Equal([new RonString("p"), new RonString("q")], ((RonTuple)root.Fields["e"]).Items);
        Assert.Empty(((RonStruct)root.Fields["f"]).Fields);
    }

    [Fact] public void Parses_identifiers_with_and_without_payload()
    {
        var list = (RonList)Parse("""[None, StringU8, Some("x"), Some(("t", "c")), SequenceU32((version: 0))]""");
        Assert.Equal(new RonIdent("None", null), list.Items[0]);
        Assert.Equal(new RonIdent("StringU8", null), list.Items[1]);
        Assert.Equal(new RonString("x"), Assert.Single(((RonTuple)((RonIdent)list.Items[2]).Payload!).Items));
        var reference = (RonTuple)Assert.Single(((RonTuple)((RonIdent)list.Items[3]).Payload!).Items);
        Assert.Equal([new RonString("t"), new RonString("c")], reference.Items);
        Assert.Equal("SequenceU32", ((RonIdent)list.Items[4]).Name);
    }

    [Fact] public void Parses_escapes_and_comments()
    {
        var s = (RonString)Parse("// header\n\"a\\\"b\\\\c\\n\\u{e9}\" // tail");
        Assert.Equal("a\"b\\c\né", s.Value);
    }

    [Theory]
    [InlineData("(a: )", 1, 5)]
    [InlineData("(a: 1", 1, 6)]
    [InlineData("[1, 2] x", 1, 8)]
    [InlineData("\"open", 1, 6)]
    [InlineData("(\n  a: 1,\n  b: ?\n)", 3, 6)]
    public void Reports_errors_with_position(string text, int line, int column)
    {
        Assert.False(RonParser.TryParse(text, out var value, out var error));
        Assert.Null(value);
        Assert.Equal((line, column), (error!.Line, error.Column));
    }

    [Fact] public void Deep_nesting_is_an_error_not_a_stack_overflow()
    {
        Assert.False(RonParser.TryParse(new string('[', 100_000), out _, out var error));
        Assert.NotNull(error);
    }
}

public class SchemaLoaderTests
{
    private const string Sample = """
        (
            version: 5,
            definitions: {
                "units_tables": [
                    (
                        version: 2,
                        fields: [
                            (name: "key", field_type: StringU8, is_key: true, default_value: None, is_reference: None, ca_order: 0, enum_values: {}),
                            (name: "faction", field_type: OptionalStringU8, is_key: false, is_reference: Some(("factions", "key"))),
                            (name: "tint", field_type: ColourRGB, is_key: false, is_reference: None),
                            (name: "extra", field_type: SequenceU32((version: 0, fields: [])), is_reference: None),
                            (name: "maybe", field_type: OptionalI32),
                        ],
                        localised_fields: [],
                    ),
                    ( version: 1, fields: [ (name: "key", field_type: StringU8, is_key: true) ] ),
                ],
            },
            patches: {},
        )
        """;

    [Fact] public void Loads_tables_versions_fields_and_references()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.ron"), Sample);
        var r = SchemaLoader.Load(dir.File("s.ron"));
        Assert.Equal(SchemaProblem.None, r.Problem);
        var v2 = r.Schema!.Find("UNITS_TABLES", 2)!;
        Assert.Equal(
            [new FieldDefinition("key", FieldType.StringU8, "StringU8", true, null),
             new FieldDefinition("faction", FieldType.OptionalStringU8, "OptionalStringU8", false, new TableReference("factions", "key")),
             new FieldDefinition("tint", FieldType.ColourRgb, "ColourRGB", false, null),
             new FieldDefinition("extra", FieldType.Unsupported, "SequenceU32", false, null),
             new FieldDefinition("maybe", FieldType.Unsupported, "OptionalI32", false, null)],
            v2.Fields);
        Assert.Single(r.Schema.Find("units_tables", 1)!.Fields);
        Assert.Null(r.Schema.Find("units_tables", 3));
        Assert.Null(r.Schema.Find("other_tables", 1));
    }

    [Theory]
    [InlineData("( definitions: {", SchemaProblem.InvalidSyntax)]
    [InlineData("( version: 5 )", SchemaProblem.InvalidStructure)]
    [InlineData("""( definitions: { "t_tables": [ ( version: "x", fields: [] ) ] } )""", SchemaProblem.InvalidStructure)]
    [InlineData("""( definitions: { "t_tables": [ ( version: 1, fields: [ (field_type: StringU8) ] ) ] } )""", SchemaProblem.InvalidStructure)]
    public void Broken_schema_gives_a_problem(string text, SchemaProblem expected)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.ron"), text);
        var r = SchemaLoader.Load(dir.File("s.ron"));
        Assert.Equal(expected, r.Problem);
        Assert.Null(r.Schema);
        Assert.False(string.IsNullOrWhiteSpace(r.Detail));
    }

    [Fact] public void Missing_file_is_NotFound() => Assert.Equal(SchemaProblem.NotFound, SchemaLoader.Load(@"X:\none.ron").Problem);
}

public class SchemaSourceTests
{
    private const string Good = """( definitions: { "a_tables": [ ( version: 1, fields: [ (name: "key", field_type: StringU8, is_key: true) ] ) ] } )""";

    [Fact] public void Override_wins_when_it_loads()
    {
        using var shipped = new TempDir(); using var over = new TempDir();
        File.WriteAllText(shipped.File("s.ron"), Good);
        File.WriteAllText(over.File("s.ron"), Good.Replace("a_tables", "b_tables"));
        var r = SchemaSource.Resolve(shipped.Path, over.Path, "s.ron");
        Assert.Equal((SchemaOrigin.Override, over.File("s.ron")), (r.Origin, r.Path));
        Assert.True(r.Schema!.Tables.ContainsKey("b_tables"));
        Assert.Equal(File.GetLastWriteTimeUtc(over.File("s.ron")), r.FileDateUtc);
        Assert.Null(r.ShippedAttempt);
    }

    [Theory]
    [InlineData(null, SchemaProblem.NotFound)]          // override folder has no such file
    [InlineData("( broken", SchemaProblem.InvalidSyntax)]
    public void Falls_back_to_shipped_and_keeps_the_override_problem(string? overrideContent, SchemaProblem expected)
    {
        using var shipped = new TempDir(); using var over = new TempDir();
        File.WriteAllText(shipped.File("s.ron"), Good);
        if (overrideContent != null) File.WriteAllText(over.File("s.ron"), overrideContent);
        var r = SchemaSource.Resolve(shipped.Path, over.Path, "s.ron");
        Assert.Equal((SchemaOrigin.Shipped, shipped.File("s.ron"), expected), (r.Origin, r.Path, r.OverrideAttempt!.Problem));
        Assert.NotNull(r.Schema);
    }

    [Fact] public void No_override_folder_uses_shipped_without_an_attempt()
    {
        using var shipped = new TempDir();
        File.WriteAllText(shipped.File("s.ron"), Good);
        var r = SchemaSource.Resolve(shipped.Path, null, "s.ron");
        Assert.Equal(SchemaOrigin.Shipped, r.Origin); Assert.Null(r.OverrideAttempt);
    }

    [Fact] public void Nothing_usable_gives_origin_None()
    {
        using var shipped = new TempDir();
        var r = SchemaSource.Resolve(shipped.Path, null, "s.ron");
        Assert.Equal((SchemaOrigin.None, (GameSchema?)null, SchemaProblem.NotFound), (r.Origin, r.Schema, r.ShippedAttempt!.Problem));
    }
}

public class ShippedSchemaTests
{
    [Fact] public void Shipped_wh3_schema_loads_and_has_the_expected_shape()
    {
        var r = SchemaLoader.Load(Path.Combine(RepoRoot.Find(), "schemas", "schema_wh3.ron"));
        Assert.Equal(SchemaProblem.None, r.Problem);
        var fields = r.Schema!.Tables.Values.SelectMany(defs => defs).SelectMany(d => d.Fields).ToList();
        Assert.True(r.Schema.Tables.Count > 1500);
        Assert.True(fields.Count(f => f.Reference != null) > 5000);
        Assert.All(fields, f => Assert.False(string.IsNullOrEmpty(f.Name)));
        var unsupported = fields.Where(f => f.Type == FieldType.Unsupported).Select(f => f.RawType).Distinct().ToList();
        Assert.True(unsupported.Count <= 3, string.Join(", ", unsupported));
        Assert.True(File.Exists(Path.Combine(RepoRoot.Find(), "schemas", "LICENSE")));
    }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.Schema.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren.** Schema-Dateien kopieren. Stimmt eine erwartete Fehlerposition in `Reports_errors_with_position` nicht mit dem eigenen Parser überein, gilt die Regel: Die Position ist das erste Zeichen, das nicht passt, oder bei fehlendem Abschluss die Stelle direkt hinter dem letzten Zeichen. Weicht ein Erwartungswert davon ab, wird er im Test korrigiert und im Bericht genannt.
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx`
- [ ] **Step 5: Commit** – `git commit -m "feat: add RON schema loader and ship the WH3 schema"`

---

### Task 5: `Tables`

**Files:**
- Create: `src/TotalWarModTool.Tables/TotalWarModTool.Tables.csproj` (referenziert `Schema`), `DecodedTable.cs`, `DbTableDecoder.cs`, `DbTablePath.cs`, `LocDecoder.cs`
- Test: `tests/TotalWarModTool.Tables.Tests/…csproj` (referenziert zusätzlich `PackFiles` für die Tests auf echten Daten), `TableBytes.cs`, `DbTableDecoderTests.cs`, `LocDecoderTests.cs`, `RpfmTsv.cs`, `TablesRealDataTests.cs`

**Interfaces:**
- Consumes: `GameSchema`, `TableDefinition`, `FieldDefinition`, `FieldType`, `SchemaLoader` (Task 4); `PackReader`, `PackIndex` (Task 2, 3, nur in Tests); `RealData`.
- Produces:

```csharp
namespace TotalWarModTool.Tables;

public enum TableDecodeStatus { Ok, TableNotInSchema, VersionNotInSchema, UnsupportedFieldType, Failed }

// Rows is empty unless Status is Ok. Cell types: Boolean bool, I16 short, I32 int, I64 long, F32 float, F64 double,
// ColourRgb string "RRGGBB", StringU8/StringU16 string, OptionalStringU8 string ("" when absent).
public sealed record DecodedTable(TableDecodeStatus Status, string TableName, int Version, bool HasGuid,
    TableDefinition? Definition, IReadOnlyList<object?[]> Rows, string? Detail);

public static class DbTableDecoder
{
    public static DecodedTable Decode(byte[] data, string tableName, GameSchema schema);
}

public static class DbTablePath
{
    // "db/units_tables/data__" -> "units_tables". False for anything that is not db/<table>/<file>.
    public static bool TryGetTableName(string entryPath, out string tableName);
}

public sealed record LocRow(string Key, string Text, bool Tooltip);
public enum LocDecodeStatus { Ok, Failed }
public sealed record DecodedLoc(LocDecodeStatus Status, IReadOnlyList<LocRow> Rows, string? Detail);
public static class LocDecoder { public static DecodedLoc Decode(byte[] data); }
```

Format (Spec 3.4, 3.8):

```text
DB table:
   [FD FE FC FF, u16 n, n UTF-16 code units]     optional GUID marker
   [FC FD FE FF, i32 version]                    optional version marker; without it the version is 0
   1 byte (ignored)
   u32 row count
   rows: the fields of the definition, in definition order
     Boolean           1 byte, must be 0 or 1
     I16 / I32 / I64   2 / 4 / 8 bytes, little endian
     F32 / F64         4 / 8 bytes
     ColourRGB         u32, shown as upper-case hex with at least 6 digits ("X6")
     StringU8          u16 byte count, UTF-8 bytes
     StringU16         u16 code-unit count, UTF-16LE
     OptionalStringU8  1 byte flag (0 or 1); only with flag 1 a StringU8 follows
   The table must end exactly after the last row.

Loc file:
   FF FE, "LOC" (ASCII), 00, i32 version (must be 1), u32 row count
   rows: StringU16 key, StringU16 text, 1 byte flag (0 or 1)
   The file must end exactly after the last row.
```

Festlegungen:
- Reihenfolge der Prüfung: Kopf lesen → Tabelle im Schema? (`TableNotInSchema`) → Version im Schema? (`VersionNotInSchema`, `Detail` nennt die Version) → enthält die Definition ein Feld vom Typ `Unsupported`? (`UnsupportedFieldType`, `Detail` nennt Feld und `RawType`) → Zeilen lesen.
- `Failed` mit `Detail` (Zeile, Spalte, Grund) bei: Daten zu kurz, Boolean oder Flag nicht 0/1, Text reicht über das Ende, Bytes nach der letzten Zeile, Kopf unvollständig. Bei `Failed` ist `Rows` leer, `Definition` bleibt gesetzt.
- Vor dem Anlegen der Zeilenliste: Hat die Definition mindestens ein Feld und ist die Zeilenanzahl größer als die Zahl der restlichen Bytes → `Failed`. Die Zeilenanzahl dient nie ungeprüft als Kapazität.
- `Version` und `HasGuid` sind bei jedem Status gesetzt, sobald der Kopf lesbar war.
- Decoder werfen bei keinem Eingabewert. `data` der Länge 0 → `Failed`.

- [ ] **Step 1: Failing Tests schreiben**

`TableBytes` (Testhelfer im Testprojekt):

```csharp
internal sealed class TableBytes
{
    public TableBytes Guid(string guid = "46e87609-af6e-4857-bea4-f29b4fbd55fd");   // FD FE FC FF, u16 length, UTF-16
    public TableBytes Version(int version);                                          // FC FD FE FF, i32
    public TableBytes Rows(uint count);                                              // 01, u32 count
    public TableBytes Bool(bool v); public TableBytes I16(short v); public TableBytes I32(int v); public TableBytes I64(long v);
    public TableBytes F32(float v); public TableBytes F64(double v); public TableBytes Colour(uint v);
    public TableBytes Str8(string v); public TableBytes Str16(string v); public TableBytes OptStr8(string? v);
    public TableBytes Raw(params byte[] bytes);
    public byte[] Build();
}
```

```csharp
public class DbTableDecoderTests
{
    private static FieldDefinition F(string name, FieldType type, bool key = false) => new(name, type, type.ToString(), key, null);
    private static GameSchema Schema(params (string Table, int Version, FieldDefinition[] Fields)[] defs) =>
        new(defs.GroupBy(d => d.Table).ToDictionary(g => g.Key, g => (IReadOnlyList<TableDefinition>)g.Select(d => new TableDefinition(d.Version, d.Fields)).ToList()));

    private static readonly FieldDefinition[] AllTypes =
    [
        F("key", FieldType.StringU8, key: true), F("flag", FieldType.Boolean), F("small", FieldType.I16), F("count", FieldType.I32),
        F("big", FieldType.I64), F("ratio", FieldType.F32), F("exact", FieldType.F64), F("tint", FieldType.ColourRgb),
        F("wide", FieldType.StringU16), F("maybe", FieldType.OptionalStringU8),
    ];

    [Fact] public void Decodes_every_supported_field_type()
    {
        var data = new TableBytes().Guid().Version(3).Rows(2)
            .Str8("ünit_a").Bool(true).I16(-2).I32(70000).I64(5_000_000_000).F32(1.5f).F64(2.25).Colour(0x004080FF).Str16("wide ü").OptStr8("yes")
            .Str8("unit_b").Bool(false).I16(7).I32(-1).I64(-9).F32(0f).F64(-0.5).Colour(0).Str16("").OptStr8(null)
            .Build();
        var t = DbTableDecoder.Decode(data, "units_tables", Schema(("units_tables", 3, AllTypes)));
        Assert.Equal((TableDecodeStatus.Ok, "units_tables", 3, true), (t.Status, t.TableName, t.Version, t.HasGuid));
        Assert.Equal(new object?[] { "ünit_a", true, (short)-2, 70000, 5_000_000_000L, 1.5f, 2.25, "4080FF", "wide ü", "yes" }, t.Rows[0]);
        Assert.Equal(new object?[] { "unit_b", false, (short)7, -1, -9L, 0f, -0.5, "000000", "", "" }, t.Rows[1]);
        Assert.Equal(10, t.Definition!.Fields.Count);
    }

    [Fact] public void Without_markers_the_version_is_zero()
    {
        var data = new TableBytes().Rows(1).Str8("a").Build();
        var t = DbTableDecoder.Decode(data, "x_tables", Schema(("x_tables", 0, [F("key", FieldType.StringU8)])));
        Assert.Equal((TableDecodeStatus.Ok, 0, false, 1), (t.Status, t.Version, t.HasGuid, t.Rows.Count));
    }

    [Fact] public void Picks_the_definition_of_the_file_version()
    {
        var schema = Schema(("x_tables", 1, [F("key", FieldType.StringU8)]), ("x_tables", 2, [F("key", FieldType.StringU8), F("n", FieldType.I32)]));
        var t = DbTableDecoder.Decode(new TableBytes().Version(2).Rows(1).Str8("a").I32(5).Build(), "X_TABLES", schema);
        Assert.Equal((TableDecodeStatus.Ok, 2), (t.Status, t.Definition!.Fields.Count));
    }

    [Fact] public void Empty_table_is_ok()
    {
        var t = DbTableDecoder.Decode(new TableBytes().Guid().Version(1).Rows(0).Build(), "x_tables", Schema(("x_tables", 1, [F("key", FieldType.StringU8)])));
        Assert.Equal((TableDecodeStatus.Ok, 0), (t.Status, t.Rows.Count));
    }

    [Fact] public void Unknown_table_and_unknown_version()
    {
        var schema = Schema(("x_tables", 1, [F("key", FieldType.StringU8)]));
        var data = new TableBytes().Version(9).Rows(0).Build();
        Assert.Equal(TableDecodeStatus.TableNotInSchema, DbTableDecoder.Decode(data, "y_tables", schema).Status);
        var t = DbTableDecoder.Decode(data, "x_tables", schema);
        Assert.Equal((TableDecodeStatus.VersionNotInSchema, 9), (t.Status, t.Version));
        Assert.Contains("9", t.Detail); Assert.Empty(t.Rows);
    }

    [Fact] public void Unsupported_field_type_is_reported_by_name()
    {
        var schema = Schema(("x_tables", 0, [F("key", FieldType.StringU8), new FieldDefinition("maybe", FieldType.Unsupported, "OptionalI32", false, null)]));
        var t = DbTableDecoder.Decode(new TableBytes().Rows(0).Build(), "x_tables", schema);
        Assert.Equal(TableDecodeStatus.UnsupportedFieldType, t.Status);
        Assert.Contains("maybe", t.Detail); Assert.Contains("OptionalI32", t.Detail);
    }

    [Fact] public void Wrong_layout_fails_without_partial_rows()
    {
        // file was written with (string, bool); the schema expects (string, i32, bool)
        var schema = Schema(("x_tables", 1, [F("key", FieldType.StringU8), F("n", FieldType.I32), F("flag", FieldType.Boolean)]));
        var data = new TableBytes().Version(1).Rows(2).Str8("first").Bool(true).Str8("second").Bool(false).Build();
        var t = DbTableDecoder.Decode(data, "x_tables", schema);
        Assert.Equal(TableDecodeStatus.Failed, t.Status);
        Assert.Empty(t.Rows); Assert.NotNull(t.Definition); Assert.False(string.IsNullOrWhiteSpace(t.Detail));
    }

    [Fact] public void Boolean_that_is_not_0_or_1_fails()
    {
        var schema = Schema(("x_tables", 0, [F("flag", FieldType.Boolean)]));
        var t = DbTableDecoder.Decode(new TableBytes().Rows(1).Raw(2).Build(), "x_tables", schema);
        Assert.Equal(TableDecodeStatus.Failed, t.Status);
        Assert.Contains("flag", t.Detail);
    }

    [Fact] public void Bytes_after_the_last_row_fail()
    {
        var schema = Schema(("x_tables", 0, [F("flag", FieldType.Boolean)]));
        var t = DbTableDecoder.Decode(new TableBytes().Rows(1).Bool(true).Raw(0xAA).Build(), "x_tables", schema);
        Assert.Equal(TableDecodeStatus.Failed, t.Status);
        Assert.Empty(t.Rows);
    }

    [Fact] public void String_that_overruns_the_table_fails()
    {
        var schema = Schema(("x_tables", 0, [F("key", FieldType.StringU8)]));
        var data = new TableBytes().Rows(1).Raw(0xFF, 0xFF, 0x41).Build();   // claims 65535 bytes, has 1
        Assert.Equal(TableDecodeStatus.Failed, DbTableDecoder.Decode(data, "x_tables", schema).Status);
    }

    [Fact] public void Huge_row_count_fails_without_allocating()
    {
        var schema = Schema(("x_tables", 0, [F("key", FieldType.StringU8)]));
        var t = DbTableDecoder.Decode(new TableBytes().Rows(uint.MaxValue).Build(), "x_tables", schema);
        Assert.Equal(TableDecodeStatus.Failed, t.Status);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFD, 0xFE, 0xFC, 0xFF, 0x10 })]              // GUID marker cut off
    [InlineData(new byte[] { 0xFC, 0xFD, 0xFE, 0xFF, 0x01, 0x00 })]        // version marker cut off
    [InlineData(new byte[] { 0x01, 0x05, 0x00 })]                          // row count cut off
    public void Truncated_header_fails(byte[] data) =>
        Assert.Equal(TableDecodeStatus.Failed, DbTableDecoder.Decode(data, "x_tables", new GameSchema(new Dictionary<string, IReadOnlyList<TableDefinition>>
            { ["x_tables"] = [new TableDefinition(0, [new FieldDefinition("key", FieldType.StringU8, "StringU8", true, null)])] })).Status);

    [Theory]
    [InlineData("db/units_tables/data__", true, "units_tables")]
    [InlineData("DB/Units_Tables/my mod file", true, "Units_Tables")]
    [InlineData("db/units_tables", false, "")]
    [InlineData("text/db/units.loc", false, "")]
    [InlineData("db/a/b/c", false, "")]
    public void TryGetTableName(string path, bool expected, string table)
    {
        Assert.Equal(expected, DbTablePath.TryGetTableName(path, out var name));
        Assert.Equal(table, name);
    }
}

public class LocDecoderTests
{
    private static byte[] Loc(int version, uint count, Action<TableBytes> rows)
    {
        var b = new TableBytes().Raw(0xFF, 0xFE, 0x4C, 0x4F, 0x43, 0x00).I32(version).Raw(BitConverter.GetBytes(count));
        rows(b); return b.Build();
    }

    [Fact] public void Decodes_rows()
    {
        var d = LocDecoder.Decode(Loc(1, 2, b => b.Str16("units_name_a").Str16("Schwertkämpfer").Bool(true).Str16("units_name_b").Str16("").Bool(false)));
        Assert.Equal(LocDecodeStatus.Ok, d.Status);
        Assert.Equal([new LocRow("units_name_a", "Schwertkämpfer", true), new LocRow("units_name_b", "", false)], d.Rows);
    }

    [Fact] public void Empty_loc_is_ok() => Assert.Equal((LocDecodeStatus.Ok, 0), (LocDecoder.Decode(Loc(1, 0, _ => { })).Status, LocDecoder.Decode(Loc(1, 0, _ => { })).Rows.Count));

    [Fact] public void Other_version_wrong_marker_bad_flag_leftover_and_huge_count_fail()
    {
        Assert.Equal(LocDecodeStatus.Failed, LocDecoder.Decode(Loc(2, 0, _ => { })).Status);
        Assert.Equal(LocDecodeStatus.Failed, LocDecoder.Decode(new TableBytes().Raw(0xFF, 0xFE, 0x4C, 0x4F, 0x58, 0x00).I32(1).Raw(0, 0, 0, 0).Build()).Status);
        Assert.Equal(LocDecodeStatus.Failed, LocDecoder.Decode(Loc(1, 1, b => b.Str16("k").Str16("t").Raw(7))).Status);
        Assert.Equal(LocDecodeStatus.Failed, LocDecoder.Decode(Loc(1, 1, b => b.Str16("k").Str16("t").Bool(true).Raw(0))).Status);
        Assert.Equal(LocDecodeStatus.Failed, LocDecoder.Decode(Loc(1, uint.MaxValue, _ => { })).Status);
        Assert.Equal(LocDecodeStatus.Failed, LocDecoder.Decode([]).Status);
        Assert.All(new[] { LocDecoder.Decode([]), LocDecoder.Decode(Loc(2, 0, _ => { })) }, d => { Assert.Empty(d.Rows); Assert.False(string.IsNullOrWhiteSpace(d.Detail)); });
    }
}
```

`RpfmTsv` (Testhelfer): liest einen TSV-Export von `rpfm_cli`. Zeile 1 sind die Spaltennamen, Zeile 2 ist eine Metazeile und wird übergangen, leere Zeilen am Ende zählen nicht. `static bool CellEquals(object? mine, string tsv, bool ignoreCase = false)`: `bool` gegen `true` / `false`; Ganzzahlen invariant; `float` und `double` numerisch mit Toleranz 0,00006 (RPFM schreibt 4 Nachkommastellen); Texte exakt oder nach Ersetzen von `\\t` → Tab und `\\n` → Zeilenumbruch im TSV-Wert; mit `ignoreCase` ohne Rücksicht auf Groß- und Kleinschreibung (für Farbwerte).

```csharp
public class TablesRealDataTests
{
    private static GameSchema Wh3Schema() => SchemaLoader.Load(Path.Combine(RepoRoot.Find(), "schemas", "schema_wh3.ron")).Schema!;
    private static string SchemaPath => Path.Combine(RepoRoot.Find(), "schemas", "schema_wh3.ron");

    [RealDataFact] public void Every_table_of_db_pack_is_ok_or_has_a_diagnosis()
    {
        var schema = Wh3Schema();
        var index = PackReader.ReadIndex(Path.Combine(RealData.Wh3Data, "db.pack")).Index!;
        int ok = 0, total = 0;
        foreach (var e in index.Entries)
        {
            if (!DbTablePath.TryGetTableName(e.Path, out var table)) continue;
            total++;
            var t = DbTableDecoder.Decode(PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!, table, schema);
            if (t.Status == TableDecodeStatus.Ok) { ok++; Assert.All(t.Rows, row => Assert.Equal(t.Definition!.Fields.Count, row.Length)); }
            else { Assert.Empty(t.Rows); if (t.Status != TableDecodeStatus.TableNotInSchema) Assert.False(string.IsNullOrWhiteSpace(t.Detail), e.Path); }
        }
        Assert.True(total > 1000);
        Assert.True(ok >= total * 0.95, $"{ok} of {total} tables decoded");
    }

    [RpfmFact] public void Db_pack_tables_match_rpfm_cli_tsv()
    {
        using var dir = new TempDir();
        var pack = Path.Combine(RealData.Wh3Data, "db.pack");
        RealData.RpfmExtractFolder(pack, "db", dir.Path, SchemaPath);
        var schema = Wh3Schema();
        var index = PackReader.ReadIndex(pack).Index!;
        int compared = 0;
        foreach (var e in index.Entries)
        {
            var tsvPath = Path.Combine(dir.Path, e.Path + ".tsv");
            if (!DbTablePath.TryGetTableName(e.Path, out var table) || !File.Exists(tsvPath)) continue;
            var t = DbTableDecoder.Decode(PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!, table, schema);
            Assert.True(t.Status == TableDecodeStatus.Ok, $"{e.Path}: {t.Status} {t.Detail}");
            var tsv = RpfmTsv.Read(tsvPath);
            Assert.Equal(tsv.Rows.Count, t.Rows.Count);
            // rpfm merges colour columns into one display column; compare every TSV column that exists in the definition
            for (int c = 0; c < tsv.Columns.Count; c++)
            {
                int field = t.Definition!.Fields.ToList().FindIndex(f => f.Name == tsv.Columns[c]);
                if (field < 0) continue;
                bool colour = t.Definition.Fields[field].Type == FieldType.ColourRgb;
                for (int r = 0; r < tsv.Rows.Count; r++)
                    Assert.True(RpfmTsv.CellEquals(t.Rows[r][field], tsv.Rows[r][c], ignoreCase: colour), $"{e.Path} row {r} column {tsv.Columns[c]}");
            }
            compared++;
        }
        Assert.True(compared > 1000);
    }

    [RpfmFact] public void Loc_files_match_rpfm_cli_tsv()
    {
        using var dir = new TempDir();
        var pack = Path.Combine(RealData.Wh3Data, "local_en.pack");
        RealData.RpfmExtractFolder(pack, "/", dir.Path, SchemaPath);
        var index = PackReader.ReadIndex(pack).Index!;
        int compared = 0;
        foreach (var e in index.Entries.Where(e => e.Path.EndsWith(".loc", StringComparison.OrdinalIgnoreCase)))
        {
            var d = LocDecoder.Decode(PackReader.ReadEntry(index, e, CancellationToken.None).Bytes!);
            Assert.True(d.Status == LocDecodeStatus.Ok, $"{e.Path}: {d.Detail}");
            var tsv = RpfmTsv.Read(Path.Combine(dir.Path, e.Path + ".tsv"));
            Assert.Equal(tsv.Rows.Count, d.Rows.Count);
            for (int r = 0; r < d.Rows.Count; r++)
                Assert.True(tsv.Rows[r][0] == d.Rows[r].Key && RpfmTsv.CellEquals(d.Rows[r].Text, tsv.Rows[r][1]) && RpfmTsv.CellEquals(d.Rows[r].Tooltip, tsv.Rows[r][2]), $"{e.Path} row {r}");
            compared++;
        }
        Assert.True(compared > 100);
    }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.Tables.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren**
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` mit beiden Variablen. Expected: alles bestanden, die drei Tests auf echten Daten liefen.
- [ ] **Step 5: Commit** – `git commit -m "feat: decode DB tables and loc files with the schema"`

---

### Task 6: `Workspace` – Pack-Katalog

**Files:**
- Create: `src/TotalWarModTool.Workspace/TotalWarModTool.Workspace.csproj` (referenziert `GameProfiles`, `PackFiles`, `Operations`), `PackSource.cs`, `PackCatalog.cs`
- Modify: `docs/specs/2026-10-02-fundament.md` (nur falls die Umsetzung von 4.2 oder 4.3 abweicht), `Backlog.txt`
- Test: `tests/TotalWarModTool.Workspace.Tests/…csproj`, `PackCatalogTests.cs`, `PackCatalogRealDataTests.cs`

**Interfaces:**
- Consumes: `GameProfile` (Task 1); `PackReader.ReadHeader`, `PackHeader`, `PackType`, `PackProblem`, `FileFingerprint` (Task 2); `OperationProgress(long Done, long? Total, string? Item)` aus `TotalWarModTool.Operations` (Plan 1); `PackBuilder`, `RealData`.
- Produces:

```csharp
namespace TotalWarModTool.Workspace;

public enum PackOrigin { Vanilla, DataFolderMod, Workshop }

public sealed record PackSource(string Path, string Name, PackOrigin Origin, string? WorkshopId,
    FileFingerprint? Fingerprint, PackHeader? Header, PackProblem Problem, string? Detail);

public sealed record PackCatalogResult(IReadOnlyList<PackSource> Packs, bool UsedManifest);

public static class PackCatalog
{
    // Throws OperationCanceledException when cancelled. Missing folders give an empty list, not an error.
    public static PackCatalogResult Scan(GameProfile profile, string installPath, string? workshopPath,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken);
}
```

Festlegungen:
- Datenordner: `<installPath>\<profile.DataFolder>\*.pack`, nur die oberste Ebene. Workshop: `<workshopPath>\<id>\*.pack`, genau eine Ebene tief; `WorkshopId` ist der Ordnername.
- Pro Pack: `FileFingerprint.TryRead` und `PackReader.ReadHeader` (28 Bytes). Ein Header-Problem landet in `Problem` und `Detail`; das Pack bleibt in der Liste.
- Manifest: Ist `profile.VanillaManifestFile` gesetzt und `<Datenordner>\<Datei>` lesbar, gilt `UsedManifest = true`. Jede Zeile wird am ersten Tab geteilt; zählt, was auf `.pack` endet, nur der Dateiname, ohne Groß- und Kleinschreibung. Vanilla ist dann genau, was im Manifest steht.
- Ohne Manifest: Vanilla ist ein Pack im Datenordner, dessen Typ (Name des `PackType`, ohne Groß- und Kleinschreibung) in `profile.VanillaPackTypes` steht. Ein Pack mit Header-Problem ist dann `DataFolderMod`.
- Packs unter dem Workshop-Ordner sind immer `Workshop`.
- Reihenfolge: erst `Vanilla`, dann `DataFolderMod`, dann `Workshop`; innerhalb nach `Name` (ordinal, ohne Groß- und Kleinschreibung), bei gleichem Namen nach `Path`.
- Fortschritt: nach jedem Pack `new OperationProgress(done, total, name)`; `total` ist die Zahl aller gefundenen Packs. Das Token wird vor jedem Pack geprüft.
- `Backlog.txt`, Abschnitt „Aus dem Fundament zurückgestellt“: zwei Zeilen ergänzen: „LZ4: Checksummen des Frames prüfen (bisher nur Größenprüfung)“ und „Feldtyp OptionalI32 und weitere unbestätigte Typen (nicht messbar, bisher Status nicht unterstützt)“.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class PackCatalogTests
{
    private static GameProfile Profile(string? manifest) => new("testgame", "Test Game", 42, "Test Game", "packs", "PFH5", "schema_test.ron", ["boot", "release"], manifest);

    private sealed class Layout : IDisposable
    {
        public TempDir Root { get; } = new();
        public string Install => Root.Path;
        public string Data => Path.Combine(Root.Path, "packs");
        public string Workshop => Path.Combine(Root.Path, "workshop");
        public Layout() { Directory.CreateDirectory(Data); Directory.CreateDirectory(Workshop); }
        public void DataPack(string name, uint type) => new PackBuilder(packType: type).WriteTo(Path.Combine(Data, name));
        public void WorkshopPack(string id, string name) { Directory.CreateDirectory(Path.Combine(Workshop, id)); new PackBuilder().WriteTo(Path.Combine(Workshop, id, name)); }
        public void Dispose() => Root.Dispose();
    }

    private static string Describe(PackCatalogResult r) => string.Join(" | ", r.Packs.Select(p => $"{p.Name}:{p.Origin}:{p.WorkshopId}"));

    [Fact] public void Without_manifest_the_pack_type_decides()
    {
        using var l = new Layout();
        l.DataPack("zz_vanilla.pack", 1); l.DataPack("boot.pack", 0); l.DataPack("my_mod.pack", 3); l.DataPack("movie.pack", 4);
        l.WorkshopPack("123", "b_ws.pack"); l.WorkshopPack("99", "A_ws.pack");
        var r = PackCatalog.Scan(Profile(null), l.Install, l.Workshop, null, CancellationToken.None);
        Assert.False(r.UsedManifest);
        Assert.Equal("boot.pack:Vanilla: | zz_vanilla.pack:Vanilla: | movie.pack:DataFolderMod: | my_mod.pack:DataFolderMod: | A_ws.pack:Workshop:99 | b_ws.pack:Workshop:123", Describe(r));
        Assert.All(r.Packs, p => { Assert.Equal(PackProblem.None, p.Problem); Assert.NotNull(p.Fingerprint); Assert.NotNull(p.Header); });
    }

    [Fact] public void With_manifest_only_listed_packs_are_vanilla()
    {
        using var l = new Layout();
        l.DataPack("data.pack", 1); l.DataPack("sneaky_mod.pack", 1); l.DataPack("Local_EN.pack", 3);
        File.WriteAllText(Path.Combine(l.Data, "manifest.txt"), "text/credits.xml\t100\t1\ndata.pack\t5\t1\nlocal_en.pack\t7\t1\nmissing_dlc.pack\t9\t1\n");
        var r = PackCatalog.Scan(Profile("manifest.txt"), l.Install, null, null, CancellationToken.None);
        Assert.True(r.UsedManifest);
        Assert.Equal("data.pack:Vanilla: | Local_EN.pack:Vanilla: | sneaky_mod.pack:DataFolderMod:", Describe(r));
    }

    [Fact] public void Named_but_missing_manifest_falls_back_to_the_pack_type()
    {
        using var l = new Layout();
        l.DataPack("data.pack", 1);
        var r = PackCatalog.Scan(Profile("manifest.txt"), l.Install, null, null, CancellationToken.None);
        Assert.Equal((false, "data.pack:Vanilla:"), (r.UsedManifest, Describe(r)));
    }

    [Fact] public void Broken_pack_stays_in_the_list_with_its_problem()
    {
        using var l = new Layout();
        File.WriteAllBytes(Path.Combine(l.Data, "broken.pack"), [1, 2, 3]);
        l.DataPack("good.pack", 1);
        var r = PackCatalog.Scan(Profile(null), l.Install, null, null, CancellationToken.None);
        var broken = Assert.Single(r.Packs, p => p.Name == "broken.pack");
        Assert.Equal((PackOrigin.DataFolderMod, PackProblem.Truncated, (PackHeader?)null), (broken.Origin, broken.Problem, broken.Header));
        Assert.Equal(PackOrigin.Vanilla, Assert.Single(r.Packs, p => p.Name == "good.pack").Origin);
    }

    [Fact] public void Ignores_subfolders_of_data_and_deeper_workshop_levels()
    {
        using var l = new Layout();
        Directory.CreateDirectory(Path.Combine(l.Data, "sub")); new PackBuilder().WriteTo(Path.Combine(l.Data, "sub", "nested.pack"));
        Directory.CreateDirectory(Path.Combine(l.Workshop, "5", "deep")); new PackBuilder().WriteTo(Path.Combine(l.Workshop, "5", "deep", "x.pack"));
        new PackBuilder().WriteTo(Path.Combine(l.Workshop, "loose.pack"));
        File.WriteAllText(Path.Combine(l.Data, "readme.txt"), "x");
        Assert.Empty(PackCatalog.Scan(Profile(null), l.Install, l.Workshop, null, CancellationToken.None).Packs);
    }

    [Fact] public void Missing_folders_give_an_empty_catalog()
    {
        using var root = new TempDir();
        Assert.Empty(PackCatalog.Scan(Profile(null), root.Path, Path.Combine(root.Path, "none"), null, CancellationToken.None).Packs);
    }

    private sealed class Collect : IProgress<OperationProgress> { public List<OperationProgress> Items = []; public void Report(OperationProgress value) => Items.Add(value); }

    [Fact] public void Reports_progress_per_pack()
    {
        using var l = new Layout();
        l.DataPack("a.pack", 1); l.DataPack("b.pack", 3); l.WorkshopPack("7", "c.pack");
        var progress = new Collect();
        PackCatalog.Scan(Profile(null), l.Install, l.Workshop, progress, CancellationToken.None);
        Assert.Equal([1L, 2L, 3L], progress.Items.Select(p => p.Done));
        Assert.All(progress.Items, p => { Assert.Equal(3L, p.Total); Assert.EndsWith(".pack", p.Item); });
    }

    [Fact] public void Cancelled_token_throws()
    {
        using var l = new Layout();
        l.DataPack("a.pack", 1);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PackCatalog.Scan(Profile(null), l.Install, null, null, cts.Token));
    }
}

public class PackCatalogRealDataTests
{
    [RealDataFact] public void Catalog_of_the_real_install_is_plausible()
    {
        var profile = GameProfileLoader.LoadAll(Path.Combine(RepoRoot.Find(), "profiles")).Profiles.Single(p => p.Id == "warhammer_3");
        var r = PackCatalog.Scan(profile, RealData.Wh3Dir!, RealData.Wh3Workshop, null, CancellationToken.None);
        Assert.True(r.UsedManifest);
        var vanilla = r.Packs.Where(p => p.Origin == PackOrigin.Vanilla).ToList();
        Assert.True(vanilla.Count > 100);
        Assert.Contains(vanilla, p => p.Name.Equals("db.pack", StringComparison.OrdinalIgnoreCase));
        Assert.All(vanilla, p => Assert.True(p.Problem == PackProblem.None, $"{p.Name}: {p.Problem}"));
        Assert.All(vanilla, p => Assert.Contains(p.Header!.Type, new[] { PackType.Boot, PackType.Release, PackType.Patch }));
        Assert.All(r.Packs.Where(p => p.Origin == PackOrigin.Workshop), p => Assert.Matches("^[0-9]+$", p.WorkshopId));
        Assert.All(r.Packs.Where(p => p.Origin != PackOrigin.Workshop), p => Assert.Null(p.WorkshopId));
    }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.Workspace.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren**
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet build TotalWarModTool.slnx -c Release` (0 Warnungen), dann `dotnet test TotalWarModTool.slnx` zweimal mit beiden Variablen und einmal ohne. Expected: mit Variablen laufen alle Tests auf echten Daten; ohne Variablen sind genau diese übersprungen und der Rest ist grün. Die Zahlen (bestanden, übersprungen) stehen im Bericht.
- [ ] **Step 5: Commit** – `git commit -m "feat: add pack catalog with vanilla, data-folder and workshop origins"`
