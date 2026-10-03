# Teil 2 Plan 2: `Projects` – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eine UI-freie Library `Projects`, die Mod-Projekte anlegt und lädt, die Projektliste eines Spiels führt, ein Pack verlustfrei in einen Projektordner importiert, aus dem Projektordner ein Pack baut und es kontrolliert in den `data`-Ordner installiert.

**Architecture:** `Projects` verbindet die Libraries aus Plan 1 (`TableTsv`, Encoder, `PackWriter`) mit dem Dateisystem. Reine Regeln (Pfadabbildung, Umwandlung eines Eintrags) sind von den schreibenden Abläufen getrennt. Jeder Ablauf meldet Probleme als Ergebnis und hinterlässt bei Fehler oder Abbruch keinen Teilzustand: Der Import schreibt in einen Staging-Ordner und benennt um, der Bau schreibt eine temp-Datei und benennt um. Die App bleibt in diesem Plan unverändert; Plan 3 verdrahtet.

**Tech Stack:** C# 12, .NET 8, xUnit 2.9.2. Kein neues NuGet-Paket.

**Spec:** `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md`. Schreibgrenze in 4, Projekt auf der Platte in 5.1, Projektliste in 5.2, Module und „Festlegungen aus Plan 1“ in 5.4 (Punkt 12 nennt Pflichten dieses Plans), Import in 5.5, Bauen und Installieren in 5.7, Fehlerfälle in 7. Projektregeln: `CLAUDE.md`.

**Branch:** `feature/teil-2-projects` (von `master`, Stand `faac752` oder neuer). Baseline vor Task 1: `dotnet test TotalWarModTool.slnx` mit beiden Umgebungsvariablen ergibt 809 bestandene Tests, 0 übersprungen, Build ohne Warnung.

**Echte Daten für Tests:** `TWMT_WH3_DIR` und `TWMT_RPFM_CLI`, nur für den Testlauf gesetzt. Wer eine Task umsetzt, lässt die Suite **mit** beiden Variablen laufen und nennt im Bericht, wie viele Tests auf echten Daten liefen.

## Global Constraints

- Code, Bezeichner, Code-Kommentare und Commit-Messages Englisch.
- Kein neues NuGet-Paket. `TreatWarningsAsErrors` ist an.
- Neues Projekt `src/TotalWarModTool.Projects` (`net8.0`), Namespace `TotalWarModTool.Projects`, Referenzen genau: `Storage`, `PackFiles`, `Schema`, `Tables`, `Operations`. Kein Verweis auf `Workspace`, `GameProfiles`, `Logging`, `Localization`, `Settings`, `Session` oder die App. Testprojekt `tests/TotalWarModTool.Projects.Tests`. Beide in `TotalWarModTool.slnx` eintragen.
- Bestehende öffentliche Signaturen bleiben. Einzige Erweiterung außerhalb von `Projects`: `AtomicFile.Replace` (Task 5).
- **Schreibgrenze.** Produktcode schreibt nur in Ordner, die der Aufrufer übergibt: den Projektordner, einen Staging-Ordner direkt neben dem Zielordner des Imports, den `data`-Ordner (nur die eine Pack-Datei und ihre temp-Datei) und die Datei `projects.json`. Die Quelle eines Imports wird nur gelesen. Kein Pfad aus einem Pack darf aus dem Zielordner herausführen.
- **Kein Teilzustand.** Scheitert ein Ablauf oder wird er abgebrochen, bleibt der vorige Stand: kein halbes Projekt, kein halbes Pack, keine halbe Datei in `data`. Staging-Ordner und temp-Dateien werden in `finally` entfernt.
- **Diagnosen statt Exceptions.** Jedes Problem mit Dateien, Packs oder Inhalten ist ein Ergebnis (Enum plus `Detail`, kurzer technischer Text auf Englisch, kein UI-Text). Nach außen dringen nur `OperationCanceledException` und Argumentfehler. Kein `catch (Exception)` ohne Typfilter.
- Die kaputte Projektdatei eines Nutzers wird nie überschrieben und nie nach `.bad` kopiert (Spec 7). Für `projects.json` gilt die übliche Regel (`.bad`, dann leere Liste).
- Dateien werden als strenges UTF-8 gelesen: ungültige Bytes sind ein Problem, kein Ersatzzeichen. TSV wird als UTF-8 ohne BOM geschrieben.
- Abbruch: Jede Schleife über Einträge oder Dateien prüft das `CancellationToken`. Fortschritt ist `IProgress<OperationProgress>` mit Anzahl, Gesamt und aktuellem Element.
- Zeit und GUIDs für Tabellenköpfe kommen als Parameter herein. Keine statischen Singletons, kein veränderlicher statischer Zustand. Invariante Kultur.
- Kein Spielwissen im Code: Der `data`-Ordner, die Vanilla-Packnamen und die Profil-ID kommen vom Aufrufer.
- Felder und Funktionen nur, soweit dieser Plan sie nutzt. Der Papierkorb und „Ins Projekt übernehmen“ kommen mit Plan 4.
- Test-first: failing Test → RED prüfen → implementieren → GREEN → Commit. Die ganze Suite bleibt grün. Keine Zeit-Assertions.
- Tests schreiben nur in `TempDir`. Der „`data`-Ordner“ der Tests ist ein Temp-Ordner. Nichts schreibt in den Spielordner, den Workshop-Ordner oder den RPFM-Ordner. Subagenten lesen und schreiben nichts unter `%APPDATA%\TotalWarModTool` und `%LocalAppData%\TotalWarModTool`.
- Ein Commit pro Task, nach GREEN. Eigene Pfade ausdrücklich stagen. Die Attribution-Zeile der laufenden Sitzung gehört ans Ende der Commit-Message.
- Die Tests in diesem Plan legen Namen und Erwartungen fest. Scheitert ein Test nur an einem Compiler-Detail, wird er minimal angepasst, ohne die Erwartung zu ändern. Widerspricht eine Erwartung einer Regel des Plans, wird das gemeldet, nicht still entschieden.

## Review Focus

Diese Fälle nennt die Spec nicht einzeln, sie treffen Nutzer aber zuerst. Jeder hat einen Test in der genannten Task.

1. **Feindliche oder kaputte Pfade in einem fremden Pack** (`..\..\x`, `C:\x`, `CON`, Punkt am Ende, Steuerzeichen, Datei und Ordner gleichen Namens). Erwartung: nichts landet außerhalb des Zielordners, der Eintrag wird ausgelassen und im Bericht genannt, der Import läuft weiter. → Task 3 (`UnsafePathReason_rejects`), Task 4 (`Unsafe_and_colliding_entries_are_skipped_and_reported`).
2. **Import bricht mittendrin ab** (Abbruch, Zielordner nicht beschreibbar, Quelle ändert sich). Erwartung: kein Projektordner, kein Staging-Rest, ein vorhandener leerer Zielordner bleibt leer. → Task 4 (`Cancel_leaves_no_trace`, `A_failed_import_leaves_no_trace`).
3. **Projektordner von Hand verändert** (Tabelle in falschem Ordner, TSV mit BOM und `\r\n`, ungültiges UTF-8, fremde Dateien, leere Ordner, zwei Dateien mit gleichem Pack-Pfad). Erwartung: Der Bau nennt jede Datei mit ihrem Problem und schreibt nichts; ein vorhandenes Pack bleibt unberührt. → Task 5 (`Every_problem_is_reported_and_nothing_is_written`).
4. **Projektdateien von außen** (kopierter Projektordner mit gleicher GUID, Projektdatei aus neuerer Version, von Hand kaputt, relative Pfade in der Liste). Erwartung: Diagnose, nie ein Überschreiben der Projektdatei, die Liste bleibt benutzbar. → Task 1 (`Load_reports_problems_and_never_touches_the_file`), Task 2 (`Add_rejects_duplicates_and_relative_paths`, `Load_repairs_the_list`).
5. **Fremde Datei gleichen Namens in `data`** oder das installierte Pack wurde seit der Installation verändert. Erwartung: Installieren und Deinstallieren lehnen ab, die Datei bleibt unberührt. → Task 6 (`A_foreign_or_changed_file_is_never_touched`).

---

### Task 1: Projektgerüst, `ModProject`, `ProjectFile`, `PackNameRules`

**Files:**
- Create: `src/TotalWarModTool.Projects/TotalWarModTool.Projects.csproj`, `ModProject.cs`, `ProjectFile.cs`, `PackNameRules.cs`
- Create: `tests/TotalWarModTool.Projects.Tests/TotalWarModTool.Projects.Tests.csproj`, `ProjectFileTests.cs`, `PackNameRulesTests.cs`
- Modify: `TotalWarModTool.slnx`

**Interfaces:**
- Consumes: `JsonFileStore.Save` (schreibt `schemaVersion` zuerst, atomar); Testhelfer `TempDir`.
- Produces:

```csharp
namespace TotalWarModTool.Projects;

public sealed record ModProject(Guid Id, string Name, string GameId, string PackName, IReadOnlyList<string> Dependencies);

public enum ProjectProblem { None, NotFound, Unreadable, Corrupt, NewerVersion, Invalid, FolderNotEmpty, InvalidPackName, Unwritable }

/// Project is null unless Problem is None. Detail is short technical English.
public sealed record ProjectResult(ModProject? Project, ProjectProblem Problem, string? Detail);

public static class ProjectFile
{
    public const int SchemaVersion = 1;
    public const string FileName = "twmt-project.json";
    public const string PackFolder = "pack";
    public const string BuildFolder = "build";

    public static ProjectResult Load(string projectFolder);
    /// Writes the project file atomically. Problems: InvalidPackName, Unwritable.
    public static ProjectResult Save(string projectFolder, ModProject project);
    /// Creates the folder (if missing), the pack folder and the project file with a new id.
    /// Problems: FolderNotEmpty, InvalidPackName, Invalid (empty name or game id), Unwritable.
    public static ProjectResult CreateNew(string projectFolder, string name, string gameId, string packName, IReadOnlyList<string> dependencies);
}

public static class PackNameRules
{
    /// Null when packName can be the file name of a mod pack; otherwise a short English reason.
    public static string? Check(string packName);
}
```

Festlegungen:
- Datei: JSON-Objekt mit `schemaVersion`, `id` (GUID im Format `D`), `name`, `game`, `packName`, `dependencies` (Liste von Texten).
- `Load` liest selbst (nicht `JsonFileStore.Load`, weil das nach `.bad` kopiert) und verändert nichts auf der Platte:
  - Ordner oder Datei fehlt → `NotFound`; nicht lesbar → `Unreadable`.
  - Kein JSON, kein Objekt, `schemaVersion` fehlt oder ist keine ganze Zahl → `Corrupt`.
  - `schemaVersion` größer als 1 → `NewerVersion` (wird nicht geladen, damit ein Speichern keine fremden Felder verliert).
  - `id` fehlt, ist keine GUID oder `Guid.Empty`; `name` oder `game` fehlt oder ist leer bzw. nur Leerraum; `packName` besteht `PackNameRules.Check` nicht; `dependencies` ist vorhanden, aber keine Liste von nicht leeren Texten → `Invalid`. `Detail` nennt das Feld.
  - `dependencies` fehlt → leere Liste.
- `PackNameRules.Check` lehnt ab: leer; länger als 100 Zeichen; endet nicht auf `.pack` (ohne Rücksicht auf Groß-/Kleinschreibung) oder besteht nur aus der Endung; enthält `\`, `/` oder ein Zeichen aus `Path.GetInvalidFileNameChars()`; beginnt oder endet mit Leerzeichen; ein einzelnes Surrogat-Zeichen; ein unter Windows reservierter Name vor dem ersten Punkt (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`, ohne Rücksicht auf Groß-/Kleinschreibung).
- `CreateNew`: Der Ordner darf fehlen oder leer sein. Die GUID kommt aus `Guid.NewGuid()`.
- `Save` und `CreateNew` übersetzen `IOException` und `UnauthorizedAccessException` in `Unwritable`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class PackNameRulesTests
{
    [Theory]
    [InlineData("my_mod.pack")] [InlineData("!!My Mod (v2).PACK")] [InlineData("ünïcode_日本.pack")]
    public void Accepts_a_usable_name(string name) => Assert.Null(PackNameRules.Check(name));

    [Theory]
    [InlineData("")] [InlineData(".pack")] [InlineData("mod")] [InlineData("mod.pack.txt")] [InlineData("a/b.pack")] [InlineData(@"a\b.pack")]
    [InlineData("a:b.pack")] [InlineData("a*b.pack")] [InlineData(" mod.pack")] [InlineData("con.pack")] [InlineData("COM1.x.pack")] [InlineData("a\tb.pack")]
    public void Rejects_an_unusable_name(string name) => Assert.False(string.IsNullOrWhiteSpace(PackNameRules.Check(name)));

    [Fact] public void Rejects_a_too_long_name_and_a_lone_surrogate()
    {
        Assert.Null(PackNameRules.Check(new string('a', 95) + ".pack"));
        Assert.NotNull(PackNameRules.Check(new string('a', 96) + ".pack"));
        Assert.NotNull(PackNameRules.Check("a" + (char)0xD800 + ".pack"));
    }
}

public class ProjectFileTests
{
    [Fact] public void CreateNew_then_Load_gives_the_same_project()
    {
        using var dir = new TempDir();
        var folder = Path.Combine(dir.Path, "my mod");
        var created = ProjectFile.CreateNew(folder, "My Mod", "warhammer_3", "my_mod.pack", ["other.pack"]);
        Assert.Equal(ProjectProblem.None, created.Problem);
        Assert.NotEqual(Guid.Empty, created.Project!.Id);
        Assert.True(Directory.Exists(Path.Combine(folder, "pack")));
        var loaded = ProjectFile.Load(folder);
        Assert.Equal(ProjectProblem.None, loaded.Problem);
        Assert.Equal((created.Project.Id, "My Mod", "warhammer_3", "my_mod.pack"), (loaded.Project!.Id, loaded.Project.Name, loaded.Project.GameId, loaded.Project.PackName));
        Assert.Equal(["other.pack"], loaded.Project.Dependencies);
        var text = File.ReadAllText(Path.Combine(folder, "twmt-project.json"));
        Assert.Contains("\"schemaVersion\": 1", text);
        Assert.Contains("\"game\": \"warhammer_3\"", text);
    }

    [Fact] public void CreateNew_accepts_an_existing_empty_folder_and_rejects_the_rest()
    {
        using var dir = new TempDir();
        Assert.Equal(ProjectProblem.None, ProjectFile.CreateNew(dir.Path, "A", "g", "a.pack", []).Problem);
        Assert.Equal(ProjectProblem.FolderNotEmpty, ProjectFile.CreateNew(dir.Path, "A", "g", "a.pack", []).Problem);
        using var other = new TempDir();
        Assert.Equal(ProjectProblem.InvalidPackName, ProjectFile.CreateNew(other.Path, "A", "g", "a.txt", []).Problem);
        Assert.Equal(ProjectProblem.Invalid, ProjectFile.CreateNew(other.Path, " ", "g", "a.pack", []).Problem);
        Assert.Empty(Directory.EnumerateFileSystemEntries(other.Path));
    }

    [Theory]
    [InlineData(null, ProjectProblem.NotFound)]
    [InlineData("{ broken", ProjectProblem.Corrupt)]
    [InlineData("[1]", ProjectProblem.Corrupt)]
    [InlineData("""{"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","game":"g","packName":"a.pack"}""", ProjectProblem.Corrupt)]
    [InlineData("""{"schemaVersion":2,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","game":"g","packName":"a.pack"}""", ProjectProblem.NewerVersion)]
    [InlineData("""{"schemaVersion":1,"id":"nope","name":"A","game":"g","packName":"a.pack"}""", ProjectProblem.Invalid)]
    [InlineData("""{"schemaVersion":1,"id":"00000000-0000-0000-0000-000000000000","name":"A","game":"g","packName":"a.pack"}""", ProjectProblem.Invalid)]
    [InlineData("""{"schemaVersion":1,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"","game":"g","packName":"a.pack"}""", ProjectProblem.Invalid)]
    [InlineData("""{"schemaVersion":1,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","packName":"a.pack"}""", ProjectProblem.Invalid)]
    [InlineData("""{"schemaVersion":1,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","game":"g","packName":"a.txt"}""", ProjectProblem.Invalid)]
    [InlineData("""{"schemaVersion":1,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","game":"g","packName":"a.pack","dependencies":["ok.pack",5]}""", ProjectProblem.Invalid)]
    public void Load_reports_problems_and_never_touches_the_file(string? content, ProjectProblem expected)
    {
        using var dir = new TempDir();
        if (content is not null) File.WriteAllText(dir.File("twmt-project.json"), content);
        var result = ProjectFile.Load(dir.Path);
        Assert.Equal(expected, result.Problem);
        Assert.Null(result.Project);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        if (content is not null) Assert.Equal(content, File.ReadAllText(dir.File("twmt-project.json")));
        Assert.False(File.Exists(dir.File("twmt-project.json.bad")));
    }

    [Fact] public void Load_tolerates_missing_dependencies_and_Save_rejects_a_bad_pack_name()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("twmt-project.json"), """{"schemaVersion":1,"id":"46e87609-af6e-4857-bea4-f29b4fbd55fd","name":"A","game":"g","packName":"a.pack"}""");
        var project = ProjectFile.Load(dir.Path).Project!;
        Assert.Empty(project.Dependencies);
        Assert.Equal(ProjectProblem.InvalidPackName, ProjectFile.Save(dir.Path, project with { PackName = "x" }).Problem);
        Assert.Equal(ProjectProblem.None, ProjectFile.Save(dir.Path, project with { Name = "B" }).Problem);
        Assert.Equal("B", ProjectFile.Load(dir.Path).Project!.Name);
    }
}
```

- [ ] **Step 2: RED prüfen**

Run: `dotnet test tests/TotalWarModTool.Projects.Tests`
Expected: Build schlägt fehl (Typen fehlen).

- [ ] **Step 3: Projekte anlegen, in `TotalWarModTool.slnx` eintragen, `ModProject`, `ProjectFile`, `PackNameRules` implementieren**

Das `.csproj` der Library bekommt schon alle fünf Referenzen aus den Global Constraints und `InternalsVisibleTo` für das Testprojekt. Das Testprojekt referenziert `Projects`, `PackFiles`, `Tables`, `Schema`, `Storage`, `Operations`.

- [ ] **Step 4: GREEN prüfen**

Run: `dotnet test TotalWarModTool.slnx` (mit beiden Umgebungsvariablen)
Expected: alle Tests bestanden, 0 übersprungen, 0 Warnungen.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: add the projects library with the project file"
```

---

### Task 2: Projektliste

**Files:**
- Create: `src/TotalWarModTool.Projects/ProjectList.cs`, `ProjectListStore.cs`
- Test: `tests/TotalWarModTool.Projects.Tests/ProjectListTests.cs`, `ProjectListStoreTests.cs`

**Interfaces:**
- Consumes: `JsonFileStore.Load/Save`, `LoadStatus` (aus `Storage`); Muster: `src/TotalWarModTool.Session/SessionStore.cs`.
- Produces:

```csharp
/// What the tool installed into the data folder for a project: the file name and the state of the file right after the install.
public sealed record InstalledPack(string FileName, long Length, DateTime LastWriteTimeUtc);

public sealed record ProjectListEntry(Guid Id, string Path, InstalledPack? Installed);

/// ActiveId is null only when the list is empty.
public sealed record ProjectList(IReadOnlyList<ProjectListEntry> Projects, Guid? ActiveId)
{
    public static ProjectList Empty { get; } = new([], null);
}

public enum ProjectListProblem { None, DuplicateId, DuplicatePath, InvalidPath, UnknownId }
public sealed record ProjectListEdit(ProjectList List, ProjectListProblem Problem);

/// Pure edits. On a problem the returned list is the unchanged input.
public static class ProjectListEdits
{
    public static ProjectListEdit Add(ProjectList list, Guid id, string path);
    public static ProjectListEdit Remove(ProjectList list, Guid id);
    public static ProjectListEdit SetActive(ProjectList list, Guid id);
    public static ProjectListEdit SetInstalled(ProjectList list, Guid id, InstalledPack? installed);
}

public sealed record ProjectListLoadResult(ProjectList List, LoadStatus Status, string? BadCopyPath);

public sealed class ProjectListStore
{
    public const int SchemaVersion = 1;
    public const string FileName = "projects.json";
    public ProjectListStore(string roamingRoot, string profileId);   // file: <roamingRoot>\games\<profileId>\projects.json
    public string FilePath { get; }
    public ProjectListLoadResult Load();
    public void Save(ProjectList list);
}
```

Festlegungen:
- `Add`: `id` gleich `Guid.Empty` → `ArgumentException`. Pfad nicht absolut (`Path.IsPathFullyQualified`) → `InvalidPath`. Der Pfad wird mit `Path.GetFullPath` normalisiert und ohne schließenden Trenner gespeichert. Gleiche GUID → `DuplicateId`. Gleicher Pfad ohne Rücksicht auf Groß-/Kleinschreibung → `DuplicatePath`. Das erste Projekt einer leeren Liste wird aktiv.
- `Remove`: unbekannte GUID → `UnknownId`. War das Projekt aktiv, wird das erste verbleibende aktiv, sonst `null`.
- `SetActive`, `SetInstalled`: unbekannte GUID → `UnknownId`.
- `Load` repariert still (wie `SessionStore`): Einträge ohne gültige GUID oder ohne absoluten Pfad entfallen; bei doppelter GUID oder doppeltem Pfad gewinnt der erste; ein `installed` ohne Dateinamen oder mit negativer Länge wird `null`; zeigt `activeId` auf kein Projekt, wird das erste aktiv (bei leerer Liste `null`).
- `LastWriteTimeUtc` übersteht Speichern und Laden auf den Tick genau und hat `DateTimeKind.Utc`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class ProjectListTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact] public void Add_makes_the_first_project_active_and_normalises_the_path()
    {
        var one = ProjectListEdits.Add(ProjectList.Empty, A, @"C:\mods\a\");
        Assert.Equal(ProjectListProblem.None, one.Problem);
        Assert.Equal((A, @"C:\mods\a"), (one.List.ActiveId!.Value, one.List.Projects[0].Path));
        var two = ProjectListEdits.Add(one.List, B, @"C:\mods\x\..\b");
        Assert.Equal((A, @"C:\mods\b", 2), (two.List.ActiveId!.Value, two.List.Projects[1].Path, two.List.Projects.Count));
    }

    [Fact] public void Add_rejects_duplicates_and_relative_paths()
    {
        var list = ProjectListEdits.Add(ProjectList.Empty, A, @"C:\mods\a").List;
        Assert.Equal(ProjectListProblem.DuplicateId, ProjectListEdits.Add(list, A, @"C:\mods\copy").Problem);
        Assert.Equal(ProjectListProblem.DuplicatePath, ProjectListEdits.Add(list, B, @"c:\MODS\A\").Problem);
        Assert.Equal(ProjectListProblem.InvalidPath, ProjectListEdits.Add(list, B, @"mods\b").Problem);
        Assert.Same(list, ProjectListEdits.Add(list, B, "").List);
        Assert.Throws<ArgumentException>(() => ProjectListEdits.Add(list, Guid.Empty, @"C:\mods\b"));
    }

    [Fact] public void Remove_moves_the_active_mark_and_edits_reject_unknown_ids()
    {
        var list = ProjectListEdits.Add(ProjectListEdits.Add(ProjectList.Empty, A, @"C:\a").List, B, @"C:\b").List;
        Assert.Equal(B, ProjectListEdits.SetActive(list, B).List.ActiveId);
        Assert.Equal(B, ProjectListEdits.Remove(list, A).List.ActiveId);
        Assert.Null(ProjectListEdits.Remove(ProjectListEdits.Remove(list, A).List, B).List.ActiveId);
        var installed = new InstalledPack("a.pack", 5, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        Assert.Equal(installed, ProjectListEdits.SetInstalled(list, A, installed).List.Projects[0].Installed);
        Assert.Null(ProjectListEdits.SetInstalled(ProjectListEdits.SetInstalled(list, A, installed).List, A, null).List.Projects[0].Installed);
        var unknown = Guid.NewGuid();
        Assert.All(new[] { ProjectListEdits.Remove(list, unknown), ProjectListEdits.SetActive(list, unknown), ProjectListEdits.SetInstalled(list, unknown, null) },
            e => Assert.Equal((ProjectListProblem.UnknownId, list), (e.Problem, e.List)));
    }
}

public class ProjectListStoreTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact] public void Save_then_Load_keeps_everything_to_the_tick()
    {
        using var dir = new TempDir();
        var store = new ProjectListStore(dir.Path, "warhammer_3");
        Assert.Equal(Path.Combine(dir.Path, "games", "warhammer_3", "projects.json"), store.FilePath);
        var time = new DateTime(638_000_000_000_000_001, DateTimeKind.Utc);
        var list = new ProjectList([new(A, @"C:\a", new InstalledPack("a.pack", 12345, time)), new(B, @"D:\b", null)], B);
        store.Save(list);
        var loaded = store.Load();
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal(list.Projects, loaded.List.Projects);
        Assert.Equal((B, DateTimeKind.Utc), (loaded.List.ActiveId!.Value, loaded.List.Projects[0].Installed!.LastWriteTimeUtc.Kind));
    }

    [Fact] public void A_missing_file_is_an_empty_list_and_a_broken_file_goes_to_bad()
    {
        using var dir = new TempDir();
        var store = new ProjectListStore(dir.Path, "g");
        Assert.Equal((LoadStatus.Missing, 0), (store.Load().Status, store.Load().List.Projects.Count));
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "{ broken");
        var loaded = store.Load();
        Assert.Equal((LoadStatus.Corrupt, 0), (loaded.Status, loaded.List.Projects.Count));
        Assert.True(File.Exists(store.FilePath + ".bad"));
    }

    [Fact] public void Load_repairs_the_list()
    {
        using var dir = new TempDir();
        var store = new ProjectListStore(dir.Path, "g");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """
            {"schemaVersion":1,"activeId":"99999999-9999-9999-9999-999999999999","projects":[
              null,
              {"id":"nope","path":"C:\\x"},
              {"id":"11111111-1111-1111-1111-111111111111","path":"relative\\a"},
              {"id":"11111111-1111-1111-1111-111111111111","path":"C:\\a","installed":{"fileName":"","length":1,"lastWriteTimeUtc":"2026-01-02T03:04:05Z"}},
              {"id":"11111111-1111-1111-1111-111111111111","path":"C:\\again"},
              {"id":"22222222-2222-2222-2222-222222222222","path":"c:\\A"},
              {"id":"22222222-2222-2222-2222-222222222222","path":"C:\\b","installed":{"fileName":"b.pack","length":-1,"lastWriteTimeUtc":"2026-01-02T03:04:05Z"}}
            ]}
            """);
        var list = store.Load().List;
        Assert.Equal([(A, @"C:\a"), (B, @"C:\b")], list.Projects.Select(p => (p.Id, p.Path)));
        Assert.All(list.Projects, p => Assert.Null(p.Installed));
        Assert.Equal(A, list.ActiveId);
    }
}
```

- [ ] **Step 2: RED prüfen** — `dotnet test tests/TotalWarModTool.Projects.Tests`, Build schlägt fehl.

- [ ] **Step 3: `ProjectList.cs` (Typen, `ProjectListEdits`) und `ProjectListStore` implementieren**

Das Dateiformat ist wie in `SessionStore` eine eigene private Klasse mit lauter nullbaren Mitgliedern.

- [ ] **Step 4: GREEN prüfen** — ganze Suite mit beiden Variablen, 0 übersprungen, 0 Warnungen.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: add the project list of a game"
```

---

### Task 3: `ProjectLayout` und `EntryConversion`

**Files:**
- Create: `src/TotalWarModTool.Projects/ProjectLayout.cs`, `EntryConversion.cs`
- Test: `tests/TotalWarModTool.Projects.Tests/ProjectLayoutTests.cs`, `EntryConversionTests.cs`, `TestData.cs`

**Interfaces:**
- Consumes: `DbTablePath.TryGetTableName`, `DbTableDecoder.Decode/ReadGuid`, `DbTableEncoder.Encode`, `LocDecoder.Decode`, `LocEncoder.Encode`, `TableTsv.Write/Read/WriteLoc/ReadLoc`, `GameSchema`.
- Produces:

```csharp
public enum ProjectFileKind { Table, Loc, Raw }

/// Pure rules that map a pack path (with '/') to a file below the pack folder of a project and back.
public static class ProjectLayout
{
    /// Null when the pack path can be a file below a project folder on Windows; otherwise a short English reason.
    public static string? UnsafePathReason(string packPath);
    /// The relative file (with '\') for an entry stored as kind: Table and Loc get ".tsv" appended, Raw keeps the path.
    public static string ToRelativeFile(string packPath, ProjectFileKind kind);
    /// What a file below the pack folder is, and the pack path it becomes. relativeFile uses '\' or '/'.
    public static (string PackPath, ProjectFileKind Kind) FromRelativeFile(string relativeFile);
    /// True when an entry with this pack path would be taken for a table or loc file in text form.
    public static bool LooksLikeTextForm(string packPath);
}

/// Bytes is the content of the file in the project. Note says why a table or loc file stayed binary (null otherwise).
public sealed record ProjectFileContent(ProjectFileKind Kind, byte[] Bytes, string? Note);

/// Bytes is the pack entry, or null with a problem. Line is 1-based, 0 when the problem has no line.
public sealed record PackEntryContent(byte[]? Bytes, int Line, string? Detail);

public static class EntryConversion
{
    /// Pack entry -> project file. Never throws for the data. packPath must have passed UnsafePathReason.
    public static ProjectFileContent ToProjectFile(string packPath, byte[] entryBytes, GameSchema? schema);
    /// Project file in text form -> pack entry. kind is Table or Loc. newGuid gives the GUID text of a table header.
    public static PackEntryContent ToPackEntry(ProjectFileKind kind, string packPath, byte[] fileBytes, GameSchema? schema, Func<string> newGuid);
}
```

Festlegungen:
- `FromRelativeFile` (Trenner zuerst auf `/`): genau drei Segmente, das erste `db` (ohne Rücksicht auf Groß-/Kleinschreibung), Endung `.tsv` (ohne Rücksicht auf Groß-/Kleinschreibung), Name vor der Endung nicht leer → `Table`, Pack-Pfad ohne `.tsv`. Sonst Endung `.loc.tsv` mit nicht leerem Namen davor → `Loc`, Pack-Pfad ohne `.tsv`. Sonst `Raw`, Pack-Pfad unverändert.
- `LooksLikeTextForm(p)` ist `FromRelativeFile(p).Kind != Raw`.
- `UnsafePathReason` lehnt ab: leer; ein Segment leer, `.` oder `..`; `\` im Pfad (Pack-Pfade tragen hier `/`); ein Zeichen aus `< > : " | ? *` oder unter U+0020; ein einzelnes Surrogat-Zeichen; ein Segment, das auf Leerzeichen oder Punkt endet; ein Segment, dessen Teil vor dem ersten Punkt reserviert ist (Liste wie in Task 1); ein Segment über 255 Zeichen.
- `ToProjectFile`:
  - `DbTablePath.TryGetTableName` trifft: Ohne Schema → `Raw`, Note `no schema`. Tabellenname mit `;` → `Raw` mit Note. Dekodieren; Status nicht `Ok` → `Raw`, Note mit Status und Detail. `TableTsv.Write`; `Text` ist `null` → `Raw`, Note ist dessen `Detail`. Gegenprobe: `TableTsv.Read(text)` und `DbTableEncoder.Encode(…, DbTableDecoder.ReadGuid(entryBytes), …)` müssen byte-gleich `entryBytes` ergeben; sonst `Raw`, Note `round trip differs`. Bestanden → `Table`, Bytes sind der Text als UTF-8 ohne BOM.
  - Pfad endet auf `.loc` (ohne Rücksicht auf Groß-/Kleinschreibung): `LocDecoder.Decode`, `TableTsv.WriteLoc`, Gegenprobe mit `ReadLoc` und `LocEncoder.Encode`; gleiche Rückfälle auf `Raw` mit Note.
  - Alles andere → `Raw`, Note `null`.
  - Eine `ArgumentException` der Encoder in der Gegenprobe zählt als nicht bestanden.
- `ToPackEntry`:
  - Bytes streng als UTF-8 dekodieren (ein BOM am Anfang ist erlaubt); ungültig → Problem `the file is not valid UTF-8`, Zeile 0.
  - `Table`: ohne Schema → Problem `no schema`. `TableTsv.Read`; Problem → dessen Zeile und Detail. Der Tabellenname der Metazeile muss dem Tabellennamen aus `packPath` gleichen (ohne Rücksicht auf Groß-/Kleinschreibung), sonst Problem in Zeile 2, das beide Namen nennt. Der Pfad der Metazeile wird nicht geprüft: Die Lage der Datei bestimmt den Pack-Pfad. Kodieren mit `newGuid()`.
  - `Loc`: `TableTsv.ReadLoc`, dann `LocEncoder.Encode`.

- [ ] **Step 1: Failing Tests schreiben**

`TestData.cs` (Helfer für diese und die folgenden Tasks):

```csharp
internal static class TestData
{
    public const string HeaderGuid = "46e87609-af6e-4857-bea4-f29b4fbd55fd";
    public static readonly FieldDefinition[] UnitFields =
    [
        new("key", FieldType.StringU8, "StringU8", true, null), new("cost", FieldType.I32, "I32", false, null), new("speed", FieldType.F32, "F32", false, null),
    ];
    public static readonly GameSchema Schema = new(new Dictionary<string, IReadOnlyList<TableDefinition>>
    {
        ["units_tables"] = [new TableDefinition(2, UnitFields)],
        ["colours_tables"] = [new TableDefinition(1, [new("r", FieldType.I32, "I32", false, null), new("g", FieldType.I32, "I32", false, null), new("b", FieldType.I32, "I32", false, null)])],
    });

    public static byte[] Units(params (string Key, int Cost, float Speed)[] rows) =>
        DbTableEncoder.Encode(Schema.Find("units_tables", 2)!, 2, HeaderGuid, rows.Select(r => new object?[] { r.Key, r.Cost, r.Speed }).ToList());

    public static byte[] Loc(params (string Key, string Text)[] rows) => LocEncoder.Encode(rows.Select(r => new LocRow(r.Key, r.Text, false)).ToList());

    public static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);
}
```

```csharp
public class ProjectLayoutTests
{
    [Theory]
    [InlineData("db/units_tables/my_mod", ProjectFileKind.Table, @"db\units_tables\my_mod.tsv")]
    [InlineData("text/db/my_mod.loc", ProjectFileKind.Loc, @"text\db\my_mod.loc.tsv")]
    [InlineData("script/a.lua", ProjectFileKind.Raw, @"script\a.lua")]
    [InlineData("db/units_tables/binary", ProjectFileKind.Raw, @"db\units_tables\binary")]
    public void ToRelativeFile_and_back(string packPath, ProjectFileKind kind, string file)
    {
        Assert.Equal(file, ProjectLayout.ToRelativeFile(packPath, kind));
        Assert.Equal((packPath, kind), ProjectLayout.FromRelativeFile(file));
    }

    [Theory]
    [InlineData("DB/Units_Tables/X.TSV", "DB/Units_Tables/X", ProjectFileKind.Table)]
    [InlineData("db/units_tables/sub/x.tsv", "db/units_tables/sub/x.tsv", ProjectFileKind.Raw)]
    [InlineData("db/x.tsv", "db/x.tsv", ProjectFileKind.Raw)]
    [InlineData("db/units_tables/.tsv", "db/units_tables/.tsv", ProjectFileKind.Raw)]
    [InlineData("notes/table.tsv", "notes/table.tsv", ProjectFileKind.Raw)]
    [InlineData("text/X.LOC.TSV", "text/X.LOC", ProjectFileKind.Loc)]
    [InlineData("text/.loc.tsv", "text/.loc.tsv", ProjectFileKind.Raw)]
    public void FromRelativeFile_recognises_text_forms_only_where_the_rule_says(string file, string packPath, ProjectFileKind kind) =>
        Assert.Equal((packPath, kind), ProjectLayout.FromRelativeFile(file));

    [Fact] public void LooksLikeTextForm_follows_FromRelativeFile()
    {
        Assert.True(ProjectLayout.LooksLikeTextForm("db/units_tables/x.tsv"));
        Assert.True(ProjectLayout.LooksLikeTextForm("text/a.loc.tsv"));
        Assert.False(ProjectLayout.LooksLikeTextForm("db/units_tables/x"));
    }

    [Theory]
    [InlineData("")] [InlineData("../x")] [InlineData("a/../../x")] [InlineData("a//b")] [InlineData("/a")] [InlineData("a/")] [InlineData("./a")]
    [InlineData(@"a\b")] [InlineData("C:/x")] [InlineData("a/b:c")] [InlineData("a/b?")] [InlineData("a/b\tc")] [InlineData("a/con")] [InlineData("a/NUL.txt")]
    [InlineData("a/lpt9.x.y")] [InlineData("a/b.")] [InlineData("a/b ")] [InlineData("a /b")]
    public void UnsafePathReason_rejects(string packPath) => Assert.False(string.IsNullOrWhiteSpace(ProjectLayout.UnsafePathReason(packPath)));

    [Fact] public void UnsafePathReason_accepts_ordinary_paths_and_limits_segment_length()
    {
        Assert.Null(ProjectLayout.UnsafePathReason("db/units_tables/!my mod_ü日本"));
        Assert.Null(ProjectLayout.UnsafePathReason("a/console.txt"));
        Assert.Null(ProjectLayout.UnsafePathReason("a/" + new string('x', 255)));
        Assert.NotNull(ProjectLayout.UnsafePathReason("a/" + new string('x', 256)));
        Assert.NotNull(ProjectLayout.UnsafePathReason("a/" + (char)0xD800));
    }
}

public class EntryConversionTests
{
    private static string NewGuid() => "00000000-0000-0000-0000-000000000001";

    [Fact] public void A_table_becomes_tsv_and_builds_back_to_the_same_content()
    {
        var bytes = TestData.Units(("unit_a", 100, 0.1f), ("ünit_b", -5, 2.0000002E-06f));
        var file = EntryConversion.ToProjectFile("db/units_tables/my_mod", bytes, TestData.Schema);
        Assert.Equal((ProjectFileKind.Table, null), (file.Kind, file.Note));
        Assert.Equal("key\tcost\tspeed\n#units_tables;2;db/units_tables/my_mod\t\t\nunit_a\t100\t0.1\nünit_b\t-5\t2.0000002E-06\n", Encoding.UTF8.GetString(file.Bytes));
        var entry = EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/units_tables/my_mod", file.Bytes, TestData.Schema, () => TestData.HeaderGuid);
        Assert.Equal(bytes, entry.Bytes);
        Assert.Equal(NewGuid(), DbTableDecoder.ReadGuid(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/units_tables/my_mod", file.Bytes, TestData.Schema, NewGuid).Bytes!));
    }

    [Fact] public void A_loc_file_becomes_tsv_and_builds_back_to_the_same_bytes()
    {
        var bytes = TestData.Loc(("k1", "Text\twith tab"), ("k2", ""));
        var file = EntryConversion.ToProjectFile("text/db/my_mod.loc", bytes, null);
        Assert.Equal(ProjectFileKind.Loc, file.Kind);
        Assert.Equal(bytes, EntryConversion.ToPackEntry(ProjectFileKind.Loc, "text/db/my_mod.loc", file.Bytes, null, NewGuid).Bytes);
    }

    [Fact] public void Tables_that_cannot_be_text_stay_binary_with_a_note()
    {
        var units = TestData.Units(("a", 1, 1f));
        Assert.Equal((ProjectFileKind.Raw, "no schema"), Kind(EntryConversion.ToProjectFile("db/units_tables/x", units, null)));
        var unknown = EntryConversion.ToProjectFile("db/other_tables/x", units, TestData.Schema);
        Assert.Equal(ProjectFileKind.Raw, unknown.Kind);
        Assert.Contains("TableNotInSchema", unknown.Note);
        var broken = EntryConversion.ToProjectFile("db/units_tables/x", units[..^1], TestData.Schema);
        Assert.Equal((ProjectFileKind.Raw, units.Length - 1), (broken.Kind, broken.Bytes.Length));
        Assert.False(string.IsNullOrWhiteSpace(broken.Note));
        var colours = DbTableEncoder.Encode(TestData.Schema.Find("colours_tables", 1)!, 1, TestData.HeaderGuid, [[257, 0, 0]]);
        var refused = EntryConversion.ToProjectFile("db/colours_tables/x", colours, TestData.Schema);
        Assert.Equal((ProjectFileKind.Raw, colours), (refused.Kind, refused.Bytes));
        Assert.False(string.IsNullOrWhiteSpace(refused.Note));
        Assert.Equal(ProjectFileKind.Raw, EntryConversion.ToProjectFile("db/units;tables/x", units, TestData.Schema).Kind);

        static (ProjectFileKind, string?) Kind(ProjectFileContent c) => (c.Kind, c.Note);
    }

    [Fact] public void Other_entries_and_broken_loc_files_stay_as_they_are()
    {
        var lua = TestData.Utf8("print('x')");
        Assert.Equal((ProjectFileKind.Raw, lua, null), Parts(EntryConversion.ToProjectFile("script/a.lua", lua, TestData.Schema)));
        var notLoc = EntryConversion.ToProjectFile("text/a.loc", lua, null);
        Assert.Equal((ProjectFileKind.Raw, lua), (notLoc.Kind, notLoc.Bytes));
        Assert.False(string.IsNullOrWhiteSpace(notLoc.Note));

        static (ProjectFileKind, byte[], string?) Parts(ProjectFileContent c) => (c.Kind, c.Bytes, c.Note);
    }

    [Fact] public void ToPackEntry_reports_problems_with_their_line()
    {
        var ok = "key\tcost\tspeed\n#units_tables;2;whatever/the/meta/says\t\t\na\t1\t1\n";
        Assert.NotNull(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/units_tables/x", TestData.Utf8(ok), TestData.Schema, NewGuid).Bytes);
        Assert.NotNull(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/Units_Tables/x", TestData.Utf8("\uFEFF" + ok.Replace("\n", "\r\n")), TestData.Schema, NewGuid).Bytes);

        Problem(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/units_tables/x", TestData.Utf8(ok.Replace("a\t1", "a\tone")), TestData.Schema, NewGuid), 3);
        Problem(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/other_tables/x", TestData.Utf8(ok), TestData.Schema, NewGuid), 2);
        Problem(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/units_tables/x", TestData.Utf8(ok), null, NewGuid), 0);
        Problem(EntryConversion.ToPackEntry(ProjectFileKind.Table, "db/units_tables/x", [0x6B, 0xFF, 0xFE], TestData.Schema, NewGuid), 0);
        Problem(EntryConversion.ToPackEntry(ProjectFileKind.Loc, "text/a.loc", TestData.Utf8("key\ttext\n#Loc;1;p\t\n"), null, NewGuid), 1);

        static void Problem(PackEntryContent content, int line)
        {
            Assert.Null(content.Bytes);
            Assert.Equal(line, content.Line);
            Assert.False(string.IsNullOrWhiteSpace(content.Detail));
        }
    }
}
```

- [ ] **Step 2: RED prüfen** — Build schlägt fehl.

- [ ] **Step 3: `ProjectLayout` und `EntryConversion` implementieren**

- [ ] **Step 4: GREEN prüfen** — ganze Suite mit beiden Variablen.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: map pack entries to project files and back"
```

---

### Task 4: `PackImporter`

**Files:**
- Create: `src/TotalWarModTool.Projects/PackImporter.cs`, `ImportModel.cs`
- Test: `tests/TotalWarModTool.Projects.Tests/PackImporterTests.cs`

**Interfaces:**
- Consumes: Task 1 (`ProjectFile`, `PackNameRules`, `ModProject`), Task 3 (`ProjectLayout`, `EntryConversion`), `PackReader.ReadIndex/ReadEntry`, `PackIndex`, `OperationProgress`; Testhelfer `PackBuilder`, `TestData`.
- Produces:

```csharp
public sealed record SkippedEntry(string PackPath, string Reason);
public sealed record BinaryTable(string PackPath, string Reason);

/// Counts by path only; nothing is read from the data section.
public sealed record ImportPreview(int Tables, int LocFiles, int OtherFiles, long StoredBytes, IReadOnlyList<SkippedEntry> Skipped);

public enum ImportProblem { None, SourceUnreadable, InvalidPackName, TargetNotEmpty, TargetUnwritable }

/// Project is null unless Problem is None. StayedBinary lists tables and loc files that are in the project as binary files.
public sealed record ImportReport(ModProject? Project, ImportProblem Problem, string? Detail,
    int Tables, int LocFiles, int OtherFiles, IReadOnlyList<BinaryTable> StayedBinary, IReadOnlyList<SkippedEntry> Skipped);

public sealed record ImportRequest(string SourcePack, string TargetFolder, string ProjectName, string GameId, string PackName,
    IReadOnlySet<string> VanillaPackNames);

public static class PackImporter
{
    public const long MaxEntryBytes = 1_073_741_824;   // 1 GiB; larger entries are skipped and reported

    public static ImportPreview Preview(PackIndex index);
    public static ImportReport Import(ImportRequest request, GameSchema? schema, IProgress<OperationProgress>? progress, CancellationToken cancellationToken);
}
```

Festlegungen:
- Welche Einträge ausgelassen werden, entscheidet eine gemeinsame private Funktion für `Preview` und `Import`, in Index-Reihenfolge, erster Grund gewinnt:
  1. `ProjectLayout.UnsafePathReason` → dessen Text.
  2. `ProjectLayout.LooksLikeTextForm` → `the name collides with the text form of a table or loc file`.
  3. Derselbe Pfad wie ein früherer Eintrag ohne Rücksicht auf Groß-/Kleinschreibung → `duplicate of '<früherer Pfad>'`.
  4. Der Pfad ist zugleich Ordner eines früheren Eintrags oder ein früherer Eintrag ist Ordner dieses Pfads (`a` und `a/b`), ohne Rücksicht auf Groß-/Kleinschreibung → `'<früherer Pfad>' is a file and a folder`.
- `Preview` zählt die übrigen Einträge nach Pfad: `DbTablePath.TryGetTableName` → Tabelle; Endung `.loc` → Loc; sonst andere. `StoredBytes` ist die Summe ihrer `StoredSize`.
- `Import`, Reihenfolge:
  1. `PackNameRules.Check(request.PackName)` schlägt fehl oder der Name steht in `VanillaPackNames` (der Aufrufer liefert ein Set, das Groß-/Kleinschreibung ignoriert) → `InvalidPackName`. `ProjectName` oder `GameId` leer → `ArgumentException`.
  2. `PackReader.ReadIndex`; Problem → `SourceUnreadable` mit Problem und Detail.
  3. `TargetFolder` existiert und ist nicht leer, oder ist eine Datei → `TargetNotEmpty`.
  4. Staging-Ordner `<TargetFolder>.import-<8 Hex-Zeichen>` im selben Elternordner anlegen. Dorthin schreiben: pro Eintrag die Datei unter `pack\<ToRelativeFile>`, am Ende die Projektdatei (`ProjectFile.Save` mit neuer GUID, den Abhängigkeiten des Index und `request.PackName`).
  5. Ein vorhandener leerer `TargetFolder` wird entfernt, dann `Directory.Move(staging, TargetFolder)`.
- Pro Eintrag: Token prüfen; auslassen nach der gemeinsamen Funktion; Zielpfad länger als 259 Zeichen (bezogen auf den endgültigen `TargetFolder`) → ausgelassen mit Grund; `PackReader.ReadEntry` mit `MaxEntryBytes`; Problem → ausgelassen mit `EntryProblem` und Detail; `EntryConversion.ToProjectFile`; Datei schreiben. Danach `progress` melden, auch für einen ausgelassenen Eintrag (`Done` = Zahl der fertigen Einträge, `Total` = Zahl aller Einträge, `Item` = Pfad).
- Eine Tabelle oder Loc-Datei, die `Raw` blieb (Note nicht `null`), steht in `StayedBinary`. Die Zähler `Tables` und `LocFiles` zählen nur die als Text geschriebenen, `OtherFiles` alle `Raw`.
- `IOException` oder `UnauthorizedAccessException` beim Anlegen, Schreiben oder Umbenennen → `TargetUnwritable`. Abbruch → `OperationCanceledException`.
- In jedem Fall außer Erfolg wird der Staging-Ordner entfernt (`finally`, mit bis zu drei Versuchen), und `TargetFolder` ist danach im selben Zustand wie davor.
- Die Quelle wird nur über `PackReader` gelesen.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class PackImporterTests
{
    private static readonly IReadOnlySet<string> Vanilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "data.pack" };

    private static string Pack(TempDir dir, Action<PackBuilder> fill)
    {
        var builder = new PackBuilder();
        fill(builder);
        return builder.WriteTo(dir.File("source.pack"));
    }

    private static ImportRequest Request(TempDir dir, string source, string packName = "my_mod.pack") =>
        new(source, Path.Combine(dir.Path, "project"), "My Mod", "warhammer_3", packName, Vanilla);

    [Fact] public void Imports_tables_loc_files_and_other_files()
    {
        using var dir = new TempDir();
        var units = TestData.Units(("unit_a", 100, 0.1f));
        var loc = TestData.Loc(("k", "Text"));
        var source = Pack(dir, p => p.AddDependency("base.pack")
            .AddEntry(@"db\units_tables\my_mod", units).AddEntry(@"text\db\my_mod.loc", loc)
            .AddEntry(@"script\a.lua", TestData.Utf8("print(1)")).AddEntry(@"db\other_tables\x", [1, 2, 3]));
        var before = File.ReadAllBytes(source);
        var seen = new List<OperationProgress>();
        var report = PackImporter.Import(Request(dir, source), TestData.Schema, new SyncProgress(seen.Add), CancellationToken.None);

        Assert.Equal(ImportProblem.None, report.Problem);
        Assert.Equal((1, 1, 2), (report.Tables, report.LocFiles, report.OtherFiles));
        Assert.Equal("db/other_tables/x", Assert.Single(report.StayedBinary).PackPath);
        Assert.Empty(report.Skipped);
        var project = Path.Combine(dir.Path, "project");
        Assert.StartsWith("key\tcost\tspeed\n", File.ReadAllText(Path.Combine(project, "pack", "db", "units_tables", "my_mod.tsv")));
        Assert.True(File.Exists(Path.Combine(project, "pack", "text", "db", "my_mod.loc.tsv")));
        Assert.Equal("print(1)", File.ReadAllText(Path.Combine(project, "pack", "script", "a.lua")));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(project, "pack", "db", "other_tables", "x")));
        var loaded = ProjectFile.Load(project).Project!;
        Assert.Equal((report.Project!.Id, "My Mod", "warhammer_3", "my_mod.pack"), (loaded.Id, loaded.Name, loaded.GameId, loaded.PackName));
        Assert.Equal(["base.pack"], loaded.Dependencies);
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Equal([1L, 2L, 3L, 4L], seen.Select(p => p.Done));
        Assert.All(seen, p => Assert.Equal(4L, p.Total));
        Assert.Empty(Directory.EnumerateDirectories(dir.Path, "*.import-*"));
    }

    [Fact] public void Unsafe_and_colliding_entries_are_skipped_and_reported()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p
            .AddEntry(@"..\..\escape.txt", [1]).AddEntry(@"C:\abs.txt", [1]).AddEntry(@"a\con", [1]).AddEntry(@"a\b.", [1])
            .AddEntry(@"db\units_tables\x.tsv", [1]).AddEntry(@"text\x.loc.tsv", [1])
            .AddEntry(@"keep\File.txt", [1]).AddEntry(@"KEEP\file.TXT", [2])
            .AddEntry(@"node", [1]).AddEntry(@"node\child.txt", [1]));
        var preview = PackImporter.Preview(PackReader.ReadIndex(source).Index!);
        var report = PackImporter.Import(Request(dir, source), TestData.Schema, null, CancellationToken.None);

        Assert.Equal(ImportProblem.None, report.Problem);
        Assert.Equal(["../../escape.txt", "C:/abs.txt", "a/con", "a/b.", "db/units_tables/x.tsv", "text/x.loc.tsv", "KEEP/file.TXT", "node/child.txt"], report.Skipped.Select(s => s.PackPath));
        Assert.Equal(report.Skipped, preview.Skipped);
        Assert.All(report.Skipped, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
        Assert.Equal((0, 0, 2, 2L), (preview.Tables, preview.LocFiles, preview.OtherFiles, preview.StoredBytes));
        var files = Directory.EnumerateFiles(dir.Path, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dir.Path, f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.Equal([@"project\pack\keep\File.txt", @"project\pack\node", @"project\twmt-project.json", "source.pack"], files);
    }

    [Fact] public void An_unreadable_entry_is_skipped_and_the_import_goes_on()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry(@"a\broken.bin", [1, 2, 3], compressed: true).AddEntry(@"a\fine.txt", [7]));
        var report = PackImporter.Import(Request(dir, source), null, null, CancellationToken.None);
        Assert.Equal(ImportProblem.None, report.Problem);
        Assert.Equal("a/broken.bin", Assert.Single(report.Skipped).PackPath);
        Assert.True(File.Exists(Path.Combine(dir.Path, "project", "pack", "a", "fine.txt")));
    }

    [Fact] public void Problems_before_the_first_write()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry("a.txt", [1]));
        Assert.Equal(ImportProblem.InvalidPackName, PackImporter.Import(Request(dir, source, "nope"), null, null, CancellationToken.None).Problem);
        Assert.Equal(ImportProblem.InvalidPackName, PackImporter.Import(Request(dir, source, "DATA.pack"), null, null, CancellationToken.None).Problem);
        Assert.Equal(ImportProblem.SourceUnreadable, PackImporter.Import(Request(dir, dir.File("missing.pack")), null, null, CancellationToken.None).Problem);
        Directory.CreateDirectory(Path.Combine(dir.Path, "project"));
        File.WriteAllText(Path.Combine(dir.Path, "project", "mine.txt"), "x");
        Assert.Equal(ImportProblem.TargetNotEmpty, PackImporter.Import(Request(dir, source), null, null, CancellationToken.None).Problem);
        Assert.Equal(["mine.txt"], Directory.EnumerateFileSystemEntries(Path.Combine(dir.Path, "project")).Select(Path.GetFileName));
        Assert.Empty(Directory.EnumerateDirectories(dir.Path, "*.import-*"));
    }

    [Fact] public void An_existing_empty_target_folder_is_used()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry("a.txt", [1]));
        Directory.CreateDirectory(Path.Combine(dir.Path, "project"));
        Assert.Equal(ImportProblem.None, PackImporter.Import(Request(dir, source), null, null, CancellationToken.None).Problem);
        Assert.True(File.Exists(Path.Combine(dir.Path, "project", "pack", "a.txt")));
    }

    [Fact] public void Cancel_leaves_no_trace()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry("a.txt", [1]).AddEntry("b.txt", [2]).AddEntry("c.txt", [3]));
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.Done == 2) cts.Cancel(); });
        Assert.ThrowsAny<OperationCanceledException>(() => PackImporter.Import(Request(dir, source), null, progress, cts.Token));
        Assert.Equal(["source.pack"], Directory.EnumerateFileSystemEntries(dir.Path).Select(Path.GetFileName));
    }

    [Fact] public void A_failed_import_leaves_no_trace()
    {
        using var dir = new TempDir();
        var source = Pack(dir, p => p.AddEntry("a.txt", [1]));
        // The parent of the target is a file, so nothing can be created below it.
        File.WriteAllText(dir.File("blocker"), "x");
        var request = Request(dir, source) with { TargetFolder = Path.Combine(dir.Path, "blocker", "project") };
        var report = PackImporter.Import(request, null, null, CancellationToken.None);
        Assert.Equal(ImportProblem.TargetUnwritable, report.Problem);
        Assert.Null(report.Project);
        Assert.Equal(["blocker", "source.pack"], Directory.EnumerateFileSystemEntries(dir.Path).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
    }

    private sealed class SyncProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
```

- [ ] **Step 2: RED prüfen** — Build schlägt fehl.

- [ ] **Step 3: `ImportModel.cs` (Typen) und `PackImporter` implementieren**

- [ ] **Step 4: GREEN prüfen** — ganze Suite mit beiden Variablen.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: import a pack into a new project folder"
```

---

### Task 5: `ProjectBuilder` und `AtomicFile.Replace`

**Files:**
- Create: `src/TotalWarModTool.Projects/ProjectBuilder.cs`, `BuildModel.cs`
- Modify: `src/TotalWarModTool.Storage/AtomicFile.cs` (neue Methode `Replace`)
- Test: `tests/TotalWarModTool.Projects.Tests/ProjectBuilderTests.cs`, `ProjectRealDataTests.cs`; `tests/TotalWarModTool.Storage.Tests/AtomicFileTests.cs` (ergänzen)

**Interfaces:**
- Consumes: Task 1, Task 3 (`ProjectLayout.FromRelativeFile`, `EntryConversion.ToPackEntry`), Task 4 (`PackImporter`, nur im Test auf echten Daten), `PackWriter.Validate/Write`, `PackWriteEntry`, `OperationProgress`.
- Produces:

```csharp
// Storage
public static class AtomicFile
{
    /// Moves tempPath over targetPath (replacing it) with the same retries as WriteAllText. The error of the last try is rethrown;
    /// tempPath is left in place then.
    public static void Replace(string tempPath, string targetPath);
}

// Projects
/// RelativeFile is relative to the pack folder ('\'), empty when the problem is not tied to one file. Line is 0 when it has none.
public sealed record BuildProblem(string RelativeFile, int Line, string Detail);

/// Entries are ready for PackWriter. An empty Problems list means the plan can be written.
public sealed record BuildPlan(IReadOnlyList<PackWriteEntry> Entries, IReadOnlyList<string> Dependencies, IReadOnlyList<BuildProblem> Problems);

public enum BuildOutcome { Built, HasProblems, WriteFailed }

/// PackPath is the built pack when Outcome is Built.
public sealed record BuildResult(BuildOutcome Outcome, string? PackPath, string? Detail);

public static class ProjectBuilder
{
    /// Reads the pack folder of the project and prepares every entry. Writes nothing.
    public static BuildPlan ComputeBuild(string projectFolder, ModProject project, GameSchema? schema, Func<string> newGuid,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken);

    /// Writes build\<PackName> through a temp file in the same folder. A plan with problems writes nothing.
    public static BuildResult WriteBuild(string projectFolder, ModProject project, BuildPlan plan, DateTimeOffset buildTime,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken);
}
```

Festlegungen:
- `ComputeBuild`:
  - Der Ordner `pack` fehlt → ein Problem `the pack folder is missing`, keine Einträge. Ein leerer Ordner `pack` ist kein Problem (leeres Pack).
  - Alle Dateien unter `pack` rekursiv, nach relativem Pfad ordinal sortiert. Leere Ordner zählen nicht. Pro Datei: Token prüfen, Datei vorbereiten, danach `progress` melden (`Done` = Zahl der fertigen Dateien, `Total` = Zahl der Dateien, `Item` = relativer Pfad).
  - `ProjectLayout.FromRelativeFile` bestimmt Art und Pack-Pfad. `Table` und `Loc`: Datei ganz lesen, `EntryConversion.ToPackEntry`; Problem → `BuildProblem` mit Zeile und Detail; sonst ein Eintrag mit den Bytes im Speicher. `Raw`: Länge aus `FileInfo`, `Open` öffnet die Datei lesend mit `FileShare.Read`; nichts wird vorab gelesen.
  - Datei nicht lesbar (`IOException`, `UnauthorizedAccessException`) → `BuildProblem` für diese Datei.
  - Alle Probleme werden gesammelt, eines pro Datei. Danach `PackWriter.Validate(project.Dependencies, entries)`; ein Problem dort wird ein `BuildProblem` mit leerem `RelativeFile` und dem `Detail` des Writers.
  - `Dependencies` des Plans sind die des Projekts.
- `WriteBuild`:
  - Plan mit Problemen → `HasProblems`, nichts wird angelegt oder verändert.
  - Ordner `build` anlegen. Temp-Datei `build\<PackName>.<8 Hex-Zeichen>.tmp`. `PackWriter.Write` dorthin; der Fortschritt des Writers wird synchron (kein `Progress<T>`) in `OperationProgress(done, entries.Count, null)` übersetzt.
  - Problem des Writers → `WriteFailed` mit Problem und Detail; das alte Pack bleibt unberührt.
  - Erfolg → `AtomicFile.Replace(temp, build\<PackName>)`; scheitert es (`IOException`, `UnauthorizedAccessException`) → `WriteFailed`, temp-Datei entfernen, altes Pack bleibt.
  - Abbruch → `OperationCanceledException`; die temp-Datei ist danach weg (in `finally` nachprüfen und entfernen).
- `AtomicFile.Replace`: nutzt das vorhandene private `MoveWithRetry`. `WriteAllText` bleibt im Verhalten unverändert.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class ProjectBuilderTests
{
    private static readonly DateTimeOffset BuildTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private static string NewGuid() => TestData.HeaderGuid;

    private static (string Folder, ModProject Project) NewProject(TempDir dir)
    {
        var folder = Path.Combine(dir.Path, "project");
        return (folder, ProjectFile.CreateNew(folder, "My Mod", "warhammer_3", "my_mod.pack", ["base.pack"]).Project!);
    }

    private static void Put(string folder, string relative, byte[] bytes)
    {
        var file = Path.Combine(folder, "pack", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);
    }

    private const string UnitsTsv = "key\tcost\tspeed\n#units_tables;2;db/units_tables/my_mod\t\t\nunit_a\t100\t0.1\n";

    [Fact] public void Builds_a_pack_from_text_and_raw_files()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Put(folder, @"db\units_tables\my_mod.tsv", TestData.Utf8("\uFEFF" + UnitsTsv.Replace("\n", "\r\n")));
        Put(folder, @"text\db\my_mod.loc.tsv", TestData.Utf8("key\ttext\ttooltip\n#Loc;1;x\t\t\nk\tText\tfalse\n"));
        Put(folder, @"script\a.lua", TestData.Utf8("print(1)"));
        Put(folder, @"db\other_tables\binary", [1, 2, 3]);
        Directory.CreateDirectory(Path.Combine(folder, "pack", "empty folder"));

        var seen = new List<OperationProgress>();
        var plan = ProjectBuilder.ComputeBuild(folder, project, TestData.Schema, NewGuid, new SyncProgress(seen.Add), CancellationToken.None);
        Assert.Empty(plan.Problems);
        Assert.Equal(4, plan.Entries.Count);
        Assert.Equal(4L, seen[^1].Done);
        Assert.False(Directory.Exists(Path.Combine(folder, "build")));

        var result = ProjectBuilder.WriteBuild(folder, project, plan, BuildTime, null, CancellationToken.None);
        Assert.Equal(BuildOutcome.Built, result.Outcome);
        Assert.Equal(Path.Combine(folder, "build", "my_mod.pack"), result.PackPath);
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(Path.Combine(folder, "build")).Select(Path.GetFileName));

        var index = PackReader.ReadIndex(result.PackPath!).Index!;
        Assert.Equal(["base.pack"], index.Dependencies);
        Assert.Equal(["db/other_tables/binary", "db/units_tables/my_mod", "script/a.lua", "text/db/my_mod.loc"], index.Entries.Select(e => e.Path));
        Assert.Equal(TestData.Units(("unit_a", 100, 0.1f)), PackReader.ReadEntry(index, index.Find("db/units_tables/my_mod")!, CancellationToken.None).Bytes);
        Assert.Equal(TestData.Loc(("k", "Text")), PackReader.ReadEntry(index, index.Find("text/db/my_mod.loc")!, CancellationToken.None).Bytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, PackReader.ReadEntry(index, index.Find("db/other_tables/binary")!, CancellationToken.None).Bytes);
    }

    [Fact] public void Every_problem_is_reported_and_nothing_is_written()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Put(folder, @"db\units_tables\bad_value.tsv", TestData.Utf8(UnitsTsv.Replace("100", "many")));
        Put(folder, @"db\other_tables\wrong_folder.tsv", TestData.Utf8(UnitsTsv));
        Put(folder, @"db\units_tables\not_utf8.tsv", [0x6B, 0xFF]);
        Put(folder, @"text\broken.loc.tsv", TestData.Utf8("nothing"));
        Put(folder, @"db\units_tables\twice.tsv", TestData.Utf8(UnitsTsv));
        Put(folder, @"db\units_tables\twice", [1]);
        Put(folder, @"script\fine.lua", [1]);
        Directory.CreateDirectory(Path.Combine(folder, "build"));
        File.WriteAllBytes(Path.Combine(folder, "build", "my_mod.pack"), [9, 9]);

        var plan = ProjectBuilder.ComputeBuild(folder, project, TestData.Schema, NewGuid, null, CancellationToken.None);
        Assert.Equal([(@"db\other_tables\wrong_folder.tsv", 2), (@"db\units_tables\bad_value.tsv", 3), (@"db\units_tables\not_utf8.tsv", 0), (@"text\broken.loc.tsv", 2), ("", 0)],
            plan.Problems.Select(p => (p.RelativeFile, p.Line)));
        Assert.All(plan.Problems, p => Assert.False(string.IsNullOrWhiteSpace(p.Detail)));
        Assert.Contains("twice", plan.Problems[^1].Detail);

        var result = ProjectBuilder.WriteBuild(folder, project, plan, BuildTime, null, CancellationToken.None);
        Assert.Equal(BuildOutcome.HasProblems, result.Outcome);
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(Path.Combine(folder, "build", "my_mod.pack")));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(Path.Combine(folder, "build")).Select(Path.GetFileName));
    }

    [Fact] public void Tables_need_a_schema_and_a_missing_pack_folder_is_a_problem()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Assert.Empty(ProjectBuilder.ComputeBuild(folder, project, null, NewGuid, null, CancellationToken.None).Problems);
        Put(folder, @"db\units_tables\my_mod.tsv", TestData.Utf8(UnitsTsv));
        Assert.Equal(@"db\units_tables\my_mod.tsv", Assert.Single(ProjectBuilder.ComputeBuild(folder, project, null, NewGuid, null, CancellationToken.None).Problems).RelativeFile);
        Directory.Delete(Path.Combine(folder, "pack"), recursive: true);
        Assert.Equal("", Assert.Single(ProjectBuilder.ComputeBuild(folder, project, TestData.Schema, NewGuid, null, CancellationToken.None).Problems).RelativeFile);
    }

    [Fact] public void A_failed_or_cancelled_write_keeps_the_old_pack_and_leaves_no_temp_file()
    {
        using var dir = new TempDir();
        var (folder, project) = NewProject(dir);
        Put(folder, @"a.bin", [1, 2, 3]);
        Put(folder, @"b.bin", [4]);
        var build = Path.Combine(folder, "build");
        Directory.CreateDirectory(build);
        File.WriteAllBytes(Path.Combine(build, "my_mod.pack"), [9, 9]);

        var plan = ProjectBuilder.ComputeBuild(folder, project, null, NewGuid, null, CancellationToken.None);
        File.WriteAllBytes(Path.Combine(folder, "pack", "a.bin"), [1]);   // the file shrank after the plan was made
        var failed = ProjectBuilder.WriteBuild(folder, project, plan, BuildTime, null, CancellationToken.None);
        Assert.Equal(BuildOutcome.WriteFailed, failed.Outcome);
        Assert.Contains("SourceChanged", failed.Detail);

        using var cts = new CancellationTokenSource();
        var again = ProjectBuilder.ComputeBuild(folder, project, null, NewGuid, null, CancellationToken.None);
        Assert.ThrowsAny<OperationCanceledException>(() =>
            ProjectBuilder.WriteBuild(folder, project, again, BuildTime, new SyncProgress(_ => cts.Cancel()), cts.Token));

        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(Path.Combine(build, "my_mod.pack")));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(build).Select(Path.GetFileName));
    }

    private sealed class SyncProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}

public class ProjectRealDataTests
{
    [RealDataFact] public void A_workshop_pack_imported_and_rebuilt_has_the_same_content()
    {
        if (RealData.Wh3Workshop is not { } workshop) return;
        var source = Directory.EnumerateFiles(workshop, "*.pack", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f)).Where(f => f.Length is > 1_000_000 and < 30_000_000).OrderBy(f => f.Length).ThenBy(f => f.FullName, StringComparer.Ordinal)
            .Select(f => f.FullName).FirstOrDefault(f => PackReader.ReadIndex(f).Index is { } i && i.Entries.Any(e => DbTablePath.TryGetTableName(e.Path, out _)));
        if (source is null) return;

        using var dir = new TempDir();
        var schema = SchemaLoader.Load(Path.Combine(RepoRoot.Find(), "schemas", "schema_wh3.ron")).Schema!;
        var request = new ImportRequest(source, Path.Combine(dir.Path, "project"), "Round Trip", "warhammer_3", "twmt_round_trip.pack", new HashSet<string>());
        var report = PackImporter.Import(request, schema, null, CancellationToken.None);
        Assert.Equal(ImportProblem.None, report.Problem);
        Assert.True(report.Tables > 0, "no table was imported as text");

        var folder = request.TargetFolder;
        var plan = ProjectBuilder.ComputeBuild(folder, report.Project!, schema, () => Guid.NewGuid().ToString(), null, CancellationToken.None);
        Assert.True(plan.Problems.Count == 0, string.Join("; ", plan.Problems.Take(5)));
        var built = ProjectBuilder.WriteBuild(folder, report.Project!, plan, DateTimeOffset.UnixEpoch, null, CancellationToken.None);
        Assert.Equal(BuildOutcome.Built, built.Outcome);

        var original = PackReader.ReadIndex(source).Index!;
        var rebuilt = PackReader.ReadIndex(built.PackPath!).Index!;
        var skipped = report.Skipped.Select(s => s.PackPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(original.Dependencies, rebuilt.Dependencies);
        Assert.Equal(original.Entries.Count - skipped.Count, rebuilt.Entries.Count);
        foreach (var entry in original.Entries.Where(e => !skipped.Contains(e.Path)))
        {
            var before = PackReader.ReadEntry(original, entry, CancellationToken.None).Bytes!;
            var after = PackReader.ReadEntry(rebuilt, rebuilt.Find(entry.Path)!, CancellationToken.None).Bytes!;
            if (DbTablePath.TryGetTableName(entry.Path, out var table)
                && DbTableDecoder.Decode(before, table, schema) is { Status: TableDecodeStatus.Ok } a && DbTableDecoder.Decode(after, table, schema) is { Status: TableDecodeStatus.Ok } b)
            {
                // A rebuilt table differs only in the GUID of its header.
                Assert.True(DbTableEncoder.Encode(a.Definition!, a.Version, null, a.Rows).AsSpan().SequenceEqual(DbTableEncoder.Encode(b.Definition!, b.Version, null, b.Rows)), entry.Path);
            }
            else
            {
                Assert.True(before.AsSpan().SequenceEqual(after), entry.Path);
            }
        }
    }
}
```

In `tests/TotalWarModTool.Storage.Tests/AtomicFileTests.cs` ergänzen:

```csharp
[Fact] public void Replace_moves_the_temp_file_over_the_target()
{
    using var dir = new TempDir();
    File.WriteAllText(dir.File("target.bin"), "old");
    File.WriteAllText(dir.File("temp.bin"), "new");
    AtomicFile.Replace(dir.File("temp.bin"), dir.File("target.bin"));
    Assert.Equal("new", File.ReadAllText(dir.File("target.bin")));
    Assert.False(File.Exists(dir.File("temp.bin")));
    File.WriteAllText(dir.File("temp2.bin"), "first");
    AtomicFile.Replace(dir.File("temp2.bin"), dir.File("fresh.bin"));
    Assert.Equal("first", File.ReadAllText(dir.File("fresh.bin")));
}
```

- [ ] **Step 2: RED prüfen** — Build schlägt fehl.

- [ ] **Step 3: `AtomicFile.Replace`, `BuildModel.cs` (Typen) und `ProjectBuilder` implementieren**

Das Testprojekt `Projects.Tests` bekommt für den Test auf echten Daten keine weitere Referenz: `SchemaLoader` liegt in `Schema`.

- [ ] **Step 4: GREEN prüfen** — ganze Suite mit beiden Variablen. Im Bericht nennen: welches Workshop-Pack der Test auf echten Daten nahm, wie viele Tabellen als Text, wie viele binär blieben, wie viele Einträge ausgelassen wurden.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: build a pack from a project folder"
```

---

### Task 6: `PackInstaller` und Doku

**Files:**
- Create: `src/TotalWarModTool.Projects/PackInstaller.cs`
- Modify: `docs/specs/2026-10-03-teil-2-projekte-bearbeiten-bauen.md`, `Backlog.txt`
- Test: `tests/TotalWarModTool.Projects.Tests/PackInstallerTests.cs`

**Interfaces:**
- Consumes: Task 2 (`InstalledPack`), `AtomicFile.Replace` (Task 5), `PackNameRules`.
- Produces:

```csharp
public enum InstallProblem { None, InvalidPackName, VanillaName, NoBuiltPack, DataFolderMissing, ForeignFile, TargetUnwritable }

/// Installed is the new record for the project list: set after an install, null after an uninstall or on a problem.
public sealed record InstallResult(InstallProblem Problem, string? Detail, InstalledPack? Installed);

/// recorded is what the project list holds for this project (null when the tool never installed it).
public sealed record InstallRequest(string DataFolder, string PackName, InstalledPack? Recorded, IReadOnlySet<string> VanillaPackNames);

public static class PackInstaller
{
    /// Pure check: None when Install or Uninstall may touch <DataFolder>\<PackName>.
    public static InstallProblem CheckOwnership(InstallRequest request);
    public static InstallResult Install(string builtPack, InstallRequest request, CancellationToken cancellationToken);
    public static InstallResult Uninstall(InstallRequest request);
}
```

Festlegungen (Spec 5.7):
- `CheckOwnership`, erste zutreffende Regel:
  1. `PackNameRules.Check` schlägt fehl → `InvalidPackName`.
  2. Name in `VanillaPackNames` (das Set des Aufrufers ignoriert Groß-/Kleinschreibung) → `VanillaName`.
  3. `DataFolder` existiert nicht → `DataFolderMissing`.
  4. `<DataFolder>\<PackName>` existiert nicht → `None`.
  5. Die Datei existiert: `None` nur, wenn `Recorded` nicht `null` ist, `Recorded.FileName` dem Namen gleicht (ohne Rücksicht auf Groß-/Kleinschreibung) und Länge und `LastWriteTimeUtc` der Datei genau `Recorded` entsprechen. Sonst `ForeignFile`.
- `Install`: `CheckOwnership`; Problem → Ergebnis ohne jede Änderung. `builtPack` fehlt → `NoBuiltPack`. Kopieren in `<DataFolder>\<PackName>.twmt-tmp` (Token vor jedem Block prüfen), dann `AtomicFile.Replace`. Danach den Zustand der Zieldatei lesen und als `InstalledPack` zurückgeben. `IOException` oder `UnauthorizedAccessException` (Spiel sperrt die Datei, Ordner schreibgeschützt) → `TargetUnwritable`; die temp-Datei wird entfernt, die alte Zieldatei bleibt unberührt. Abbruch → `OperationCanceledException`, temp-Datei weg.
- `Uninstall`: `CheckOwnership`; Problem → Ergebnis ohne Änderung. Datei fehlt → `None` (nichts zu tun). Sonst löschen; Fehler → `TargetUnwritable`.
- Geschrieben oder gelöscht werden in `DataFolder` nur `<PackName>` und `<PackName>.twmt-tmp`.
- Der Aufrufer trägt das Ergebnis in die Projektliste ein (`ProjectListEdits.SetInstalled`) und speichert sie.

Doku im selben Commit:
- Spec 5.4: neuer Unterabschnitt „Festlegungen aus Plan 2“ mit diesen Punkten:
  1. Der Papierkorb kommt mit Plan 4, weil erst das Löschen von Dateien ihn braucht (Abschnitt 9 entsprechend anpassen: Punkt 2 ohne „Papierkorb“, Punkt 4 mit „Papierkorb“; die Zeile `Projects` in der Modultabelle 5.4 ebenso).
  2. `Projects` referenziert `GameProfiles` nicht. Der `data`-Ordner und die Vanilla-Packnamen kommen vom Aufrufer (Modultabelle anpassen).
  3. Der `packName` eines Imports wird vom Aufrufer vorgegeben (Vorschlag: Dateiname der Quelle) und gegen die Vanilla-Namen geprüft (5.5 anpassen: der Satz „`packName` ist der Dateiname der Quelle“).
  4. Die Lage einer Datei im Projekt bestimmt ihren Pack-Pfad. Der Tabellenname der Metazeile muss zum Ordner passen, der Pfad der Metazeile wird nicht geprüft.
  5. Eine Projektdatei mit höherer `schemaVersion` wird nicht geladen.
  6. Einträge über 1 GiB werden beim Import ausgelassen und gemeldet.
  7. Jede Datei unter `pack\` geht ins Pack, auch Dateien wie `.gitkeep`.
  8. Installieren gibt den neuen Fingerprint zurück; die App speichert ihn in der Projektliste. Scheitert dieses Speichern, gilt die Datei beim nächsten Mal als fremd.
- `Backlog.txt`, Abschnitt „Aus Teil 2 zurückgestellt“: Import großer Einträge als Stream (über 1 GiB); Dateien unter `pack\` vom Bau ausschließen (z. B. `.gitkeep`); das installierte Pack auch erkennen, wenn die Projektliste nach der Installation nicht gespeichert werden konnte. Die Zeile „Abhängigkeiten eines Projekts auf doppelte Namen prüfen (mit der Projektdatei in Plan 2)“ bleibt im Backlog, ohne den Zusatz in Klammern.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class PackInstallerTests
{
    private static readonly IReadOnlySet<string> Vanilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "data.pack" };

    private static (string Built, string Data) Setup(TempDir dir)
    {
        var data = Path.Combine(dir.Path, "data");
        Directory.CreateDirectory(data);
        File.WriteAllBytes(dir.File("built.pack"), [1, 2, 3]);
        return (dir.File("built.pack"), data);
    }

    [Fact] public void Install_update_and_uninstall()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        var first = PackInstaller.Install(built, new InstallRequest(data, "my_mod.pack", null, Vanilla), CancellationToken.None);
        Assert.Equal(InstallProblem.None, first.Problem);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(data, "my_mod.pack")));
        var info = new FileInfo(Path.Combine(data, "my_mod.pack"));
        Assert.Equal(new InstalledPack("my_mod.pack", 3, info.LastWriteTimeUtc), first.Installed);

        File.WriteAllBytes(built, [4, 5, 6, 7]);
        var second = PackInstaller.Install(built, new InstallRequest(data, "MY_MOD.pack", first.Installed, Vanilla), CancellationToken.None);
        Assert.Equal((InstallProblem.None, 4L), (second.Problem, second.Installed!.Length));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);

        var removed = PackInstaller.Uninstall(new InstallRequest(data, "my_mod.pack", second.Installed, Vanilla));
        Assert.Equal((InstallProblem.None, null), (removed.Problem, removed.Installed));
        Assert.Empty(Directory.EnumerateFileSystemEntries(data));
        Assert.Equal(InstallProblem.None, PackInstaller.Uninstall(new InstallRequest(data, "my_mod.pack", second.Installed, Vanilla)).Problem);
    }

    [Fact] public void A_foreign_or_changed_file_is_never_touched()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        var target = Path.Combine(data, "my_mod.pack");
        File.WriteAllBytes(target, [9, 9]);
        var time = File.GetLastWriteTimeUtc(target);

        foreach (var recorded in new InstalledPack?[]
                 {
                     null,
                     new("other.pack", 2, time),
                     new("my_mod.pack", 3, time),
                     new("my_mod.pack", 2, time.AddTicks(1)),
                 })
        {
            var request = new InstallRequest(data, "my_mod.pack", recorded, Vanilla);
            Assert.Equal(InstallProblem.ForeignFile, PackInstaller.CheckOwnership(request));
            Assert.Equal(InstallProblem.ForeignFile, PackInstaller.Install(built, request, CancellationToken.None).Problem);
            Assert.Equal(InstallProblem.ForeignFile, PackInstaller.Uninstall(request).Problem);
        }

        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(target));
        Assert.Equal(time, File.GetLastWriteTimeUtc(target));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
        Assert.Equal(InstallProblem.None, PackInstaller.CheckOwnership(new InstallRequest(data, "my_mod.pack", new InstalledPack("MY_MOD.PACK", 2, time), Vanilla)));
    }

    [Fact] public void Problems_before_anything_is_written()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        Assert.Equal(InstallProblem.VanillaName, PackInstaller.Install(built, new InstallRequest(data, "DATA.pack", null, Vanilla), CancellationToken.None).Problem);
        Assert.Equal(InstallProblem.VanillaName, PackInstaller.Uninstall(new InstallRequest(data, "data.pack", null, Vanilla)).Problem);
        Assert.Equal(InstallProblem.InvalidPackName, PackInstaller.Install(built, new InstallRequest(data, @"..\x.pack", null, Vanilla), CancellationToken.None).Problem);
        Assert.Equal(InstallProblem.DataFolderMissing, PackInstaller.Install(built, new InstallRequest(Path.Combine(dir.Path, "nope"), "a.pack", null, Vanilla), CancellationToken.None).Problem);
        Assert.Equal(InstallProblem.NoBuiltPack, PackInstaller.Install(dir.File("missing.pack"), new InstallRequest(data, "a.pack", null, Vanilla), CancellationToken.None).Problem);
        Assert.Empty(Directory.EnumerateFileSystemEntries(data));
    }

    [Fact] public void A_locked_target_is_reported_and_stays()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        var first = PackInstaller.Install(built, new InstallRequest(data, "my_mod.pack", null, Vanilla), CancellationToken.None);
        File.WriteAllBytes(built, [4, 5]);
        using (new FileStream(Path.Combine(data, "my_mod.pack"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var request = new InstallRequest(data, "my_mod.pack", first.Installed, Vanilla);
            Assert.Equal(InstallProblem.TargetUnwritable, PackInstaller.Install(built, request, CancellationToken.None).Problem);
            Assert.Equal(InstallProblem.TargetUnwritable, PackInstaller.Uninstall(request).Problem);
        }

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(data, "my_mod.pack")));
        Assert.Equal(["my_mod.pack"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
    }

    [Fact] public void Cancel_leaves_no_temp_file()
    {
        using var dir = new TempDir();
        var (built, data) = Setup(dir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PackInstaller.Install(built, new InstallRequest(data, "my_mod.pack", null, Vanilla), cts.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(data));
    }
}
```

Hinweis zu `A_locked_target_is_reported_and_stays`: `AtomicFile.Replace` wiederholt den Versuch rund 1 s lang, bevor der Fehler kommt. Das ist für diesen einen Test in Ordnung.

- [ ] **Step 2: RED prüfen** — Build schlägt fehl.

- [ ] **Step 3: `PackInstaller` implementieren, Spec und Backlog anpassen**

- [ ] **Step 4: GREEN prüfen** — ganze Suite mit beiden Variablen, 0 übersprungen, 0 Warnungen.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: install a built pack into the data folder under ownership rules"
```

---

## Abschluss

Kein nutzersichtbares Verhalten ändert sich: kein Eintrag im Changelog, keine manuelle Checkliste. Nach Task 6 folgt das Review des ganzen Branches, danach Plan 3 (App: Projekte im Explorer, Import, Bauen, Installieren, Textansicht, Session), der auf `ProjectFile`, `ProjectListStore`, `PackImporter`, `ProjectBuilder` und `PackInstaller` aufbaut.
