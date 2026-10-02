# Fundament Plan 1: Gerüst – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eine startbare, leere App „Total War Modding Tool“ mit Theme, Sprache (en/de), Log, Crash-Dialog, Einstellungsfenster und der Fortschritts-Komponente, auf die Plan 2 und 3 aufsetzen.

**Architecture:** UI-freie Leaf-Libraries (`Storage`, `Logging`, `Localization`, `Settings`, `Operations`) mit je einem Testprojekt. Die WPF-App ist der einzige Composition Root: Sie erzeugt alle Services als Instanzen und reicht sie weiter. Keine statischen Singletons.

**Tech Stack:** C# 12, .NET 8 (`net8.0`, App `net8.0-windows`), WPF, xUnit 2.9.2. Gebaut wird mit dem installierten SDK 10.0.400 (gemessen: `net8.0-windows` baut und läuft auf Runtime 8.0.23).

**Spec:** `docs/specs/2026-10-02-fundament.md` (Abschnitte 4.1, 4.6, 4.8, 5, 6, 7). Projektregeln: `CLAUDE.md`.

**Vorlagen aus PMT** (lesen, anpassen, nicht blind kopieren):
`C:\Users\flori\Documents\Claude Projekte\Paradox Modding Tool\Runde 10.1\`
- `src/ParadoxModTool.Storage/SharedFile.cs` (atomar schreiben)
- `src/ParadoxModTool.Localization/UiText.cs`, `src/ParadoxModTool.Settings/LanguageCatalog.cs`
- `src/ParadoxModTool.Settings/WindowBoundsCheck.cs`
- `src/ParadoxModTool.App/ThemeManager.cs` (Methode `MapSystemColors`), `src/ParadoxModTool.App/Themes/Controls.xaml`
- `src/ParadoxModTool.App/TaskProgressWindow.cs`
- `tests/ParadoxModTool.Editor.Tests/Sta.cs`

**Branch:** `feature/fundament-geruest` (von `master`). Baseline: Das Repo enthält noch keinen Code, es gibt keinen früheren Build- oder Teststand.

## Global Constraints

- Code, Bezeichner und Code-Kommentare Englisch. Commit-Messages Englisch.
- Libraries zielen auf `net8.0`, nur `TotalWarModTool.App` und `TotalWarModTool.App.Tests` auf `net8.0-windows`.
- Build-Einstellungen nur in `Directory.Build.props`, Paketversionen nur in `Directory.Packages.props`.
- Erlaubte NuGet-Pakete in diesem Plan: nur `Microsoft.NET.Test.Sdk` 17.11.1, `xunit` 2.9.2, `xunit.runner.visualstudio` 2.8.2. Kein MVVM-Paket, kein Mocking-Framework.
- `TreatWarningsAsErrors` ist an. Ein Build mit Warnungen gilt als fehlgeschlagen.
- Keine statischen Singletons für Services. Module referenzieren nie die App.
- Test-first: failing Test → RED prüfen → implementieren → GREEN → Commit. Die ganze Suite (`dotnet test TotalWarModTool.slnx`) bleibt grün.
- Ein Commit pro Task, nach GREEN. Vor jedem Commit `git add -A`. Die Attribution-Zeile der laufenden Sitzung gehört ans Ende der Commit-Message.
- Die Tests in diesem Plan legen Namen und Erwartungen fest. Scheitert ein Test nur an einem Compiler-Detail (Typinferenz, Nullable-Hinweis), wird der Test minimal angepasst, ohne die Erwartung zu ändern.
- Tests schreiben nur in ein eigenes Temp-Verzeichnis (`TempDir`) und räumen auf.
- Die App schreibt nur nach `%APPDATA%\TotalWarModTool` und `%LocalAppData%\TotalWarModTool`.
- Nie direkt `File.WriteAllText` auf Zieldateien. Immer `AtomicFile.WriteAllText`.
- Kein `catch (Exception)` ohne Log-Eintrag.
- Alle UI-Texte über `AppTexts` (intern `UiText.Get(key, fallback)`), Inline-Fallback Englisch. Jeder neue Key steht in `locales/en.json` und `locales/de.json`. Der en-Wert ist gleich dem Inline-Fallback.
- Standardsprache ist `en`, auch auf einem deutschen System.
- Farben nur über `DynamicResource Theme.<Token>`. Keine Hex-Werte in XAML außerhalb von `ThemePalette`.
- Keine Ellipsen in Labels.
- Alle Klassen der App liegen im Namespace `TotalWarModTool.App`, egal in welchem Unterordner.
- Anzeigename der App: „Total War Modding Tool“. Name im Code und in Ordnern: `TotalWarModTool`. Exe: `TotalWarModTool.exe`.

## Review Focus

Diese Fälle nennt die Spec nicht einzeln, sie treffen Nutzer aber zuerst. Jeder hat einen Test in der genannten Task.

1. **Datei gesperrt oder Ordner nicht beschreibbar** (Virenscanner hält `settings.json`, Log-Ordner ist schreibgeschützt). Erwartung: Speichern wiederholt kurz, die App startet und läuft auch ohne Log weiter. → Task 1 (`WriteAllText_retries_while_target_is_locked`), Task 2 (`Does_not_throw_when_directory_cannot_be_created`, `Does_not_throw_when_log_file_is_locked`).
2. **Sprachdatei kaputt, unvollständig oder verschwunden** (Community-Datei mit falschem Platzhalter, gelöschte `de.json` bei Einstellung `de`). Erwartung: Text fällt pro Key auf Englisch zurück, kein Absturz, Einstellung wird auf `en` repariert. → Task 3 (`Format_falls_back_when_pattern_is_broken`, `Load_of_missing_language_uses_english`), Task 5 (`FixLanguage_resets_unknown_language_to_en`).
3. **Operation meldet extrem oft Fortschritt oder scheitert vor Ablauf der Verzögerung.** Erwartung: UI bleibt flüssig, kein Fenster blitzt auf, Fehler wird gemeldet, Eingabe ist wieder frei. → Task 6 (`Flood_of_reports_posts_nothing_to_the_ui_thread`, `Exception_before_delay_reports_error_without_window`).
4. **Zweiter Fehler, während der Crash-Dialog offen ist.** Erwartung: nur Log-Eintrag, kein Stapel von Dialogen. → Task 7 (`Handle_while_dialog_open_only_logs`).
5. **Monitor wurde abgesteckt, gespeicherte Fensterposition liegt außerhalb.** Erwartung: Fenster erscheint in Standardgröße auf dem sichtbaren Bildschirm. → Task 4 (`WindowBoundsCheck`-Fälle).

---

### Task 1: Solution-Gerüst und `Storage`

**Files:**
- Create: `Directory.Build.props`, `Directory.Packages.props`, `tests/Directory.Build.props`, `TotalWarModTool.slnx`
- Create: `build.bat`, `test.bat`, `README.md`, `CHANGELOG.md`
- Create: `src/TotalWarModTool.Storage/TotalWarModTool.Storage.csproj`, `AtomicFile.cs`, `JsonFileStore.cs`
- Create: `tests/Shared/TempDir.cs`, `tests/Shared/RepoRoot.cs`
- Test: `tests/TotalWarModTool.Storage.Tests/TotalWarModTool.Storage.Tests.csproj`, `AtomicFileTests.cs`, `JsonFileStoreTests.cs`

**Interfaces:**
- Consumes: nichts.
- Produces:

```csharp
namespace TotalWarModTool.Storage;

public static class AtomicFile
{
    // temp file next to target + File.Move(overwrite). Creates the directory.
    // Retries the move up to 15 times on IOException/UnauthorizedAccessException, sleeping 10 ms * attempt.
    // On final failure: deletes the temp file and rethrows.
    public static void WriteAllText(string path, string contents);
}

public enum LoadStatus { Loaded, Missing, Corrupt, NewerVersion, Unreadable }
public sealed record LoadResult<T>(T Value, LoadStatus Status, string? BadCopyPath);

public static class JsonFileStore
{
    public static LoadResult<T> Load<T>(string path, int currentSchemaVersion, Func<T> createDefault) where T : class;
    public static void Save<T>(string path, int schemaVersion, T value) where T : class;
}
```

```csharp
// tests/Shared (linked into every test project)
public sealed class TempDir : IDisposable   // unique folder under %TEMP%\twmt-tests; Dispose deletes recursively
{
    public string Path { get; }
    public string File(string name);        // Path combined with name
}
public static class RepoRoot { public static string Find(); }   // walks up from AppContext.BaseDirectory to the folder containing TotalWarModTool.slnx
```

Festlegungen:
- JSON: camelCase, eingerückt, Enums als Text. `schemaVersion` ist die erste Eigenschaft des Wurzelobjekts, daneben liegen die Eigenschaften von `T`.
- `Load`: Datei fehlt → `Missing`, Default, nichts wird angelegt. Kein gültiges JSON, kein Objekt, `schemaVersion` fehlt oder ist keine Zahl, `T` lässt sich nicht lesen → Kopie nach `path + ".bad"` (überschreibt eine alte), `Corrupt`, Default. `schemaVersion` größer als `currentSchemaVersion` → bekannte Felder lesen, `NewerVersion`, keine `.bad`. `IOException` oder `UnauthorizedAccessException` beim Lesen → `Unreadable`, Default, keine `.bad`.
- `Save` schreibt über `AtomicFile.WriteAllText`.

- [ ] **Step 1: Gerüst anlegen**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>12.0</LangVersion>
    <Version>0.1.0</Version>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

`tests/Directory.Build.props`:

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <Using Include="Xunit" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared\*.cs" LinkBase="Shared" />
  </ItemGroup>
</Project>
```

Projektdateien enthalten nur `TargetFramework` und Referenzen. Solution: `dotnet new sln -n TotalWarModTool` (erzeugt `TotalWarModTool.slnx`), danach beide Projekte mit `dotnet sln TotalWarModTool.slnx add` aufnehmen.

`build.bat` (ebenso `test.bat` mit `dotnet test TotalWarModTool.slnx`):

```bat
@echo off
setlocal
cd /d "%~dp0"
dotnet build TotalWarModTool.slnx
set RESULT=%ERRORLEVEL%
rem Pause only when started by double-click, so the window stays readable.
echo %cmdcmdline% | find /i "%~nx0" >nul && pause
exit /b %RESULT%
```

`README.md` (Englisch): Name, ein Satz Zweck, Befehle `build.bat` / `test.bat` / `run.bat`, Lizenz MIT.
`CHANGELOG.md`: Überschrift `# Changelog`, Abschnitt `## Unreleased` mit den leeren Unterabschnitten `### Neu`, `### Geändert`, `### Behoben`.

- [ ] **Step 2: Failing Tests schreiben**

```csharp
public class AtomicFileTests
{
    [Fact] public void WriteAllText_creates_missing_directory_and_file()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a", "b", "f.json");
        AtomicFile.WriteAllText(path, "x");
        Assert.Equal("x", File.ReadAllText(path));
    }

    [Fact] public void WriteAllText_replaces_content_and_leaves_no_temp_files()
    {
        using var dir = new TempDir();
        AtomicFile.WriteAllText(dir.File("f.json"), "old");
        AtomicFile.WriteAllText(dir.File("f.json"), "new");
        Assert.Equal("new", File.ReadAllText(dir.File("f.json")));
        Assert.Equal(["f.json"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }

    [Fact] public async Task WriteAllText_retries_while_target_is_locked()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("f.json"), "old");
        var locked = new FileStream(dir.File("f.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () => { await Task.Delay(150); locked.Dispose(); });
        AtomicFile.WriteAllText(dir.File("f.json"), "new");
        await release;
        Assert.Equal("new", File.ReadAllText(dir.File("f.json")));
    }

    [Fact] public void WriteAllText_throws_and_removes_temp_when_target_stays_locked()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("f.json"), "old");
        using (new FileStream(dir.File("f.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(dir.File("f.json"), "new"));
        Assert.Equal(["f.json"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
        Assert.Equal("old", File.ReadAllText(dir.File("f.json")));
    }
}

public class JsonFileStoreTests
{
    public sealed record Sample(string Name, int Count);
    private static Sample Default() => new("default", 0);

    [Fact] public void Save_then_Load_round_trips_and_writes_schemaVersion_first()
    {
        using var dir = new TempDir();
        JsonFileStore.Save(dir.File("s.json"), 1, new Sample("a", 2));
        var text = File.ReadAllText(dir.File("s.json"));
        Assert.True(text.IndexOf("\"schemaVersion\": 1") < text.IndexOf("\"name\": \"a\""));
        var result = JsonFileStore.Load(dir.File("s.json"), 1, Default);
        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Equal(new Sample("a", 2), result.Value);
        Assert.Null(result.BadCopyPath);
    }

    [Fact] public void Load_missing_file_returns_default_and_creates_nothing()
    {
        using var dir = new TempDir();
        var result = JsonFileStore.Load(dir.File("s.json"), 1, Default);
        Assert.Equal((LoadStatus.Missing, Default()), (result.Status, result.Value));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Theory]
    [InlineData("{ not json")]                                   // invalid JSON
    [InlineData("[1,2]")]                                        // not an object
    [InlineData("{\"name\":\"a\",\"count\":2}")]                 // schemaVersion missing
    [InlineData("{\"schemaVersion\":\"x\",\"name\":\"a\"}")]     // schemaVersion not a number
    [InlineData("{\"schemaVersion\":1,\"name\":\"a\",\"count\":\"many\"}")]   // wrong property type
    public void Load_corrupt_file_copies_to_bad_and_returns_default(string content)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.json"), content);
        File.WriteAllText(dir.File("s.json.bad"), "older bad copy");
        var result = JsonFileStore.Load(dir.File("s.json"), 1, Default);
        Assert.Equal((LoadStatus.Corrupt, Default()), (result.Status, result.Value));
        Assert.Equal(dir.File("s.json.bad"), result.BadCopyPath);
        Assert.Equal(content, File.ReadAllText(dir.File("s.json.bad")));
        Assert.Equal(content, File.ReadAllText(dir.File("s.json")));   // original stays until the next save
    }

    [Fact] public void Load_newer_schemaVersion_reads_known_fields_without_bad_copy()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.json"), "{\"schemaVersion\":7,\"name\":\"a\",\"count\":2,\"future\":true}");
        var result = JsonFileStore.Load(dir.File("s.json"), 1, Default);
        Assert.Equal((LoadStatus.NewerVersion, new Sample("a", 2)), (result.Status, result.Value));
        Assert.False(File.Exists(dir.File("s.json.bad")));
    }

    [Fact] public void Load_locked_file_is_unreadable_without_bad_copy()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.json"), "{\"schemaVersion\":1,\"name\":\"a\",\"count\":2}");
        using var locked = new FileStream(dir.File("s.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = JsonFileStore.Load(dir.File("s.json"), 1, Default);
        Assert.Equal((LoadStatus.Unreadable, Default()), (result.Status, result.Value));
        Assert.False(File.Exists(dir.File("s.json.bad")));
    }
}
```

- [ ] **Step 3: RED prüfen**

Run: `dotnet test tests/TotalWarModTool.Storage.Tests`
Expected: Build-Fehler, `AtomicFile` und `JsonFileStore` existieren nicht.

- [ ] **Step 4: `AtomicFile` und `JsonFileStore` implementieren** (Signaturen und Festlegungen siehe oben)

- [ ] **Step 5: GREEN prüfen**

Run: `dotnet test TotalWarModTool.slnx`
Expected: alle Tests bestanden, 0 Warnungen.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add solution skeleton and Storage library"
```

---

### Task 2: `Logging`

**Files:**
- Create: `src/TotalWarModTool.Logging/TotalWarModTool.Logging.csproj`, `IAppLog.cs`, `FileLog.cs`, `PathRedactor.cs`
- Create: `tests/Shared/FakeTimeProvider.cs`
- Test: `tests/TotalWarModTool.Logging.Tests/…csproj`, `FileLogTests.cs`, `PathRedactorTests.cs`

**Interfaces:**
- Consumes: nichts.
- Produces:

```csharp
namespace TotalWarModTool.Logging;

public interface IAppLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public static class PathRedactor
{
    // Replaces every occurrence of userProfilePath (case-insensitive) with "%USERPROFILE%". Null/empty profile: text unchanged.
    public static string Redact(string text, string? userProfilePath);
}

public sealed class FileLog : IAppLog
{
    public const string FileName = "twmt.log";
    public FileLog(string directory, TimeProvider time, string? userProfilePath, long maxFileBytes = 1_048_576, int maxFiles = 5);
    public string Directory { get; }
    public string CurrentFilePath { get; }   // Directory\twmt.log
}
```

```csharp
// tests/Shared
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
```

Festlegungen:
- Zeile: `yyyy-MM-dd HH:mm:ss.fff [INFO] text` (ebenso `[WARN]`, `[ERROR]`), Zeit aus `time.GetLocalNow()`. Bei `Error` mit Exception folgt `exception.ToString()` in den nächsten Zeilen.
- Jede Nachricht und jeder Exception-Text läuft durch `PathRedactor`.
- Rotation vor dem Schreiben, wenn die Datei mit der neuen Zeile `maxFileBytes` überschreitet: `twmt.log` → `twmt.1.log` → … Es bleiben höchstens `maxFiles` Dateien (aktuelle plus Archive).
- Thread-sicher über ein Lock. `FileLog` wirft nie: `IOException` und `UnauthorizedAccessException` beim Anlegen des Ordners, Rotieren oder Schreiben werden verschluckt (ein Log kann seinen eigenen Ausfall nicht loggen).

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class PathRedactorTests
{
    [Theory]
    [InlineData(@"open C:\Users\alice\AppData\x.json", @"C:\Users\alice", @"open %USERPROFILE%\AppData\x.json")]
    [InlineData(@"open c:\users\ALICE\x", @"C:\Users\alice", @"open %USERPROFILE%\x")]
    [InlineData(@"open D:\Games\data", @"C:\Users\alice", @"open D:\Games\data")]
    [InlineData(@"open C:\Users\alice\x", null, @"open C:\Users\alice\x")]
    public void Redact_replaces_profile_path(string text, string? profile, string expected) =>
        Assert.Equal(expected, PathRedactor.Redact(text, profile));
}

public class FileLogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 14, 3, 7, 123, TimeSpan.Zero);

    [Fact] public void Info_writes_timestamped_line()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), null);
        log.Info("started");
        Assert.Equal(["2026-10-02 14:03:07.123 [INFO] started"], File.ReadAllLines(log.CurrentFilePath));
    }

    [Fact] public void Error_writes_exception_and_redacts_profile_path()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), @"C:\Users\alice");
        log.Error(@"save failed for C:\Users\alice\s.json", new IOException(@"denied: C:\Users\alice\s.json"));
        var text = File.ReadAllText(log.CurrentFilePath);
        Assert.Contains(@"[ERROR] save failed for %USERPROFILE%\s.json", text);
        Assert.Contains(@"System.IO.IOException: denied: %USERPROFILE%\s.json", text);
        Assert.DoesNotContain("alice", text);
    }

    [Fact] public void Rotates_and_keeps_at_most_maxFiles()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), null, maxFileBytes: 200, maxFiles: 3);
        for (var i = 0; i < 40; i++) log.Info("line " + i.ToString("D3") + new string('x', 40));
        var names = Directory.GetFiles(dir.Path).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(["twmt.1.log", "twmt.2.log", "twmt.log"], names);
        Assert.All(Directory.GetFiles(dir.Path), f => Assert.True(new FileInfo(f).Length <= 200));
        Assert.Contains("line 039", File.ReadAllText(log.CurrentFilePath));
    }

    [Fact] public void Does_not_throw_when_directory_cannot_be_created()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("blocked"), "");                  // a file where the log folder should be
        var log = new FileLog(dir.File("blocked"), new FakeTimeProvider(T0), null);
        log.Info("x"); log.Error("y", new Exception("z"));
    }

    [Fact] public void Does_not_throw_when_log_file_is_locked()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), null);
        log.Info("first");
        using (new FileStream(log.CurrentFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) log.Info("lost");
        log.Info("third");
        Assert.Contains("third", File.ReadAllText(log.CurrentFilePath));
    }

    [Fact] public void Concurrent_writes_produce_complete_lines()
    {
        using var dir = new TempDir();
        var log = new FileLog(dir.Path, new FakeTimeProvider(T0), null);
        Parallel.For(0, 8, t => { for (var i = 0; i < 200; i++) log.Info($"t{t} i{i}"); });
        var lines = File.ReadAllLines(log.CurrentFilePath);
        Assert.Equal(1600, lines.Length);
        Assert.All(lines, l => Assert.Matches(@"^2026-10-02 14:03:07\.123 \[INFO\] t\d i\d+$", l));
    }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.Logging.Tests` – Expected: Build-Fehler, Typen fehlen.
- [ ] **Step 3: `PathRedactor`, `IAppLog`, `FileLog` implementieren**
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx` – Expected: alles bestanden.
- [ ] **Step 5: Commit** – `git commit -m "feat: add rotating file log with path redaction"`

---

### Task 3: `Localization`

**Files:**
- Create: `src/TotalWarModTool.Localization/…csproj`, `UiText.cs`, `LocaleFileReader.cs`, `LanguageCatalog.cs`, `TextsBase.cs`, `LocaleCheck.cs`
- Test: `tests/TotalWarModTool.Localization.Tests/…csproj`, `UiTextTests.cs`, `LanguageCatalogTests.cs`, `TextsBaseTests.cs`, `LocaleCheckTests.cs`

**Interfaces:**
- Consumes: nichts.
- Produces:

```csharp
namespace TotalWarModTool.Localization;

public sealed record LocaleParseError(long Line, long Position, string Message);          // both 1-based
public sealed record LocaleFile(IReadOnlyDictionary<string, string> Texts, LocaleParseError? Error);
public static class LocaleFileReader { public static LocaleFile Read(string path); }      // missing/unreadable file: empty, no error

public sealed class UiText
{
    public const string ReferenceLanguage = "en";
    public UiText(string localesDirectory);
    public string Language { get; }                 // "en" before the first Load
    public LocaleParseError? LoadError { get; }     // JSON error of the chosen language file
    public event EventHandler? Changed;             // raised at the end of every Load
    public void Load(string? language);             // null/blank -> "en"; value is trimmed and lower-cased
    public string Get(string key, string fallback);
    public string Format(string key, string fallback, params object?[] args);
}

public sealed record UiLanguageOption(string Code, string Name);
public static class LanguageCatalog
{
    public static IReadOnlyList<UiLanguageOption> Find(string localesDirectory);
    public static string DisplayName(string code);  // native name with capital first letter ("English", "Deutsch"); unknown code: the code
}

public abstract class TextsBase : System.ComponentModel.INotifyPropertyChanged
{
    protected TextsBase(UiText ui);
    protected string T(string key, string fallback);
    protected string F(string key, string fallback, params object?[] args);
    public IReadOnlyDictionary<string, string> RequestedKeys { get; }   // key -> inline fallback, filled as texts are read
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;  // raised with "" when ui.Changed fires
}

public static class LocaleCheck
{
    public static IReadOnlyList<string> FindProblems(IEnumerable<TextsBase> texts, string localesDirectory, IReadOnlyList<string> completeLanguages);
}
```

Festlegungen:
- Sprachdatei: flaches JSON-Objekt. Keys mit `_` am Anfang sind Kommentare. Nicht-Text-Werte werden ignoriert. Leerer Text zählt als fehlend.
- Kette: gewählte Sprache → `en` → Inline-Fallback.
- `Format`: Löst ein Muster eine `FormatException` aus, gilt das nächste Glied der Kette, zuletzt der Inline-Fallback. Kultur: `CultureInfo.CurrentCulture`.
- `LanguageCatalog.Find`: Codes aus `*.json` (Dateiname klein, nicht mit `_` beginnend). `en` ist immer enthalten und steht vorn, der Rest ordinal sortiert.
- `LocaleCheck.FindProblems` ruft per Reflection jede öffentliche Instanz-Eigenschaft vom Typ `string` und jede öffentliche Instanz-Methode mit Rückgabetyp `string` auf (Argumente: Zahlen 0, Texte leer) und wertet `RequestedKeys` aus. Meldungen, je eine Zeile:
  - `json:<lang>:<line>:<position>` – Datei ist kein gültiges JSON
  - `missing:<lang>:<key>` – Key fehlt oder ist leer
  - `fallback-differs:<key>` – en-Wert weicht vom Inline-Fallback ab
  - `placeholders:<lang>:<key>` – Menge der Platzhalter `{n}` weicht vom Inline-Fallback ab
  - `unused:<lang>:<key>` – Key steht in der Datei, wird aber von keiner Texts-Klasse angefordert

- [ ] **Step 1: Failing Tests schreiben**

Testhelfer in `UiTextTests.cs`: `static string Locales(TempDir dir, params (string Code, string Json)[] files)` legt die Dateien an und gibt den Ordner zurück.

```csharp
public class UiTextTests
{
    private const string En = "{\"a\":\"A-en\",\"b\":\"B-en\",\"n\":\"{0} of {1}\",\"_note\":\"comment\"}";
    private const string De = "{\"a\":\"A-de\",\"b\":\"\",\"n\":\"{0} von {5}\"}";

    [Fact] public void Before_Load_returns_inline_fallback_and_language_en()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En)));
        Assert.Equal(("en", "Fallback"), (ui.Language, ui.Get("a", "Fallback")));
    }

    [Fact] public void Chain_is_language_then_english_then_inline()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En), ("de", De)));
        ui.Load(" DE ");
        Assert.Equal("de", ui.Language);
        Assert.Equal("A-de", ui.Get("a", "x"));          // from de
        Assert.Equal("B-en", ui.Get("b", "x"));          // empty in de -> en
        Assert.Equal("x", ui.Get("missing", "x"));       // nowhere -> inline
        Assert.Equal("x", ui.Get("_note", "x"));         // comment keys are not texts
    }

    [Fact] public void Format_falls_back_when_pattern_is_broken()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En), ("de", De)));
        ui.Load("de");
        Assert.Equal("3 of 9", ui.Format("n", "{0}/{1}", 3, 9));     // de pattern needs {5} -> en
    }

    [Fact] public void Load_of_missing_language_uses_english()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En)));
        ui.Load("fr");
        Assert.Equal("A-en", ui.Get("a", "x"));
        Assert.Null(ui.LoadError);
    }

    [Fact] public void Load_of_invalid_json_reports_error_and_uses_english()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En), ("de", "{\n \"a\": \"A-de\",\n oops")));
        ui.Load("de");
        Assert.Equal("A-en", ui.Get("a", "x"));
        Assert.Equal(3, ui.LoadError!.Line);
    }

    [Fact] public void Load_raises_Changed_and_null_means_english()
    {
        using var dir = new TempDir();
        var ui = new UiText(Locales(dir, ("en", En), ("de", De)));
        var raised = 0; ui.Changed += (_, _) => raised++;
        ui.Load("de"); ui.Load(null);
        Assert.Equal((2, "en"), (raised, ui.Language));
    }
}

public class LanguageCatalogTests
{
    [Fact] public void Find_lists_english_first_then_sorted_and_skips_underscore_files()
    {
        using var dir = new TempDir();
        foreach (var f in new[] { "pl.json", "DE.json", "_glossary.json", "notes.txt" }) File.WriteAllText(dir.File(f), "{}");
        Assert.Equal(["en", "de", "pl"], LanguageCatalog.Find(dir.Path).Select(o => o.Code));
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("de", "Deutsch")]
    [InlineData("xx-nope", "xx-nope")]
    public void DisplayName_is_native_name(string code, string expected) => Assert.Equal(expected, LanguageCatalog.DisplayName(code));
}

public class TextsBaseTests
{
    private sealed class SampleTexts(UiText ui) : TextsBase(ui)
    {
        public string Title => T("sample.title", "Title");
        public string Count(int done, int total) => F("sample.count", "{0} / {1}", done, total);
    }

    [Fact] public void Reads_texts_and_records_requested_keys()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("en.json"), "{\"sample.title\":\"Title\"}");
        var texts = new SampleTexts(new UiText(dir.Path));
        Assert.Equal(("Title", "3 / 9"), (texts.Title, texts.Count(3, 9)));
        Assert.Equal(new Dictionary<string, string> { ["sample.title"] = "Title", ["sample.count"] = "{0} / {1}" }, texts.RequestedKeys);
    }

    [Fact] public void Raises_PropertyChanged_for_all_properties_when_language_changes()
    {
        using var dir = new TempDir();
        var ui = new UiText(dir.Path);
        var texts = new SampleTexts(ui);
        var names = new List<string?>(); texts.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        ui.Load("de");
        Assert.Equal([""], names);
    }
}

public class LocaleCheckTests
{
    private sealed class SampleTexts(UiText ui) : TextsBase(ui)
    {
        public string Title => T("sample.title", "Title");
        public string Count(int done, int total) => F("sample.count", "{0} / {1}", done, total);
    }

    private static IReadOnlyList<string> Check(string en, string de)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("en.json"), en); File.WriteAllText(dir.File("de.json"), de);
        return LocaleCheck.FindProblems([new SampleTexts(new UiText(dir.Path))], dir.Path, ["en", "de"]);
    }

    private const string GoodEn = "{\"sample.title\":\"Title\",\"sample.count\":\"{0} / {1}\"}";
    private const string GoodDe = "{\"sample.title\":\"Titel\",\"sample.count\":\"{0} / {1}\",\"_comment\":\"ok\"}";

    [Fact] public void Complete_files_have_no_problems() => Assert.Empty(Check(GoodEn, GoodDe));

    [Fact] public void Reports_every_problem_kind()
    {
        var problems = Check(
            "{\"sample.title\":\"Heading\",\"sample.count\":\"{0} / {1}\",\"old.key\":\"x\"}",
            "{\"sample.title\":\"\",\"sample.count\":\"{0} von {2}\"}");
        Assert.Equal(
            ["fallback-differs:sample.title", "missing:de:sample.title", "placeholders:de:sample.count", "unused:en:old.key"],
            problems.Order());
    }

    [Fact] public void Reports_invalid_json() => Assert.Contains("json:de:1:3", Check(GoodEn, "{ x"));
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.Localization.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren** (Vorlage PMT `UiText.cs`; Unterschiede: Instanz statt statisch, Referenzsprache `en`, kein User-Ordner)
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx`. Stimmt die Position in `Reports_invalid_json` nicht mit der Ausgabe von `System.Text.Json` überein, den erwarteten Wert im Test an die echte Position angleichen und im Commit vermerken.
- [ ] **Step 5: Commit** – `git commit -m "feat: add localization with fallback chain and locale check"`

---

### Task 4: `Settings`

**Files:**
- Create: `src/TotalWarModTool.Settings/…csproj` (referenziert `Storage`), `GlobalSettings.cs`, `GlobalSettingsStore.cs`, `WindowPlacement.cs`, `WindowBoundsCheck.cs`
- Test: `tests/TotalWarModTool.Settings.Tests/…csproj`, `GlobalSettingsTests.cs`, `GlobalSettingsStoreTests.cs`, `WindowBoundsCheckTests.cs`

**Interfaces:**
- Consumes: `JsonFileStore`, `LoadStatus` (Task 1).
- Produces:

```csharp
namespace TotalWarModTool.Settings;

public enum ThemeChoice { System, Light, Dark }
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);

public sealed class GlobalSettings
{
    public string Language { get; set; }            // default "en"; setter trims, lower-cases, blank -> "en"
    public ThemeChoice Theme { get; set; }          // default System
    public WindowPlacement? MainWindow { get; set; }
    public event EventHandler<string>? Changed;     // argument: property name; only raised when the value really changes
}

public sealed record SettingsLoadResult(GlobalSettings Settings, LoadStatus Status, string? BadCopyPath);

public sealed class GlobalSettingsStore
{
    public const int SchemaVersion = 1;
    public const string FileName = "settings.json";
    public GlobalSettingsStore(string directory);
    public string FilePath { get; }
    public SettingsLoadResult Load();
    public void Save(GlobalSettings settings);
}

public static class WindowBoundsCheck
{
    public static bool IsUsable(WindowPlacement? saved, double screenLeft, double screenTop, double screenWidth, double screenHeight, double minWidth, double minHeight);
}
```

Festlegungen:
- Dateiinhalt: `schemaVersion`, `language`, `theme` (Text), `mainWindow` (Objekt oder `null`).
- Still reparieren beim Laden: unbekannter `theme`-Text → `System`. Leere Sprache → `en`. `mainWindow` mit nicht endlichen Zahlen oder Breite/Höhe ≤ 0 → `null`. Dafür liest der Store ein eigenes DTO mit `theme` als Text.
- `WindowBoundsCheck`: Logik wie PMT. Brauchbar, wenn alle Werte endlich sind, Breite und Höhe mindestens die Mindestwerte haben, der Griffbereich der Titelleiste (die linken `min(Breite, 300)` Pixel) den virtuellen Bildschirm horizontal überlappt und die obersten 40 Pixel vertikal vollständig darin liegen.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class GlobalSettingsTests
{
    [Fact] public void Defaults()
    {
        var s = new GlobalSettings();
        Assert.Equal(("en", ThemeChoice.System, (WindowPlacement?)null), (s.Language, s.Theme, s.MainWindow));
    }

    [Fact] public void Changed_is_raised_with_property_name_only_on_real_change()
    {
        var s = new GlobalSettings(); var names = new List<string>(); s.Changed += (_, n) => names.Add(n);
        s.Language = "en"; s.Language = " DE "; s.Language = "de";
        s.Theme = ThemeChoice.System; s.Theme = ThemeChoice.Dark;
        s.MainWindow = new(1, 2, 800, 600, false); s.MainWindow = new(1, 2, 800, 600, false);
        Assert.Equal(["Language", "Theme", "MainWindow"], names);
        Assert.Equal("de", s.Language);
    }

    [Fact] public void Blank_language_becomes_en() { var s = new GlobalSettings { Language = "de" }; s.Language = "  "; Assert.Equal("en", s.Language); }
}

public class GlobalSettingsStoreTests
{
    [Fact] public void Save_then_Load_round_trips()
    {
        using var dir = new TempDir();
        var store = new GlobalSettingsStore(dir.Path);
        store.Save(new GlobalSettings { Language = "de", Theme = ThemeChoice.Dark, MainWindow = new(10, 20, 1200, 800, true) });
        var r = store.Load();
        Assert.Equal(LoadStatus.Loaded, r.Status);
        Assert.Equal(("de", ThemeChoice.Dark, new WindowPlacement(10, 20, 1200, 800, true)), (r.Settings.Language, r.Settings.Theme, r.Settings.MainWindow));
        Assert.Equal(Path.Combine(dir.Path, "settings.json"), store.FilePath);
    }

    [Fact] public void Load_repairs_unknown_theme_blank_language_and_invalid_placement()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("settings.json"),
            "{\"schemaVersion\":1,\"language\":\" \",\"theme\":\"Neon\",\"mainWindow\":{\"left\":0,\"top\":0,\"width\":-5,\"height\":600,\"maximized\":false}}");
        var r = new GlobalSettingsStore(dir.Path).Load();
        Assert.Equal(LoadStatus.Loaded, r.Status);
        Assert.Equal(("en", ThemeChoice.System, (WindowPlacement?)null), (r.Settings.Language, r.Settings.Theme, r.Settings.MainWindow));
    }

    [Fact] public void Load_corrupt_file_returns_defaults_and_bad_copy()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("settings.json"), "garbage");
        var r = new GlobalSettingsStore(dir.Path).Load();
        Assert.Equal((LoadStatus.Corrupt, "en"), (r.Status, r.Settings.Language));
        Assert.True(File.Exists(dir.File("settings.json.bad")));
    }

    [Fact] public void Load_without_file_returns_defaults() { using var dir = new TempDir(); Assert.Equal(LoadStatus.Missing, new GlobalSettingsStore(dir.Path).Load().Status); }
}

public class WindowBoundsCheckTests
{
    // virtual screen: left 0, top 0, 1920 x 1080; minimum window 400 x 300
    [Theory]
    [InlineData(100, 100, 1200, 800, true)]      // fully visible
    [InlineData(-200, 100, 1200, 800, true)]     // partly left, the title bar grip still reaches into the screen
    [InlineData(1800, 100, 1200, 800, true)]     // partly right, starts inside the screen
    [InlineData(2500, 100, 1200, 800, false)]    // on a monitor that is gone
    [InlineData(-1300, 100, 1200, 800, false)]   // completely left of the screen
    [InlineData(100, -10, 1200, 800, false)]     // title bar above the screen
    [InlineData(100, 1060, 1200, 800, false)]    // title bar below the screen
    [InlineData(100, 100, 300, 800, false)]      // narrower than the minimum
    [InlineData(100, 100, double.NaN, 800, false)]
    public void IsUsable(double left, double top, double width, double height, bool expected) =>
        Assert.Equal(expected, WindowBoundsCheck.IsUsable(new(left, top, width, height, false), 0, 0, 1920, 1080, 400, 300));

    [Fact] public void Null_is_not_usable() => Assert.False(WindowBoundsCheck.IsUsable(null, 0, 0, 1920, 1080, 400, 300));
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.Settings.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren**
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx`
- [ ] **Step 5: Commit** – `git commit -m "feat: add global settings model and store"`

---

### Task 5: App-Hülle mit Theme, Texten und Hauptfenster

**Files:**
- Create: `src/TotalWarModTool.App/TotalWarModTool.App.csproj`, `App.xaml`, `App.xaml.cs`, `AppPaths.cs`, `AppTexts.cs`, `StartupSanitizer.cs`, `SettingsAutoSaver.cs`
- Create: `src/TotalWarModTool.App/Mvvm/ObservableObject.cs`, `Mvvm/RelayCommand.cs`
- Create: `src/TotalWarModTool.App/Theme/ThemePalette.cs`, `Theme/ThemeManager.cs`, `Theme/ISystemThemeSource.cs`, `Theme/RegistrySystemThemeSource.cs`, `Themes/Controls.xaml`
- Create: `src/TotalWarModTool.App/MainWindow.xaml`, `MainWindow.xaml.cs`, `MainViewModel.cs`, `WindowPlacementTracker.cs`
- Create: `locales/en.json`, `locales/de.json`, `run.bat`
- Test: `tests/TotalWarModTool.App.Tests/…csproj` (`net8.0-windows`, `UseWPF`), `Sta.cs`, `FakeAppLog.cs`, `ThemeTests.cs`, `AppTextsTests.cs`, `StartupSanitizerTests.cs`, `MainViewModelTests.cs`, `AppPathsTests.cs`

**Interfaces:**
- Consumes: `UiText`, `TextsBase`, `LanguageCatalog`, `UiLanguageOption`, `LocaleCheck` (Task 3); `GlobalSettings`, `GlobalSettingsStore`, `ThemeChoice`, `WindowPlacement`, `WindowBoundsCheck` (Task 4); `IAppLog`, `FileLog` (Task 2); `LoadStatus` (Task 1).
- Produces:

```csharp
namespace TotalWarModTool.App;

public sealed record AppPaths(string RoamingRoot, string LocalRoot, string ProgramRoot)
{
    public const string AppFolderName = "TotalWarModTool";
    public static AppPaths Default();            // %APPDATA%\TotalWarModTool, %LocalAppData%\TotalWarModTool, AppContext.BaseDirectory
    public string LogDirectory { get; }          // LocalRoot\logs
    public string LocalesDirectory { get; }      // ProgramRoot\locales
}

public sealed class AppTexts(UiText ui) : TextsBase(ui)   // one property or method per UI text; keys below
{
    public const string AppName = "Total War Modding Tool";   // product name, not translated
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null);
    protected void OnPropertyChanged([CallerMemberName] string? name = null);
}
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand { public void RaiseCanExecuteChanged(); }

public sealed record ThemeToken(string Key, string Light, string Dark);
public static class ThemePalette
{
    public const string ResourcePrefix = "Theme.";
    public static IReadOnlyList<ThemeToken> All { get; }
}
public interface ISystemThemeSource { bool IsDark(); }
public sealed class RegistrySystemThemeSource : ISystemThemeSource { }   // HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize, AppsUseLightTheme == 0
public sealed class ThemeManager
{
    public ThemeManager(ResourceDictionary resources, ISystemThemeSource systemTheme);
    public bool IsDark { get; }
    public void Apply(ThemeChoice choice);       // System resolves through systemTheme
}

public static class StartupSanitizer
{
    // Sets settings.Language to "en" when no option has that code. Returns true when it changed something.
    public static bool FixLanguage(GlobalSettings settings, IReadOnlyList<UiLanguageOption> available);
}

// Saves 500 ms after the last settings.Changed (DispatcherTimer), only when there are unsaved changes.
// Never writes in a session whose load status was NewerVersion or Unreadable (logs one warning instead).
public sealed class SettingsAutoSaver
{
    public SettingsAutoSaver(GlobalSettings settings, GlobalSettingsStore store, IAppLog log, LoadStatus loadStatus);
    public void Flush();   // saves now if there are unsaved changes
}

public sealed class MainViewModel : ObservableObject
{
    public MainViewModel(AppTexts texts, AppPaths paths, Action<string> openFolder);
    public AppTexts Texts { get; }
    public ICommand OpenLogFolderCommand { get; }   // calls openFolder(paths.LogDirectory)
}
```

```csharp
// tests/TotalWarModTool.App.Tests
internal static class Sta
{
    // Runs on a fresh STA thread, rethrows what the action throws. Afterwards the dispatcher of that thread is shut down
    // (which also destroys windows left open on it). Throws TimeoutException when the thread has not finished after
    // joinTimeoutSeconds (it is a background thread, so it cannot keep the test host alive).
    public static void Run(Action action, int joinTimeoutSeconds = 30);
    // Installs a DispatcherSynchronizationContext, starts action, pumps a DispatcherFrame until the task ends,
    // rethrows its exception. Throws TimeoutException when the task is not done after timeoutSeconds; the timeout also
    // exits nested dispatcher loops (e.g. a ShowDialog nobody closes), and the dispatcher of the thread is shut down afterwards.
    public static void RunAsync(Func<Task> action, int timeoutSeconds = 10);
}
internal sealed class FakeAppLog : IAppLog { public List<string> Infos, Warnings, Errors; public List<Exception?> Exceptions; }
```

Festlegungen:
- `App.csproj`: `OutputType` `WinExe`, `UseWPF`, `AssemblyName` `TotalWarModTool`, `RootNamespace` `TotalWarModTool.App`. Sprachdateien als Inhalt: `<Content Include="..\..\locales\*.json" Link="locales\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />`.
- Theme-Tokens (Hell / Dunkel), übernommen aus PMT:

| Key | Light | Dark |
|---|---|---|
| `WindowBackground` | `#F0F0F0` | `#1E1F22` |
| `PanelBackground` | `#FFFFFF` | `#2B2D30` |
| `ControlBackground` | `#E1E1E1` | `#3C3F41` |
| `ControlHover` | `#E5F1FB` | `#4A4E52` |
| `ControlPressed` | `#CCE4F7` | `#5A5F63` |
| `ControlBorder` | `#ADADAD` | `#5C6064` |
| `Text` | `#000000` | `#DFE1E5` |
| `TextMuted` | `#808080` | `#9AA0A6` |
| `TextDisabled` | `#8A8A8A` | `#6F747A` |
| `Accent` | `#0078D7` | `#4B9CE2` |
| `AccentText` | `#FFFFFF` | `#FFFFFF` |
| `Selection` | `#CCE8FF` | `#2F4F73` |
| `SelectionText` | `#000000` | `#FFFFFF` |
| `InactiveSelection` | `#E0E0E0` | `#3A3D41` |
| `Separator` | `#A0A0A0` | `#4A4D50` |

- `ThemeManager.Apply` schreibt pro Token einen eingefrorenen `SolidColorBrush` unter `Theme.<Key>` und belegt die Systemfarben wie `MapSystemColors` in PMT.
- `Themes/Controls.xaml`: Kopie der PMT-Datei, Kommentare ins Englische übersetzt. Sie nutzt nur die 14 Tokens oben (ohne `AccentText`). In `App.xaml` als `MergedDictionary` eingebunden.
- `MainWindow`: Titel `{Binding Texts.AppTitle}`, `Background` `Theme.WindowBackground`, `Foreground` `Theme.Text`, Standardgröße 1200 × 800, Mindestgröße 400 × 300, zentriert. Inhalt: nur ein Menü mit dem Eintrag `Tools` → `Open log folder`. Der Rest bleibt leer.
- `WindowPlacementTracker(Window, GlobalSettings)`: wendet beim `SourceInitialized` die gespeicherte Position an, wenn `WindowBoundsCheck.IsUsable` gegen `SystemParameters.VirtualScreen*` mit 400 × 300 zustimmt, und schreibt beim `Closing` `RestoreBounds` plus Maximiert-Zustand nach `settings.MainWindow`.
- Startreihenfolge in `App.OnStartup` (kein `StartupUri`):
  1. `AppPaths.Default()`
  2. `FileLog` (Profilpfad aus `Environment.SpecialFolder.UserProfile`), `Info` mit Version
  3. `UiText` und `AppTexts` anlegen; bis zum `Load` in Schritt 6 liefern sie die Inline-Fallbacks
  4. `GlobalSettingsStore.Load()`; bei `Corrupt`, `Unreadable`, `NewerVersion` ein `Warn` mit Status und `.bad`-Pfad
  5. `SettingsAutoSaver` wird direkt nach Schritt 4 erzeugt (vor `FixLanguage`), damit die Reparatur der Sprache gespeichert wird; `OnExit` ruft `Flush()`
  6. `LanguageCatalog.Find`, `StartupSanitizer.FixLanguage`, `UiText.Load(settings.Language)`
  7. `ThemeManager(Resources, new RegistrySystemThemeSource()).Apply(settings.Theme)` – vor dem ersten Fenster
  8. `MainViewModel`, `MainWindow`, `WindowPlacementTracker`, `Show()`
- `openFolder` im Composition Root: legt den Ordner an und startet ihn mit `Process.Start` und `UseShellExecute = true`.
- `SettingsAutoSaver`: `IOException` und `UnauthorizedAccessException` beim Speichern ergeben ein `Warn`, keinen Absturz.

Texte dieser Task:

| Eigenschaft | Key | en (= Fallback) | de |
|---|---|---|---|
| `AppTitle` | `app.title` | Total War Modding Tool | Total War Modding Tool |
| `MenuTools` | `menu.tools` | Tools | Werkzeuge |
| `MenuOpenLogFolder` | `menu.tools.openLogFolder` | Open log folder | Log-Ordner öffnen |

`run.bat`: wie `build.bat`, mit `dotnet run --project src\TotalWarModTool.App`.

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class ThemeTests
{
    [Fact] public void Palette_has_15_tokens_with_valid_hex_values()
    {
        Assert.Equal(15, ThemePalette.All.Count);
        Assert.All(ThemePalette.All, t => { Assert.Matches("^#[0-9A-F]{6}$", t.Light); Assert.Matches("^#[0-9A-F]{6}$", t.Dark); });
        Assert.Equal(ThemePalette.All.Count, ThemePalette.All.Select(t => t.Key).Distinct().Count());
    }

    [Fact] public void Controls_xaml_references_only_known_tokens()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "TotalWarModTool.App", "Themes", "Controls.xaml"));
        var used = Regex.Matches(xaml, @"Theme\.([A-Za-z]+)").Select(m => m.Groups[1].Value).Distinct();
        Assert.Empty(used.Except(ThemePalette.All.Select(t => t.Key)));
    }

    private sealed class FakeSystemTheme(bool dark) : ISystemThemeSource { public bool IsDark() => dark; }

    [Fact] public void Apply_writes_brushes_and_system_colors() => Sta.Run(() =>
    {
        var res = new ResourceDictionary();
        var manager = new ThemeManager(res, new FakeSystemTheme(false));
        manager.Apply(ThemeChoice.Dark);
        Assert.True(manager.IsDark);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#1E1F22"), ((SolidColorBrush)res["Theme.WindowBackground"]).Color);
        Assert.True(((SolidColorBrush)res["Theme.Text"]).IsFrozen);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#2B2D30"), ((SolidColorBrush)res[SystemColors.WindowBrushKey]).Color);
        manager.Apply(ThemeChoice.Light);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#F0F0F0"), ((SolidColorBrush)res["Theme.WindowBackground"]).Color);
    });

    [Theory]
    [InlineData(true, "#1E1F22")]
    [InlineData(false, "#F0F0F0")]
    public void Apply_System_follows_system_source(bool systemDark, string expected) => Sta.Run(() =>
    {
        var res = new ResourceDictionary();
        new ThemeManager(res, new FakeSystemTheme(systemDark)).Apply(ThemeChoice.System);
        Assert.Equal((Color)ColorConverter.ConvertFromString(expected), ((SolidColorBrush)res["Theme.WindowBackground"]).Color);
    });
}

public class AppTextsTests
{
    private static string Locales => Path.Combine(RepoRoot.Find(), "locales");

    [Fact] public void Locale_files_are_complete() =>
        Assert.Empty(LocaleCheck.FindProblems([new AppTexts(new UiText(Locales))], Locales, ["en", "de"]));

    [Fact] public void German_texts_are_used_after_Load()
    {
        var ui = new UiText(Locales); var texts = new AppTexts(ui);
        ui.Load("de");
        Assert.Equal("Werkzeuge", texts.MenuTools);
    }
}

public class StartupSanitizerTests
{
    private static readonly UiLanguageOption[] Available = [new("en", "English"), new("de", "Deutsch")];

    [Fact] public void FixLanguage_resets_unknown_language_to_en()
    {
        var s = new GlobalSettings { Language = "fr" };
        Assert.True(StartupSanitizer.FixLanguage(s, Available));
        Assert.Equal("en", s.Language);
    }

    [Fact] public void FixLanguage_keeps_known_language()
    {
        var s = new GlobalSettings { Language = "de" };
        Assert.False(StartupSanitizer.FixLanguage(s, Available));
        Assert.Equal("de", s.Language);
    }
}

public class MainViewModelTests
{
    [Fact] public void OpenLogFolder_opens_the_log_directory()
    {
        var paths = new AppPaths(@"X:\roaming", @"X:\local", @"X:\program");
        string? opened = null;
        var vm = new MainViewModel(new AppTexts(new UiText(@"X:\none")), paths, p => opened = p);
        vm.OpenLogFolderCommand.Execute(null);
        Assert.Equal(@"X:\local\logs", opened);
    }
}

public class AppPathsTests
{
    [Fact] public void Default_paths_end_with_the_app_folder()
    {
        var p = AppPaths.Default();
        Assert.EndsWith(@"\TotalWarModTool", p.RoamingRoot); Assert.EndsWith(@"\TotalWarModTool", p.LocalRoot);
        Assert.Equal(Path.Combine(p.ProgramRoot, "locales"), p.LocalesDirectory);
    }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.App.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren**
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx`
- [ ] **Step 5: Von Hand prüfen** – Run: `dotnet run --project src/TotalWarModTool.App`. Erwartet: Fenster „Total War Modding Tool“ im Theme von Windows, Menü `Tools` → `Open log folder` öffnet `%LocalAppData%\TotalWarModTool\logs` mit `twmt.log`. Nach Verschieben, Schließen und Neustart steht das Fenster an derselben Stelle. `%APPDATA%\TotalWarModTool\settings.json` enthält `schemaVersion`.
- [ ] **Step 6: Commit** – `git commit -m "feat: add app shell with theme, texts and main window"`

---

### Task 6: `ProgressRunner`

**Files:**
- Create: `src/TotalWarModTool.Operations/TotalWarModTool.Operations.csproj`, `OperationProgress.cs`
- Create: `src/TotalWarModTool.App/ProgressUi/ProgressRunner.cs`, `ProgressUi/ProgressWindow.xaml`, `ProgressUi/ProgressWindow.xaml.cs`, `ProgressUi/LatestProgress.cs`, `ProgressUi/InputLock.cs`
- Modify: `src/TotalWarModTool.App/AppTexts.cs`, `App.xaml.cs`, `locales/en.json`, `locales/de.json`
- Test: `tests/TotalWarModTool.App.Tests/ProgressRunnerTests.cs`, `LatestProgressTests.cs`, `ProgressWindowTests.cs`

**Interfaces:**
- Consumes: `AppTexts`, `Sta`, `FakeAppLog` (Task 5); `IAppLog` (Task 2).
- Produces:

```csharp
namespace TotalWarModTool.Operations;
// Total == null: amount unknown. Item: the element currently being processed.
public readonly record struct OperationProgress(long Done, long? Total, string? Item);
```

```csharp
namespace TotalWarModTool.App;

public enum OperationStatus { Completed, Cancelled, Failed }
public sealed record OperationOutcome<T>(OperationStatus Status, T? Value, Exception? Error);

public sealed class LatestProgress : IProgress<OperationProgress>
{
    public void Report(OperationProgress value);        // thread-safe; stores only, posts nothing
    public bool TryTake(out OperationProgress value);   // true once per reported change
}

public sealed class InputLock : IDisposable             // blocks mouse and keyboard on the given windows until disposed
{
    public InputLock(IReadOnlyList<Window> windows);
}

public sealed partial class ProgressWindow : Window
{
    public ProgressWindow(string title, AppTexts texts, LatestProgress progress, Action requestCancel);
    public void RequestCancel();                        // same as clicking Cancel
    public void CloseByRunner();                        // the only way the window really closes
    public void Apply(OperationProgress progress);      // updates bar and texts
    public bool IsIndeterminate { get; }
    public double BarMaximum { get; }
    public double BarValue { get; }
    public string ItemText { get; }
    public string CountText { get; }
    public bool CancelEnabled { get; }
}

public sealed class ProgressRunner
{
    public static readonly TimeSpan DefaultShowDelay = TimeSpan.FromMilliseconds(150);
    public ProgressRunner(Func<IReadOnlyList<Window>> openWindows, AppTexts texts, IAppLog log, TimeProvider time,
                          Action<string, Exception> reportError, TimeSpan? showDelay = null);
    public event EventHandler<ProgressWindow>? WindowShown;   // raised on the UI thread when the window is loaded
    // Must be called on the UI thread. Throws InvalidOperationException when another run is active.
    public Task<OperationOutcome<T>> RunAsync<T>(string title, Func<IProgress<OperationProgress>, CancellationToken, T> work);
}
```

Ablauf von `RunAsync` (verbindlich, weil Signatur und Tests ihn nicht eindeutig festlegen):

```text
1. lock = new InputLock(openWindows())            -- from the very start, also during the delay
2. progress = new LatestProgress(); cts = new CancellationTokenSource()
3. task = Task.Run(() => work(progress, cts.Token))   -- starts immediately
4. await Task.WhenAny(task, Task.Delay(showDelay, time))
5. if (!task.IsCompleted):
       window = new ProgressWindow(title, texts, progress, cts.Cancel) { Owner = active window or null }
       window.Loaded += (_, _) =>
       {
           task.ContinueWith(_ => window.CloseByRunner(), TaskScheduler.FromCurrentSynchronizationContext());
           WindowShown?.Invoke(this, window);
       };
       window.ShowDialog();                       -- the close is only registered after Loaded, so Close() can never run before
                                                  -- ShowDialog(). It is registered before WindowShown is raised and posted to the UI
                                                  -- thread, so it still runs after Loaded returns: a throwing subscriber cannot leave
                                                  -- the window open
6. evaluate task:
       returned normally          -> cts cancelled ? Cancelled(value) : Completed(value)
       OperationCanceledException while cts is cancelled -> Cancelled(default)
       OperationCanceledException without a cancel request -> treated like any other exception (Failed)
       any other exception        -> log.Error(...), reportError(title, ex), Failed(default, ex)
7. finally: lock.Dispose(); cts.Dispose(); mark runner idle
```

Festlegungen:
- `InputLock` setzt pro Fenster `IsHitTestVisible = false` und markiert `PreviewKeyDown`, `PreviewKeyUp`, `PreviewTextInput` als behandelt. `Dispose` stellt den vorherigen Wert wieder her und entfernt die Handler. Kein `IsEnabled = false`, damit nichts grau aufblitzt.
- `ProgressWindow`: modal, 460 breit, Höhe nach Inhalt, nicht in der Taskleiste, Titel und fette Titelzeile, Balken, Elementtext, Zähltext, Knopf `Cancel`. Ein `DispatcherTimer` mit 50 ms ruft `TryTake` und `Apply`.
- `Apply`: `Total > 0` → bestimmter Balken (`Maximum = Total`, `Value = Done`), Zähltext aus `ProgressCount(done, total)`. Sonst unbestimmter Balken und leerer Zähltext. Elementtext ist `Item` oder leer.
- Abbrechen (Knopf oder X): ruft `requestCancel`, deaktiviert den Knopf, Elementtext wird `ProgressCancelling`. Das Fenster bleibt offen, bis der Runner es schließt. Schließen durch den Nutzer wird dafür mit `e.Cancel = true` abgewiesen.
- Composition Root: `openWindows` = `Application.Current.Windows`, `reportError` zeigt `MessageBox` mit `OperationFailedTitle` und `OperationFailedMessage(title, ex.Message)`.

Texte dieser Task:

| Eigenschaft / Methode | Key | en (= Fallback) | de |
|---|---|---|---|
| `ProgressCancel` | `progress.cancel` | Cancel | Abbrechen |
| `ProgressCancelling` | `progress.cancelling` | Cancelling | Wird abgebrochen |
| `ProgressCount(long done, long total)` | `progress.count` | {0} / {1} | {0} / {1} |
| `OperationFailedTitle` | `error.operationFailed.title` | Operation failed | Vorgang fehlgeschlagen |
| `OperationFailedMessage(string operation, string reason)` | `error.operationFailed.message` | "{0}" could not be completed: {1} The log contains details (Tools, Open log folder). | „{0}“ konnte nicht abgeschlossen werden: {1} Details stehen im Log (Werkzeuge, Log-Ordner öffnen). |

- [ ] **Step 1: Failing Tests schreiben**

Testhelfer in `ProgressRunnerTests.cs`:

```csharp
private static (ProgressRunner Runner, FakeAppLog Log, List<(string Title, Exception Error)> Reported, List<ProgressWindow> Shown)
    Create(TimeSpan delay, params Window[] windows)
{
    var log = new FakeAppLog(); var reported = new List<(string, Exception)>(); var shown = new List<ProgressWindow>();
    var runner = new ProgressRunner(() => windows, new AppTexts(new UiText(@"X:\none")), log, TimeProvider.System,
        (title, ex) => reported.Add((title, ex)), delay);
    runner.WindowShown += (_, w) => shown.Add(w);
    return (runner, log, reported, shown);
}
private static readonly TimeSpan Never = TimeSpan.FromSeconds(30);
private static readonly TimeSpan Soon = TimeSpan.FromMilliseconds(10);
```

```csharp
[Fact] public void Fast_operation_never_creates_a_window() => Sta.RunAsync(async () =>
{
    var (runner, _, reported, shown) = Create(Never);
    var outcome = await runner.RunAsync("t", (_, _) => 42);
    Assert.Equal((OperationStatus.Completed, 42), (outcome.Status, outcome.Value));
    Assert.Empty(shown); Assert.Empty(reported);
});

[Fact] public void Slow_operation_shows_window_and_closes_it_when_done() => Sta.RunAsync(async () =>
{
    var (runner, _, _, shown) = Create(Soon);
    using var gate = new ManualResetEventSlim();
    runner.WindowShown += (_, _) => gate.Set();
    var outcome = await runner.RunAsync("t", (_, _) => { gate.Wait(); return "done"; });
    Assert.Equal((OperationStatus.Completed, "done"), (outcome.Status, outcome.Value));
    Assert.Single(shown); Assert.False(shown[0].IsVisible);
});

[Fact] public void Exception_before_delay_reports_error_without_window() => Sta.RunAsync(async () =>
{
    var (runner, log, reported, shown) = Create(Never);
    var outcome = await runner.RunAsync<int>("load", (_, _) => throw new InvalidDataException("bad"));
    Assert.Equal(OperationStatus.Failed, outcome.Status);
    Assert.IsType<InvalidDataException>(outcome.Error);
    Assert.Empty(shown);
    Assert.Equal("load", Assert.Single(reported).Title); Assert.Single(log.Errors);
});

[Fact] public void Exception_after_window_shown_closes_window_and_reports_error() => Sta.RunAsync(async () =>
{
    var (runner, _, reported, shown) = Create(Soon);
    using var gate = new ManualResetEventSlim();
    runner.WindowShown += (_, _) => gate.Set();
    var outcome = await runner.RunAsync<int>("load", (_, _) => { gate.Wait(); throw new InvalidDataException("bad"); });
    Assert.Equal(OperationStatus.Failed, outcome.Status);
    Assert.False(Assert.Single(shown).IsVisible); Assert.Single(reported);
});

[Fact] public void Cancel_keeps_partial_result() => Sta.RunAsync(async () =>
{
    var (runner, _, reported, _) = Create(Soon);
    runner.WindowShown += (_, w) => w.RequestCancel();
    var outcome = await runner.RunAsync("t", (_, ct) => { ct.WaitHandle.WaitOne(); return 7; });
    Assert.Equal((OperationStatus.Cancelled, 7), (outcome.Status, outcome.Value));
    Assert.Empty(reported);
});

[Fact] public void Cancel_by_exception_is_not_an_error() => Sta.RunAsync(async () =>
{
    var (runner, log, reported, _) = Create(Soon);
    runner.WindowShown += (_, w) => w.RequestCancel();
    var outcome = await runner.RunAsync<int>("t", (_, ct) => { ct.WaitHandle.WaitOne(); ct.ThrowIfCancellationRequested(); return 0; });
    Assert.Equal(OperationStatus.Cancelled, outcome.Status);
    Assert.Empty(reported); Assert.Empty(log.Errors);
});

[Fact] public void Closing_the_window_requests_cancel_and_window_stays_until_work_ends() => Sta.RunAsync(async () =>
{
    var (runner, _, _, shown) = Create(Soon);
    var stillVisibleAfterClose = false;
    runner.WindowShown += (_, w) => { w.Close(); stillVisibleAfterClose = w.IsVisible; };
    var outcome = await runner.RunAsync("t", (_, ct) => { ct.WaitHandle.WaitOne(); return 1; });
    Assert.True(stillVisibleAfterClose);
    Assert.Equal(OperationStatus.Cancelled, outcome.Status);
    Assert.False(shown[0].IsVisible);
});

[Fact] public void Input_is_locked_while_running_and_restored_in_every_case() => Sta.RunAsync(async () =>
{
    var main = new Window();
    var (runner, _, _, _) = Create(Soon, main);
    using var gate = new ManualResetEventSlim();
    bool? hitTestWhileRunning = null;
    runner.WindowShown += (_, _) => { hitTestWhileRunning = main.IsHitTestVisible; gate.Set(); };
    await runner.RunAsync("slow", (_, _) => { gate.Wait(); return 0; });
    Assert.False(hitTestWhileRunning); Assert.True(main.IsHitTestVisible);
    await runner.RunAsync<int>("fail", (_, _) => throw new IOException("x"));
    Assert.True(main.IsHitTestVisible);
    await runner.RunAsync("fast", (_, _) => 0);
    Assert.True(main.IsHitTestVisible);
});

[Fact] public void Second_run_while_one_is_active_throws() => Sta.RunAsync(async () =>
{
    var (runner, _, _, _) = Create(Never);
    using var gate = new ManualResetEventSlim();
    var first = runner.RunAsync("a", (_, _) => { gate.Wait(); return 0; });
    await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync("b", (_, _) => 0));
    gate.Set(); await first;
});
```

```csharp
public class LatestProgressTests
{
    private sealed class CountingContext : SynchronizationContext { public int Posts; public override void Post(SendOrPostCallback d, object? state) => Posts++; }

    [Fact] public void Flood_of_reports_posts_nothing_to_the_ui_thread()
    {
        var previous = SynchronizationContext.Current;
        var context = new CountingContext(); SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var progress = new LatestProgress();
            Parallel.For(0, 200_000, i => progress.Report(new(i, 200_000, "f" + i)));
            Assert.Equal(0, context.Posts);
            Assert.True(progress.TryTake(out var last)); Assert.Equal(200_000L, last.Total);
            Assert.False(progress.TryTake(out _));        // nothing new since the last take
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
}

public class ProgressWindowTests
{
    [Fact] public void Apply_shows_bar_and_count_when_total_is_known() => Sta.Run(() =>
    {
        var w = new ProgressWindow("t", new AppTexts(new UiText(@"X:\none")), new LatestProgress(), () => { });
        w.Apply(new(120, 4000, "db/units_tables/data__"));
        Assert.Equal((false, 4000d, 120d), (w.IsIndeterminate, w.BarMaximum, w.BarValue));
        Assert.Equal(("db/units_tables/data__", "120 / 4000"), (w.ItemText, w.CountText));
        w.Apply(new(5, null, null));
        Assert.Equal((true, "", ""), (w.IsIndeterminate, w.ItemText, w.CountText));
    });

    [Fact] public void RequestCancel_calls_back_once_and_disables_the_button() => Sta.Run(() =>
    {
        var calls = 0;
        var w = new ProgressWindow("t", new AppTexts(new UiText(@"X:\none")), new LatestProgress(), () => calls++);
        w.RequestCancel(); w.RequestCancel();
        Assert.Equal((1, false, "Cancelling"), (calls, w.CancelEnabled, w.ItemText));
    });
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.App.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren** (Fensteraufbau nach PMT `TaskProgressWindow.cs`, hier als XAML mit Theme-Tokens und gebundenen Texten). `ProgressRunner` im Composition Root erzeugen.
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx`. Die Suite dreimal hintereinander laufen lassen, um Wettlaufprobleme zu finden. Erwartet: dreimal grün.
- [ ] **Step 5: Commit** – `git commit -m "feat: add progress runner with delayed modal window"`

---

### Task 7: Crash-Behandlung und Debug-Menü

**Files:**
- Create: `src/TotalWarModTool.App/Crash/CrashReport.cs`, `Crash/CrashHandler.cs`, `Crash/CrashDialog.xaml`, `Crash/CrashDialog.xaml.cs`
- Modify: `src/TotalWarModTool.App/App.xaml.cs`, `AppTexts.cs`, `MainWindow.xaml`, `MainViewModel.cs`, `locales/en.json`, `locales/de.json`
- Test: `tests/TotalWarModTool.App.Tests/CrashReportTests.cs`, `CrashHandlerTests.cs`

**Interfaces:**
- Consumes: `IAppLog`, `PathRedactor` (Task 2); `AppTexts`, `FakeAppLog`, `MainViewModel` (Task 5); `ProgressRunner` (Task 6).
- Produces:

```csharp
namespace TotalWarModTool.App;

public static class CrashReport
{
    // Lines: "Total War Modding Tool <appVersion>", ISO time, "Source: <source>", OS description, .NET description,
    // empty line, exception.ToString(). The whole text is passed through PathRedactor.
    public static string Build(Exception exception, string source, string appVersion, DateTimeOffset now, string? userProfilePath);
}

public sealed class CrashHandler
{
    public const string SourceUi = "ui", SourceAppDomain = "appdomain", SourceTask = "task";
    public CrashHandler(IAppLog log, TimeProvider time, string appVersion, string? userProfilePath, Action<string> showDialog);
    public void Install(Application app);              // hooks the three global handlers
    public void Handle(Exception exception, string source);
}

public sealed partial class CrashDialog : Window
{
    public CrashDialog(AppTexts texts, string details, Action openLogFolder);
}
```

`MainViewModel` bekommt einen vierten Konstruktorparameter und drei Befehle, die nur im Debug-Build im Menü erscheinen:

```csharp
public MainViewModel(AppTexts texts, AppPaths paths, Action<string> openFolder, ProgressRunner progress);
public ICommand DebugThrowCommand { get; }     // throws InvalidOperationException("Debug test exception")
public ICommand DebugSlowCommand { get; }      // progress.RunAsync: 5 s, 100 steps of 50 ms, reports (i, 100, "step i"), honours the token
public ICommand DebugFailCommand { get; }      // progress.RunAsync: waits 1 s, then throws IOException("Debug failure")
```

Festlegungen:
- `Handle` loggt immer (`log.Error` mit Quelle und Exception). Ist kein Dialog offen, ruft es `showDialog(CrashReport.Build(...))`. Während `showDialog` läuft, lösen weitere Aufrufe nur den Log-Eintrag aus. Wirft `showDialog` selbst, wird das geloggt und nicht weitergereicht.
- `Install`: `DispatcherUnhandledException` → `Handle(…, SourceUi)`, `Handled = true`. `AppDomain.CurrentDomain.UnhandledException` → `Handle(…, SourceAppDomain)`. `TaskScheduler.UnobservedTaskException` → `Handle(…, SourceTask)`, `SetObserved()`.
- `showDialog` im Composition Root: wechselt auf den UI-Thread (`Dispatcher.Invoke`) und zeigt `CrashDialog` modal. `Install` wird direkt nach dem Anlegen der Texte aufgerufen (zwischen Schritt 3 und 4 der Startreihenfolge; davor gibt es nur Pfade und Log), der Dialog nutzt die Texte, sobald sie geladen sind, vorher die Inline-Fallbacks. `DispatcherUnhandledException` setzt `Handled = true`: die App läuft nach dem Dialog weiter (der `ProgressRunner` verlässt sich darauf, weil die Ausnahme eines `WindowShown`-Abonnenten über diesen Weg läuft).
- `CrashDialog`: Meldungstext, schreibgeschütztes Textfeld mit den Details, Knöpfe `Open log`, `Copy details` (Zwischenablage), `Close`. Der Knopf „Fehler melden“ fehlt bewusst (Spec 4.6).
- Menü `Debug` in `MainWindow.xaml` nur im Debug-Build sichtbar (`#if DEBUG` setzt eine Eigenschaft `IsDebugBuild` im ViewModel, das Menü bindet seine Sichtbarkeit daran). Die Befehle selbst gibt es in jedem Build; als Titel der beiden Operationen dienen die Menütexte `MenuDebugSlow` und `MenuDebugFail`.

Texte dieser Task:

| Eigenschaft | Key | en (= Fallback) | de |
|---|---|---|---|
| `CrashTitle` | `crash.title` | Unexpected error | Unerwarteter Fehler |
| `CrashMessage` | `crash.message` | Total War Modding Tool ran into an unexpected error. Your settings are safe. You can keep working, but restarting the app is safer. | Total War Modding Tool ist auf einen unerwarteten Fehler gestoßen. Deine Einstellungen sind sicher. Du kannst weiterarbeiten, ein Neustart der App ist aber sicherer. |
| `CrashOpenLog` | `crash.openLog` | Open log | Log öffnen |
| `CrashCopyDetails` | `crash.copyDetails` | Copy details | Details kopieren |
| `CrashClose` | `crash.close` | Close | Schließen |
| `MenuDebug` | `menu.debug` | Debug | Debug |
| `MenuDebugThrow` | `menu.debug.throw` | Throw test exception | Test-Ausnahme auslösen |
| `MenuDebugSlow` | `menu.debug.slow` | Run slow operation | Langsame Operation starten |
| `MenuDebugFail` | `menu.debug.fail` | Run failing operation | Fehlschlagende Operation starten |

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class CrashReportTests
{
    [Fact] public void Build_contains_version_source_time_and_redacted_exception()
    {
        var ex = new InvalidOperationException(@"cannot read C:\Users\alice\x.json");
        var text = CrashReport.Build(ex, "ui", "0.1.0", new DateTimeOffset(2026, 10, 2, 14, 3, 7, TimeSpan.Zero), @"C:\Users\alice");
        var lines = text.Split(Environment.NewLine);
        Assert.Equal("Total War Modding Tool 0.1.0", lines[0]);
        Assert.Equal("2026-10-02T14:03:07.0000000+00:00", lines[1]);
        Assert.Equal("Source: ui", lines[2]);
        Assert.Contains(@"System.InvalidOperationException: cannot read %USERPROFILE%\x.json", text);
        Assert.DoesNotContain("alice", text);
    }
}

public class CrashHandlerTests
{
    private static CrashHandler Create(FakeAppLog log, Action<string> show) =>
        new(log, new FakeTimeProvider(DateTimeOffset.UnixEpoch), "0.1.0", null, show);

    [Fact] public void Handle_logs_and_shows_dialog_with_details()
    {
        var log = new FakeAppLog(); var shown = new List<string>();
        Create(log, shown.Add).Handle(new IOException("disk"), CrashHandler.SourceTask);
        Assert.Single(log.Errors);
        Assert.Contains("Source: task", Assert.Single(shown)); Assert.Contains("System.IO.IOException: disk", shown[0]);
    }

    [Fact] public void Handle_while_dialog_open_only_logs()
    {
        var log = new FakeAppLog(); var shows = 0; CrashHandler? handler = null;
        handler = Create(log, _ => { shows++; handler!.Handle(new Exception("second"), CrashHandler.SourceUi); });
        handler.Handle(new Exception("first"), CrashHandler.SourceUi);
        Assert.Equal((1, 2), (shows, log.Errors.Count));
        handler.Handle(new Exception("third"), CrashHandler.SourceUi);      // dialog closed again -> shown again
        Assert.Equal(2, shows);
    }

    [Fact] public void Handle_survives_a_failing_dialog()
    {
        var log = new FakeAppLog();
        Create(log, _ => throw new InvalidOperationException("no ui")).Handle(new Exception("x"), CrashHandler.SourceAppDomain);
        Assert.Equal(2, log.Errors.Count);        // the crash itself and the dialog failure
    }
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.App.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren.** `MainViewModelTests` an den neuen Konstruktor anpassen (ein `ProgressRunner` wie im Helfer aus Task 6).
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx`
- [ ] **Step 5: Von Hand prüfen** – Run: `dotnet run --project src/TotalWarModTool.App`.
  - `Debug` → `Throw test exception`: Crash-Dialog erscheint, `Copy details` füllt die Zwischenablage, `Open log` öffnet den Ordner, die App läuft nach `Close` weiter. `twmt.log` enthält den Fehler.
  - `Debug` → `Run slow operation`: Fenster erscheint, Balken läuft bis 100, Zähler „n / 100“, Hauptfenster nimmt keine Klicks und Tasten an. `Cancel` schließt das Fenster zügig.
  - `Debug` → `Run failing operation`: Fenster erscheint, schließt nach etwa 1 s, Fehlermeldung nennt die Operation und den Grund.
- [ ] **Step 6: Commit** – `git commit -m "feat: add global crash handling and debug menu"`

---

### Task 8: Einstellungsfenster

**Files:**
- Create: `src/TotalWarModTool.App/SettingsUi/SettingsViewModel.cs`, `SettingsUi/SettingsWindow.xaml`, `SettingsUi/SettingsWindow.xaml.cs`, `SettingsUi/SettingsWindowService.cs`, `SettingsUi/SettingsApplier.cs`
- Modify: `src/TotalWarModTool.App/App.xaml.cs`, `AppTexts.cs`, `MainWindow.xaml`, `MainViewModel.cs`, `locales/en.json`, `locales/de.json`, `CHANGELOG.md`
- Test: `tests/TotalWarModTool.App.Tests/SettingsViewModelTests.cs`, `SettingsApplierTests.cs`

**Interfaces:**
- Consumes: `GlobalSettings`, `ThemeChoice` (Task 4); `UiText`, `UiLanguageOption` (Task 3); `AppTexts`, `ThemeManager`, `ObservableObject`, `RelayCommand`, `MainViewModel` (Task 5, 7).
- Produces:

```csharp
namespace TotalWarModTool.App;

public sealed record ThemeOption(ThemeChoice Value, string Name);

public sealed class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(GlobalSettings settings, AppTexts texts, IReadOnlyList<UiLanguageOption> languages);
    public AppTexts Texts { get; }
    public IReadOnlyList<UiLanguageOption> Languages { get; }
    public UiLanguageOption SelectedLanguage { get; set; }    // reads and writes settings.Language
    public IReadOnlyList<ThemeOption> ThemeOptions { get; }    // System, Light, Dark; names rebuilt when the language changes
    public ThemeOption SelectedTheme { get; set; }             // reads and writes settings.Theme
}

public sealed class SettingsApplier    // applies setting changes at once
{
    public SettingsApplier(GlobalSettings settings, UiText ui, ThemeManager theme);   // Language -> ui.Load, Theme -> theme.Apply
}

public sealed class SettingsWindowService(Func<SettingsWindow> create)
{
    public void ShowOrActivate();      // one non-modal instance; a second call activates the open window
}
```

`MainViewModel` bekommt einen fünften Konstruktorparameter `Action openSettings` und `public ICommand OpenSettingsCommand { get; }`. Das Menü `Tools` erhält den Eintrag `Settings` vor `Open log folder`.

Festlegungen:
- `SettingsWindow`: nicht modal, Besitzer ist das Hauptfenster, 640 × 420. Links eine Liste der Kategorien (ein Eintrag `General`), rechts die Seite mit zwei beschrifteten Auswahlfeldern: Sprache (zeigt `Name`) und Theme (zeigt `Name`). Kein OK, kein Abbrechen. Jede Änderung wirkt sofort.
- Das ViewModel liest nie aus Controls. Ändert sich `settings` von außen, zieht die Auswahl nach (`settings.Changed`).
- Sprachwechsel wirkt in allen offenen Fenstern sofort, weil `AppTexts` `PropertyChanged("")` meldet.
- `CHANGELOG.md`, Abschnitt `Unreleased` → `Neu`: „Erste startbare Version: Hauptfenster mit hellem und dunklem Theme, Sprache Englisch oder Deutsch, Einstellungsfenster, Log-Ordner im Menü Werkzeuge.“

Texte dieser Task:

| Eigenschaft | Key | en (= Fallback) | de |
|---|---|---|---|
| `MenuSettings` | `menu.tools.settings` | Settings | Einstellungen |
| `SettingsTitle` | `settings.title` | Settings | Einstellungen |
| `SettingsCategoryGeneral` | `settings.category.general` | General | Allgemein |
| `SettingsLanguage` | `settings.language` | Language | Sprache |
| `SettingsTheme` | `settings.theme` | Theme | Theme |
| `SettingsThemeSystem` | `settings.theme.system` | Same as Windows | Wie Windows |
| `SettingsThemeLight` | `settings.theme.light` | Light | Hell |
| `SettingsThemeDark` | `settings.theme.dark` | Dark | Dunkel |

- [ ] **Step 1: Failing Tests schreiben**

```csharp
public class SettingsViewModelTests
{
    private static readonly UiLanguageOption[] Languages = [new("en", "English"), new("de", "Deutsch")];
    private static string Locales => Path.Combine(RepoRoot.Find(), "locales");

    [Fact] public void Selection_reflects_and_writes_settings()
    {
        var settings = new GlobalSettings { Language = "de", Theme = ThemeChoice.Dark };
        var vm = new SettingsViewModel(settings, new AppTexts(new UiText(Locales)), Languages);
        Assert.Equal(("de", ThemeChoice.Dark), (vm.SelectedLanguage.Code, vm.SelectedTheme.Value));
        vm.SelectedLanguage = Languages[0]; vm.SelectedTheme = vm.ThemeOptions.Single(o => o.Value == ThemeChoice.Light);
        Assert.Equal(("en", ThemeChoice.Light), (settings.Language, settings.Theme));
    }

    [Fact] public void Theme_options_are_System_Light_Dark_with_translated_names()
    {
        var ui = new UiText(Locales);
        var vm = new SettingsViewModel(new GlobalSettings(), new AppTexts(ui), Languages);
        Assert.Equal(["Same as Windows", "Light", "Dark"], vm.ThemeOptions.Select(o => o.Name));
        var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        ui.Load("de");
        Assert.Equal(["Wie Windows", "Hell", "Dunkel"], vm.ThemeOptions.Select(o => o.Name));
        Assert.Contains(nameof(SettingsViewModel.ThemeOptions), changed);
        Assert.Contains(nameof(SettingsViewModel.SelectedTheme), changed);
    }

    [Fact] public void Selection_follows_external_settings_change()
    {
        var settings = new GlobalSettings();
        var vm = new SettingsViewModel(settings, new AppTexts(new UiText(Locales)), Languages);
        settings.Language = "de"; settings.Theme = ThemeChoice.Dark;
        Assert.Equal(("de", ThemeChoice.Dark), (vm.SelectedLanguage.Code, vm.SelectedTheme.Value));
    }
}

public class SettingsApplierTests
{
    private sealed class FakeSystemTheme : ISystemThemeSource { public bool IsDark() => false; }

    [Fact] public void Language_and_theme_changes_apply_at_once() => Sta.Run(() =>
    {
        var settings = new GlobalSettings { Theme = ThemeChoice.Light };
        var ui = new UiText(Path.Combine(RepoRoot.Find(), "locales"));
        var res = new ResourceDictionary(); var theme = new ThemeManager(res, new FakeSystemTheme()); theme.Apply(settings.Theme);
        _ = new SettingsApplier(settings, ui, theme);
        settings.Language = "de"; settings.Theme = ThemeChoice.Dark;
        Assert.Equal("de", ui.Language);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#1E1F22"), ((SolidColorBrush)res["Theme.WindowBackground"]).Color);
    });
}
```

- [ ] **Step 2: RED prüfen** – Run: `dotnet test tests/TotalWarModTool.App.Tests` – Expected: Build-Fehler.
- [ ] **Step 3: Implementieren.** `MainViewModelTests` an den neuen Konstruktor anpassen und einen Test `OpenSettings_calls_the_delegate` ergänzen.
- [ ] **Step 4: GREEN prüfen** – Run: `dotnet test TotalWarModTool.slnx`
- [ ] **Step 5: Manuelle Testcheckliste** (Abnahme durch den Nutzer, Ergebnis im Commit-Text oder in der Übergabe festhalten)

  1. `%APPDATA%\TotalWarModTool` und `%LocalAppData%\TotalWarModTool` löschen. `run.bat` starten. Erwartet: englische Oberfläche, Theme wie Windows, kein Aufblitzen im falschen Theme.
  2. `Tools` → `Settings`. Erwartet: Fenster ist nicht modal, das Hauptfenster bleibt bedienbar. Ein zweiter Klick auf `Settings` öffnet kein zweites Fenster.
  3. Sprache auf `Deutsch`. Erwartet: Menü, Einstellungsfenster und Titel der Felder wechseln sofort. Kein Neustart nötig.
  4. Theme auf `Dunkel`, dann `Hell`, dann `Wie Windows`. Erwartet: Beide Fenster wechseln sofort, alle Texte bleiben lesbar.
  5. App schließen und neu starten. Erwartet: Sprache, Theme und Fensterposition sind erhalten.
  6. `settings.json` mit `kaputt` überschreiben, App starten. Erwartet: App startet mit Standardwerten, `settings.json.bad` existiert, das Log enthält eine Warnung.
  7. In `locales` eine Kopie `fr.json` von `en.json` anlegen (im Ausgabeordner). Erwartet: Die Sprachliste zeigt „Français“, Auswahl funktioniert. Datei löschen und neu starten. Erwartet: Sprache fällt auf Englisch zurück.
  8. Fenster auf einen zweiten Monitor schieben, App schließen, Monitor abstecken, App starten. Erwartet: Fenster erscheint auf dem verbliebenen Bildschirm.
  9. Die drei Debug-Einträge aus Task 7 erneut prüfen, einmal im dunklen Theme und in Deutsch.

- [ ] **Step 6: Commit** – `git commit -m "feat: add settings window with live language and theme switch"`
