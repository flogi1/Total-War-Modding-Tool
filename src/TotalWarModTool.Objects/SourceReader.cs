using System.Globalization;
using System.Text;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.Objects;

/// <summary>A problem of one source or entry. Transient problems (locked file) must not be remembered by a cache.</summary>
internal sealed record ReadProblem(SourceProblem Problem, bool Transient);

/// <summary>One readable entry of a source: a pack entry (Index and Entry) or a project file (FullPath).</summary>
internal sealed record EntryRef(string EntryPath, PackIndex? Index, PackEntry? Entry, string? FullPath);

/// <summary>
/// What a source offers for one table or for loc. Token describes the state of exactly the files that matter, so that a cache key built
/// from the tokens changes only when relevant content changes.
/// </summary>
internal sealed record SourceListing(TableSource Source, string Token, IReadOnlyList<EntryRef> Entries, IReadOnlyList<ReadProblem> Problems);

internal sealed record TableEntry(TableDefinition Definition, IReadOnlyList<object?[]> Rows);

/// <summary>
/// Lists and reads the entries of one source (pack or project folder) for a table or for loc. Keeps the pack indexes per pack
/// fingerprint. Reads only; never writes. Only <see cref="OperationCanceledException"/> is thrown for data and file problems.
/// </summary>
internal sealed class SourceReader
{
    private const string TsvExtension = ".tsv";
    private const string LocExtension = ".loc";
    private const string LocTsvExtension = ".loc.tsv";
    private const string DbFolder = "db";
    private const string NoEntriesToken = "-";
    private const string NoFolderToken = "none";

    private sealed record PackData(PackIndex Index, Dictionary<string, List<PackEntry>> Tables, List<PackEntry> Locs);

    private readonly object _gate = new();
    private readonly Dictionary<string, PackData> _packs = new(StringComparer.OrdinalIgnoreCase);

    public void Clear()
    {
        lock (_gate)
        {
            _packs.Clear();
        }
    }

    public SourceListing ListTable(TableSource source, string tableName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tableName);

        if (source.Kind == SourceKind.Project)
        {
            return ListProjectTable(source, tableName);
        }

        var (data, token, problem) = OpenPack(source, cancellationToken);
        if (data is null)
        {
            return new SourceListing(source, token, [], [problem!]);
        }

        if (!data.Tables.TryGetValue(tableName, out var entries))
        {
            return new SourceListing(source, NoEntriesToken, [], []);
        }

        return new SourceListing(source, token, PackRefs(data, entries), []);
    }

    public SourceListing ListLoc(TableSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Kind == SourceKind.Project)
        {
            return ListProjectLoc(source);
        }

        var (data, token, problem) = OpenPack(source, cancellationToken);
        if (data is null)
        {
            return new SourceListing(source, token, [], [problem!]);
        }

        return data.Locs.Count == 0
            ? new SourceListing(source, NoEntriesToken, [], [])
            : new SourceListing(source, token, PackRefs(data, data.Locs), []);
    }

    /// <summary>Reads and decodes one table entry. Exactly one of the two results is set.</summary>
    public (TableEntry? Entry, ReadProblem? Problem) ReadTable(TableSource source, EntryRef entry, string tableName, GameSchema schema,
        CancellationToken cancellationToken)
    {
        if (entry.Index is not null)
        {
            var (bytes, problem) = ReadBytes(source, entry, cancellationToken);
            if (bytes is null)
            {
                return (null, problem);
            }

            var decoded = DbTableDecoder.Decode(bytes, tableName, schema);
            if (decoded.Status == TableDecodeStatus.Ok && decoded.Definition is not null)
            {
                return (new TableEntry(decoded.Definition, decoded.Rows), null);
            }

            var kind = decoded.Status switch
            {
                TableDecodeStatus.TableNotInSchema => SourceProblemKind.NotInSchema,
                TableDecodeStatus.VersionNotInSchema => SourceProblemKind.VersionNotInSchema,
                _ => SourceProblemKind.DecodeFailed,
            };
            return (null, Fail(source, entry, kind, decoded.Detail ?? "The table could not be decoded"));
        }

        var text = ReadText(entry.FullPath!, out var textProblem);
        if (text is null)
        {
            return (null, Fail(source, entry, SourceProblemKind.Unreadable, textProblem!, transient: true));
        }

        var result = TableTsv.Read(text, schema);
        if (result.Table is null)
        {
            return (null, Fail(source, entry, SourceProblemKind.TsvInvalid, Describe(result.Problem!)));
        }

        if (!string.Equals(result.Table.TableName, tableName, StringComparison.OrdinalIgnoreCase))
        {
            return (null, Fail(source, entry, SourceProblemKind.TsvInvalid,
                $"The meta line names the table '{CellText.ShortenForDetail(result.Table.TableName)}' but the file is in the folder of '{tableName}'"));
        }

        return (new TableEntry(result.Table.Definition, result.Table.Rows), null);
    }

    /// <summary>Reads and decodes one loc entry. Exactly one of the two results is set.</summary>
    public (IReadOnlyList<LocRow>? Rows, ReadProblem? Problem) ReadLoc(TableSource source, EntryRef entry, CancellationToken cancellationToken)
    {
        if (entry.Index is not null)
        {
            var (bytes, problem) = ReadBytes(source, entry, cancellationToken);
            if (bytes is null)
            {
                return (null, problem);
            }

            var decoded = LocDecoder.Decode(bytes);
            return decoded.Status == LocDecodeStatus.Ok
                ? (decoded.Rows, null)
                : (null, Fail(source, entry, SourceProblemKind.DecodeFailed, decoded.Detail ?? "The loc file could not be decoded"));
        }

        var text = ReadText(entry.FullPath!, out var textProblem);
        if (text is null)
        {
            return (null, Fail(source, entry, SourceProblemKind.Unreadable, textProblem!, transient: true));
        }

        var result = TableTsv.ReadLoc(text);
        return result.Loc is null
            ? (null, Fail(source, entry, SourceProblemKind.TsvInvalid, Describe(result.Problem!)))
            : (result.Loc.Rows, null);
    }

    private (byte[]? Bytes, ReadProblem? Problem) ReadBytes(TableSource source, EntryRef entry, CancellationToken cancellationToken)
    {
        var content = PackReader.ReadEntry(entry.Index!, entry.Entry!, cancellationToken);
        if (content.Bytes is not null)
        {
            return (content.Bytes, null);
        }

        // A pack that changed under the index must be indexed again next time.
        if (content.Problem == EntryProblem.PackChanged)
        {
            lock (_gate)
            {
                _packs.Remove(source.Path);
            }
        }

        var transient = content.Problem is EntryProblem.Unreadable or EntryProblem.PackChanged;
        var detail = content.Problem.ToString() + (content.Detail is null ? string.Empty : ": " + content.Detail);
        return (null, Fail(source, entry, SourceProblemKind.Unreadable, detail, transient));
    }

    private (PackData? Data, string Token, ReadProblem? Problem) OpenPack(TableSource source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // An empty path names no file; FileFingerprint would throw for it.
        var fingerprint = string.IsNullOrEmpty(source.Path) ? null : FileFingerprint.TryRead(source.Path);
        if (fingerprint is null)
        {
            return (null, "missing", PackProblemOf(source, "The pack file was not found or is not accessible", transient: false));
        }

        lock (_gate)
        {
            if (_packs.TryGetValue(source.Path, out var cached) && SameState(cached.Index.Fingerprint, fingerprint))
            {
                return (cached, TokenOf(cached.Index.Fingerprint), null);
            }
        }

        var result = PackAccess.ReadIndex(source.Path, cancellationToken);
        if (result.Index is null)
        {
            var detail = result.Problem.ToString() + (result.Detail is null ? string.Empty : ": " + result.Detail);
            return (null, "unreadable|" + TokenOf(fingerprint), PackProblemOf(source, detail, transient: result.Problem == PackProblem.Unreadable));
        }

        var data = Index(result.Index);
        lock (_gate)
        {
            _packs[source.Path] = data;
        }

        return (data, TokenOf(result.Index.Fingerprint), null);
    }

    private static PackData Index(PackIndex index)
    {
        var tables = new Dictionary<string, List<PackEntry>>(StringComparer.OrdinalIgnoreCase);
        var locs = new List<PackEntry>();
        foreach (var entry in index.Entries)
        {
            if (DbTablePath.TryGetTableName(entry.Path, out var table))
            {
                if (!tables.TryGetValue(table, out var list))
                {
                    list = [];
                    tables[table] = list;
                }

                list.Add(entry);
            }
            else if (entry.Path.EndsWith(LocExtension, StringComparison.OrdinalIgnoreCase))
            {
                locs.Add(entry);
            }
        }

        return new PackData(index, tables, locs);
    }

    private static List<EntryRef> PackRefs(PackData data, List<PackEntry> entries) =>
        entries.ConvertAll(e => new EntryRef(e.Path, data.Index, e, null));

    private static bool SameState(FileFingerprint a, FileFingerprint b) =>
        a.Length == b.Length && a.LastWriteTimeUtc == b.LastWriteTimeUtc;

    private static string TokenOf(FileFingerprint fingerprint) =>
        fingerprint.Length.ToString(CultureInfo.InvariantCulture) + "|" + fingerprint.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);

    private static ReadProblem PackProblemOf(TableSource source, string detail, bool transient) =>
        new(new SourceProblem(source, string.Empty, SourceProblemKind.Unreadable, detail), transient);

    private static ReadProblem Fail(TableSource source, EntryRef entry, SourceProblemKind kind, string detail, bool transient = false) =>
        new(new SourceProblem(source, entry.EntryPath, kind, detail), transient);

    private static string Describe(TsvDiagnosis diagnosis) =>
        $"Line {diagnosis.Line.ToString(CultureInfo.InvariantCulture)}: {diagnosis.Detail}";

    // ---- project folders ----

    private static SourceListing ListProjectTable(TableSource source, string tableName)
    {
        // An empty project path would resolve against the working directory.
        if (string.IsNullOrEmpty(source.Path) || !IsPlainName(tableName))
        {
            return new SourceListing(source, NoFolderToken, [], []);
        }

        var packRelative = Path.Combine(DbFolder, tableName);
        var directory = new DirectoryInfo(Path.Combine(source.Path, ProjectFile.PackFolder, packRelative));
        List<(string Relative, string State)> files;
        try
        {
            if (!directory.Exists)
            {
                return new SourceListing(source, NoFolderToken, [], []);
            }

            files = directory.EnumerateFiles()
                .Where(static f => f.Name.EndsWith(TsvExtension, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static f => f.Name, StringComparer.Ordinal)
                .Select(f => (Path.Combine(packRelative, f.Name), StateOf(f)))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SourceListing(source, "unreadable", [], [FolderProblem(source, Path.Combine(ProjectFile.PackFolder, packRelative), ex.Message)]);
        }

        return ProjectListing(source, files, []);
    }

    private static SourceListing ListProjectLoc(TableSource source)
    {
        if (string.IsNullOrEmpty(source.Path))
        {
            return new SourceListing(source, NoFolderToken, [], []);
        }

        var root = new DirectoryInfo(Path.Combine(source.Path, ProjectFile.PackFolder));
        var files = new List<(string Relative, string State)>();
        var problems = new List<ReadProblem>();
        try
        {
            if (!root.Exists)
            {
                return new SourceListing(source, NoFolderToken, [], []);
            }

            Walk(root, string.Empty, source, files, problems);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SourceListing(source, "unreadable", [], [FolderProblem(source, ProjectFile.PackFolder, ex.Message)]);
        }

        files.Sort(static (a, b) => string.CompareOrdinal(a.Relative, b.Relative));
        return ProjectListing(source, files, problems);
    }

    // Folders that are links are not entered; they are reported through the path check like a linked file.
    private static void Walk(DirectoryInfo directory, string relative, TableSource source, List<(string Relative, string State)> files,
        List<ReadProblem> problems)
    {
        foreach (var file in directory.EnumerateFiles())
        {
            if (file.Name.EndsWith(LocTsvExtension, StringComparison.OrdinalIgnoreCase))
            {
                files.Add((Path.Combine(relative, file.Name), StateOf(file)));
            }
        }

        foreach (var sub in directory.EnumerateDirectories())
        {
            var subRelative = Path.Combine(relative, sub.Name);
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var check = PackFolderPaths.Resolve(source.Path, subRelative);
                if (check.Problem != PackPathProblem.None)
                {
                    problems.Add(new ReadProblem(
                        new SourceProblem(source, Path.Combine(ProjectFile.PackFolder, subRelative), SourceProblemKind.Unreadable, Describe(check)),
                        Transient: false));
                }

                continue;
            }

            Walk(sub, subRelative, source, files, problems);
        }
    }

    // Every file goes through the path check; the token lists all files with their state.
    private static SourceListing ProjectListing(TableSource source, List<(string Relative, string State)> files, List<ReadProblem> problems)
    {
        var entries = new List<EntryRef>(files.Count);
        var token = new StringBuilder();
        foreach (var (relative, state) in files)
        {
            token.Append(relative).Append('|').Append(state).Append(';');
            var entryPath = Path.Combine(ProjectFile.PackFolder, relative);
            var check = PackFolderPaths.Resolve(source.Path, relative);
            if (check.Problem == PackPathProblem.None)
            {
                entries.Add(new EntryRef(entryPath, null, null, check.FullPath));
            }
            else
            {
                problems.Add(new ReadProblem(new SourceProblem(source, entryPath, SourceProblemKind.Unreadable, Describe(check)), Transient: false));
            }
        }

        return new SourceListing(source, files.Count == 0 && problems.Count == 0 ? NoEntriesToken : token.ToString(), entries, problems);
    }

    private static ReadProblem FolderProblem(TableSource source, string path, string detail) =>
        new(new SourceProblem(source, path, SourceProblemKind.Unreadable, detail), Transient: true);

    private static string Describe(PackFolderPathCheck check) => check.Problem == PackPathProblem.Link
        ? "The path passes through a link: " + check.LinkPath + (check.Detail is null ? string.Empty : " (" + check.Detail + ")")
        : check.Detail ?? "The path is not a plain file path below the pack folder";

    private static string StateOf(FileInfo file) =>
        file.Length.ToString(CultureInfo.InvariantCulture) + "|" + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);

    private static bool IsPlainName(string name) =>
        name.Length > 0 && name != "." && name != ".." && name.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>UTF-8 text of a project file, read shared with writers and deleters. Null with a short reason when it cannot be read.</summary>
    private static string? ReadText(string path, out string? problem)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            problem = null;
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return null;
        }
    }
}
