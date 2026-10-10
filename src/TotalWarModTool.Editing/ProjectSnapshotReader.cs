using System.Text;
using TotalWarModTool.Objects;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Editing;

/// <summary>Reads what the write plan computes from. Reads only; never writes.</summary>
public static class ProjectSnapshotReader
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Reads the project files of the requested tables (every file below pack\db\&lt;table&gt;\ that ProjectFiles.List reports as Table), the loc
    /// files (Kind Loc) when requested, and the requested pack entries. Reads only; throws only for arguments and cancellation.
    /// </summary>
    public static ProjectSnapshot Read(string projectFolder, GameSchema schema, SnapshotRequest request, PackEntryExists isPackEntry,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(isPackEntry);

        var tables = new List<SnapshotTableFile>();
        var locs = new List<SnapshotLocFile>();
        var problems = new List<SnapshotProblem>();

        var list = ProjectFiles.List(projectFolder, cancellationToken);
        foreach (var problem in list.Problems)
        {
            problems.Add(new SnapshotProblem(SnapshotProblemKind.ProjectFileUnreadable, problem.RelativePath, null, problem.Detail));
        }

        foreach (var item in list.Files.OrderBy(static f => f.RelativeFile, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Kind == ProjectFileKind.Table && DbTablePath.TryGetTableName(item.PackPath, out var tableName) && request.Tables.Contains(tableName))
            {
                if (ReadFile(projectFolder, item, problems) is var (fingerprint, text))
                {
                    ReadTable(item, tableName, fingerprint, text, schema, isPackEntry(item.PackPath), tables, problems);
                }
            }
            else if (item.Kind == ProjectFileKind.Loc && request.Loc && ReadFile(projectFolder, item, problems) is var (locFingerprint, locText))
            {
                var result = TableTsv.ReadLoc(locText);
                if (result.Loc is null)
                {
                    problems.Add(Invalid(item.RelativeFile, result.Problem!));
                }
                else
                {
                    locs.Add(new SnapshotLocFile(item.RelativeFile, item.PackPath, locFingerprint, locText, result.Loc, isPackEntry(item.PackPath)));
                }
            }
        }

        var entries = ReadPackEntries(request.PackEntries, schema, problems, cancellationToken);
        return new ProjectSnapshot(tables, locs, entries, problems);
    }

    private static (FileFingerprint Fingerprint, string Text)? ReadFile(string projectFolder, ProjectFileItem item, List<SnapshotProblem> problems)
    {
        var check = PackFolderPaths.Resolve(projectFolder, item.RelativeFile);
        if (check.Problem != PackPathProblem.None)
        {
            problems.Add(new SnapshotProblem(SnapshotProblemKind.ProjectFileUnreadable, item.RelativeFile, null,
                check.Detail ?? "the path passes through a link"));
            return null;
        }

        // The fingerprint is taken before the text: a change in between shows up as a mismatch when the writer checks it.
        var fingerprint = FileFingerprint.TryRead(check.FullPath!);
        if (fingerprint is null)
        {
            problems.Add(new SnapshotProblem(SnapshotProblemKind.ProjectFileUnreadable, item.RelativeFile, null, "the file cannot be read"));
            return null;
        }

        // Strict on purpose: the text is written back by an undo, so it must be the file's bytes, not a lenient reading of them.
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(check.FullPath!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(new SnapshotProblem(SnapshotProblemKind.ProjectFileUnreadable, item.RelativeFile, null, "the file cannot be read: " + ex.Message));
            return null;
        }

        if (bytes.AsSpan().StartsWith(Utf8Bom))
        {
            problems.Add(new SnapshotProblem(SnapshotProblemKind.ProjectFileInvalid, item.RelativeFile, null, "the file starts with a byte order mark"));
            return null;
        }

        try
        {
            return (fingerprint, Utf8.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            problems.Add(new SnapshotProblem(SnapshotProblemKind.ProjectFileUnreadable, item.RelativeFile, null, "the file is not valid UTF-8"));
            return null;
        }
    }

    private static void ReadTable(ProjectFileItem item, string tableName, FileFingerprint fingerprint, string text, GameSchema schema, bool isCopy,
        List<SnapshotTableFile> tables, List<SnapshotProblem> problems)
    {
        var result = TableTsv.Read(text, schema);
        if (result.Table is null)
        {
            problems.Add(Invalid(item.RelativeFile, result.Problem!));
            return;
        }

        if (!string.Equals(result.Table.TableName, tableName, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(new SnapshotProblem(SnapshotProblemKind.ProjectFileInvalid, item.RelativeFile, null,
                $"the meta line names the table '{CellText.ShortenForDetail(result.Table.TableName)}' but the file is in the folder of '{tableName}'"));
            return;
        }

        tables.Add(new SnapshotTableFile(item.RelativeFile, item.PackPath, fingerprint, text, result.Table, isCopy));
    }

    private static SnapshotProblem Invalid(string relativeFile, TsvDiagnosis diagnosis) =>
        new(SnapshotProblemKind.ProjectFileInvalid, relativeFile, null, $"line {diagnosis.Line}: {diagnosis.Detail}");

    private static List<SnapshotPackEntry> ReadPackEntries(IReadOnlyList<PackEntryRequest> requests, GameSchema schema, List<SnapshotProblem> problems,
        CancellationToken cancellationToken)
    {
        var result = new List<SnapshotPackEntry>();
        var indexes = new Dictionary<string, PackIndexResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!indexes.TryGetValue(request.Source.Path, out var read))
            {
                read = PackReader.ReadIndex(request.Source.Path);
                indexes[request.Source.Path] = read;
            }

            if (read.Index is null)
            {
                problems.Add(new SnapshotProblem(SnapshotProblemKind.PackUnreadable, request.EntryPath, request.Source,
                    read.Detail ?? "the pack cannot be read: " + read.Problem));
                continue;
            }

            var entry = read.Index.Find(request.EntryPath);
            if (entry is null)
            {
                problems.Add(new SnapshotProblem(SnapshotProblemKind.PackEntryMissing, request.EntryPath, request.Source, "the pack has no such entry"));
                continue;
            }

            var content = PackReader.ReadEntry(read.Index, entry, cancellationToken);
            if (content.Bytes is null)
            {
                problems.Add(new SnapshotProblem(SnapshotProblemKind.PackEntryUnreadable, request.EntryPath, request.Source,
                    content.Detail ?? "the entry cannot be read: " + content.Problem));
                continue;
            }

            var adopted = NewFiles.Adopt(request.EntryPath, content.Bytes, schema, newName: null);
            if (adopted.File is null || adopted.File.Kind != ProjectFileKind.Table)
            {
                problems.Add(new SnapshotProblem(SnapshotProblemKind.PackEntryNotAdoptable, request.EntryPath, request.Source,
                    adopted.Reason ?? "the entry is not a table"));
                continue;
            }

            var table = TableTsv.Read(Utf8.GetString(adopted.File.Content), schema);
            if (table.Table is null)
            {
                problems.Add(new SnapshotProblem(SnapshotProblemKind.PackEntryNotAdoptable, request.EntryPath, request.Source,
                    $"line {table.Problem!.Line}: {table.Problem.Detail}"));
                continue;
            }

            result.Add(new SnapshotPackEntry(request.Source, request.EntryPath, adopted.File.RelativeFile, table.Table));
        }

        return result;
    }
}
