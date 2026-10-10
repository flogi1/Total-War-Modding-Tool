using TotalWarModTool.Objects;
using TotalWarModTool.PackFiles;
using TotalWarModTool.Projects;
using TotalWarModTool.Schema;
using TotalWarModTool.Tables;

namespace TotalWarModTool.Editing;

/// <summary>Computes the files an object's changes are written into. Pure: reads the snapshot, writes nothing.</summary>
public static class ProjectWritePlan
{
    /// <summary>The fallback name part of a new file when the project name is not a valid name (Spec 5.4 rule 3).</summary>
    public const string FallbackFileName = "twmt";

    private const char KeySeparator = '\u0001';

    /// <summary>Pure. Throws ArgumentException when changes.HasErrors. Computes twice in practice: once with WriteAnswers.None to collect the questions, once with the answers.</summary>
    public static WritePlan Compute(ObjectChanges changes, ProjectSnapshot snapshot, GameSchema schema, ReferenceGraph graph, string projectName,
        WriteAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(answers);
        if (changes.HasErrors)
        {
            throw new ArgumentException("The changes have errors and cannot be written.", nameof(changes));
        }

        return new Run(changes, snapshot, schema, graph, projectName ?? string.Empty, answers).Execute();
    }

    /// <summary>The mutable state of one file while the changes are applied.</summary>
    private sealed class FileWork
    {
        public required string RelativeFile { get; init; }

        public required PlannedFileKind Kind { get; init; }

        public required FileFingerprint? Previous { get; init; }

        public required string? PreviousText { get; init; }

        public required string TableName { get; init; }

        public required int Version { get; init; }

        public required string PackPath { get; init; }

        public required TableDefinition Definition { get; init; }

        public required List<object?[]> Rows { get; init; }

        public string? SourceEntry { get; init; }

        /// <summary>The pack a Copy comes from and the row count of its source file (for the question).</summary>
        public TableSource? CopySource { get; init; }

        public int SourceRowCount { get; init; }

        /// <summary>The keys of the rows removed from a Copy, in the order of the changes.</summary>
        public List<string> DeletedKeys { get; } = [];

        public bool Changed { get; set; }

        /// <summary>The rows as text in the layout of the latest definition, parallel to Rows; null where not computed yet.</summary>
        public List<string[]?> Texts { get; } = [];

        /// <summary>The key texts of all rows in the layout of the file; null while stale.</summary>
        public HashSet<string>? KeySet { get; set; }

        public int[]? ToCurrent { get; set; }

        public bool ToCurrentKnown { get; set; }
    }

    /// <summary>The mutable state of one loc file while the text changes are applied.</summary>
    private sealed class LocWork
    {
        public required string RelativeFile { get; init; }

        public required PlannedFileKind Kind { get; init; }

        public required FileFingerprint? Previous { get; init; }

        public required string? PreviousText { get; init; }

        public required string PackPath { get; init; }

        public required List<LocRow> Rows { get; init; }

        public bool Changed { get; set; }
    }

    private sealed class Run
    {
        private readonly ObjectChanges _changes;
        private readonly ProjectSnapshot _snapshot;
        private readonly GameSchema _schema;
        private readonly ReferenceGraph _graph;
        private readonly string _projectName;
        private readonly WriteAnswers _answers;

        private readonly List<WriteError> _errors = [];
        private readonly List<TargetFileQuestion> _targetQuestions = [];
        private readonly List<FileCopyQuestion> _copyQuestions = [];
        private readonly Dictionary<string, FileWork> _work = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LocWork> _locWork = new(StringComparer.OrdinalIgnoreCase);

        public Run(ObjectChanges changes, ProjectSnapshot snapshot, GameSchema schema, ReferenceGraph graph, string projectName, WriteAnswers answers)
        {
            _changes = changes;
            _snapshot = snapshot;
            _schema = schema;
            _graph = graph;
            _projectName = projectName;
            _answers = answers;
        }

        public WritePlan Execute()
        {
            if (_snapshot.Problems.Count > 0)
            {
                foreach (var problem in _snapshot.Problems)
                {
                    _errors.Add(new WriteError(WriteErrorKind.SnapshotProblem, 0, problem.File, problem.Detail));
                }

                return Result([]);
            }

            foreach (var group in _changes.Rows.GroupBy(static r => r.Table, StringComparer.OrdinalIgnoreCase))
            {
                ApplyTable(group.Key, group.ToList());
            }

            ApplyTexts();
            AskForCopies();

            var files = new List<PlannedFile>();
            if (_errors.Count == 0 && _targetQuestions.Count == 0 && _copyQuestions.Count == 0)
            {
                files = Render();
            }

            return Result(files);
        }

        private WritePlan Result(List<PlannedFile> files)
        {
            if (_errors.Count > 0 || _targetQuestions.Count > 0 || _copyQuestions.Count > 0)
            {
                files = [];
            }

            files.Sort(static (a, b) => string.CompareOrdinal(a.RelativeFile, b.RelativeFile));
            var summary = new WriteSummary(
                _changes.Rows.Count(static r => r.Kind == RowChangeKind.Set),
                _changes.Rows.Count(static r => r.Kind == RowChangeKind.Insert),
                _changes.Rows.Count(static r => r.Kind == RowChangeKind.Delete),
                _changes.Texts.Count(static t => t.Kind == TextChangeKind.Set),
                _changes.Texts.Count(static t => t.Kind == TextChangeKind.Insert),
                files.Count(static f => f.Kind == PlannedFileKind.Changed),
                files.Count(static f => f.Kind == PlannedFileKind.Created),
                files.Count(static f => f.Kind == PlannedFileKind.Copy));
            return new WritePlan(files, _targetQuestions, _copyQuestions, _errors, summary);
        }

        /// <summary>One question per copied file, asked once for all copies (the App answers with CopiesConfirmed).</summary>
        private void AskForCopies()
        {
            if (_answers.CopiesConfirmed)
            {
                return;
            }

            foreach (var work in _work.Values.Where(static w => w.Kind == PlannedFileKind.Copy && w.Changed).OrderBy(static w => w.RelativeFile, StringComparer.Ordinal))
            {
                _copyQuestions.Add(new FileCopyQuestion(work.CopySource!, work.SourceEntry!, work.RelativeFile, work.SourceRowCount, work.DeletedKeys));
            }
        }

        private List<PlannedFile> Render()
        {
            var files = new List<PlannedFile>();
            foreach (var work in _work.Values.Where(static w => w.Changed))
            {
                var written = TableTsv.Write(work.TableName, work.Version, work.PackPath, work.Definition, work.Rows);
                if (written.Text is null)
                {
                    _errors.Add(new WriteError(WriteErrorKind.TableNotWritable, 0, work.RelativeFile, written.Detail ?? "the table cannot be written"));
                    continue;
                }

                files.Add(new PlannedFile(work.RelativeFile, work.Kind, work.Previous, work.PreviousText, written.Text, work.SourceEntry));
            }

            foreach (var work in _locWork.Values.Where(static w => w.Changed))
            {
                var written = TableTsv.WriteLoc(work.PackPath, work.Rows);
                if (written.Text is null)
                {
                    _errors.Add(new WriteError(WriteErrorKind.TableNotWritable, 0, work.RelativeFile, written.Detail ?? "the loc file cannot be written"));
                    continue;
                }

                files.Add(new PlannedFile(work.RelativeFile, work.Kind, work.Previous, work.PreviousText, written.Text));
            }

            return files;
        }

        private void Fail(WriteErrorKind kind, int line, string? file, string detail) => _errors.Add(new WriteError(kind, line, file, detail));

        private void ApplyTable(string table, List<RowChange> changes)
        {
            var current = _graph.Latest(table);
            if (current is null)
            {
                foreach (var change in changes)
                {
                    Fail(WriteErrorKind.TableNotWritable, change.Line, null, "the table is not in the schema: " + table);
                }

                return;
            }

            var keyFields = _graph.KeyFields(table);
            var colour = CellText.ColourChannelFields(current);
            var needDefault = changes.Where(NeedsDefaultTarget).ToList();
            var defaultResolved = false;
            FileWork? defaultTarget = null;

            foreach (var change in changes)
            {
                switch (change.Kind)
                {
                    case RowChangeKind.Set when change.Provenance is { Source.Kind: SourceKind.Project } provenance:
                        if (FindProjectFile(provenance.EntryPath) is { } projectFile)
                        {
                            SetProjectRow(GetWork(projectFile), change, current, keyFields, colour);
                        }
                        else
                        {
                            Fail(WriteErrorKind.RowNotInFile, change.Line, null, "the project file of the row is not in the snapshot: " + provenance.EntryPath);
                        }

                        break;
                    case RowChangeKind.Set or RowChangeKind.Insert:
                        if (!defaultResolved)
                        {
                            defaultTarget = ResolveDefaultTarget(table, current, needDefault);
                            defaultResolved = true;
                        }

                        if (defaultTarget is not null)
                        {
                            AppendRow(defaultTarget, change, current, keyFields, colour);
                        }

                        break;
                    default:
                        DeleteRow(change, current, keyFields);
                        break;
                }
            }
        }

        private static bool NeedsDefaultTarget(RowChange change) =>
            change.Kind == RowChangeKind.Insert || (change.Kind == RowChangeKind.Set && change.Provenance is not { Source.Kind: SourceKind.Project });

        private SnapshotTableFile? FindProjectFile(string entryPath)
        {
            const string folder = "pack\\";
            var relative = entryPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ? entryPath[folder.Length..] : entryPath;
            return _snapshot.Tables.FirstOrDefault(f => string.Equals(f.RelativeFile, relative, StringComparison.OrdinalIgnoreCase));
        }

        private FileWork GetWork(SnapshotTableFile file)
        {
            if (_work.TryGetValue(file.RelativeFile, out var existing))
            {
                return existing;
            }

            var work = new FileWork
            {
                RelativeFile = file.RelativeFile,
                Kind = PlannedFileKind.Changed,
                Previous = file.Fingerprint,
                PreviousText = file.Text,
                TableName = file.Table.TableName,
                Version = file.Table.Version,
                PackPath = file.Table.PackPath,
                Definition = file.Table.Definition,
                Rows = [.. file.Table.Rows],
            };
            work.Texts.AddRange(Enumerable.Repeat<string[]?>(null, work.Rows.Count));
            _work[file.RelativeFile] = work;
            return work;
        }

        /// <summary>Spec 5.4: the only project file that is not a copy, else a new file, else the answer of the App.</summary>
        private FileWork? ResolveDefaultTarget(string table, TableDefinition current, List<RowChange> waiting)
        {
            var candidates = _snapshot.Tables
                .Where(f => !f.IsCopy && DbTablePath.TryGetTableName(f.PackPath, out var name) && string.Equals(name, table, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static f => f.RelativeFile, StringComparer.Ordinal)
                .ToList();
            if (candidates.Count == 1)
            {
                return GetWork(candidates[0]);
            }

            var firstLine = waiting.Count > 0 ? waiting[0].Line : 0;
            if (candidates.Count == 0)
            {
                return NewTableFile(table, current, firstLine);
            }

            var answer = _answers.TargetFiles.FirstOrDefault(p => string.Equals(p.Key, table, StringComparison.OrdinalIgnoreCase));
            if (answer.Key is null)
            {
                _targetQuestions.Add(new TargetFileQuestion(table, false, candidates.Select(static f => f.RelativeFile).ToList(), waiting.Select(static c => c.Line).ToList()));
                return null;
            }

            var chosen = candidates.FirstOrDefault(f => string.Equals(f.RelativeFile, answer.Value, StringComparison.OrdinalIgnoreCase));
            if (chosen is null)
            {
                Fail(WriteErrorKind.NoAnswer, firstLine, answer.Value, "the answer is not one of the candidate files of " + table);
                return null;
            }

            return GetWork(chosen);
        }

        /// <summary>The trimmed project name when it is a valid file name part, else the fallback.</summary>
        private string NewFileName()
        {
            var name = _projectName.Trim();
            return NewFiles.NameProblem(name) is null ? name : FallbackFileName;
        }

        private FileWork? NewTableFile(string table, TableDefinition current, int line)
        {
            var name = NewFileName();
            var schemaName = _schema.Tables.Keys.FirstOrDefault(k => string.Equals(k, table, StringComparison.OrdinalIgnoreCase)) ?? table;
            var created = NewFiles.Table(_schema, schemaName, name);
            if (created.File is null)
            {
                Fail(WriteErrorKind.TableNotWritable, line, null, created.Reason ?? "the file cannot be created");
                return null;
            }

            var relativeFile = created.File.RelativeFile;
            if (_work.TryGetValue(relativeFile, out var existing))
            {
                return existing;
            }

            // A file with this name already exists in the project (as a copy): the tool takes it (Spec 5.4).
            if (_snapshot.Tables.FirstOrDefault(f => string.Equals(f.RelativeFile, relativeFile, StringComparison.OrdinalIgnoreCase)) is { } there)
            {
                return GetWork(there);
            }

            var work = new FileWork
            {
                RelativeFile = relativeFile,
                Kind = PlannedFileKind.Created,
                Previous = null,
                PreviousText = null,
                TableName = schemaName,
                Version = current.Version,
                PackPath = $"db/{schemaName}/{name}",
                Definition = current,
                Rows = [],
            };
            _work[relativeFile] = work;
            return work;
        }

        private static int IndexOf(TableDefinition definition, string name)
        {
            for (var i = 0; i < definition.Fields.Count; i++)
            {
                if (string.Equals(definition.Fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool ParseCells(RowChange change, TableDefinition current, string file, out object?[] values)
        {
            values = new object?[current.Fields.Count];
            for (var i = 0; i < values.Length; i++)
            {
                if (i >= change.Cells.Count)
                {
                    Fail(WriteErrorKind.InvalidValue, change.Line, file, $"field '{current.Fields[i].Name}': the cell is missing");
                    return false;
                }

                if (!CellText.TryParse(current.Fields[i].Type, change.Cells[i], out values[i], out var reason))
                {
                    Fail(WriteErrorKind.InvalidValue, change.Line, file, $"field '{current.Fields[i].Name}': {reason}");
                    return false;
                }
            }

            return true;
        }

        /// <summary>Colour channels must pass CellText.IsColourChannelValue: all fields of an insert, the changed fields of a set.</summary>
        private bool ColourChannelsValid(RowChange change, TableDefinition current, IReadOnlySet<int> colour, object?[] values, string file)
        {
            foreach (var index in colour)
            {
                var checkedField = change.Kind == RowChangeKind.Insert
                    || change.ChangedFields.Contains(current.Fields[index].Name, StringComparer.OrdinalIgnoreCase);
                if (checkedField && !CellText.IsColourChannelValue(values[index]))
                {
                    Fail(WriteErrorKind.ColourChannelOutOfRange, change.Line, file,
                        $"field '{current.Fields[index].Name}': {CellText.ShortenForDetail(change.Cells[index])} is not a whole number from 0 to 255");
                    return false;
                }
            }

            return true;
        }

        /// <summary>An override row (Set of a Vanilla or Mod row) or an insert: appended in the layout of the file.</summary>
        private void AppendRow(FileWork work, RowChange change, TableDefinition current, IReadOnlyList<int> keyFields, IReadOnlySet<int> colour)
        {
            if (!ParseCells(change, current, work.RelativeFile, out var values) || !ColourChannelsValid(change, current, colour, values, work.RelativeFile))
            {
                return;
            }

            var mapping = RowLayout.MapFields(work.Definition, current);
            if (mapping is null)
            {
                Fail(WriteErrorKind.TableNotWritable, change.Line, work.RelativeFile, "the table has a field of an unsupported type");
                return;
            }

            var row = RowLayout.Remap(work.Definition, current, mapping, values);
            if (KeyOf(work, current, keyFields, row) is { } key)
            {
                work.KeySet ??= BuildKeySet(work, current, keyFields);
                if (work.KeySet is not null && !work.KeySet.Add(key))
                {
                    Fail(WriteErrorKind.KeyAlreadyInFile, change.Line, work.RelativeFile,
                        $"the key '{key.Replace(KeySeparator, '|')}' is already in the file");
                    return;
                }
            }

            work.Rows.Add(row);
            work.Texts.Add(null);
            work.Changed = true;
        }

        /// <summary>The key of a row in the layout of the file; null when the table has no key or the file lacks a key field.</summary>
        private static string? KeyOf(FileWork work, TableDefinition current, IReadOnlyList<int> keyFields, object?[] row)
        {
            if (keyFields.Count == 0)
            {
                return null;
            }

            var parts = new string[keyFields.Count];
            for (var i = 0; i < parts.Length; i++)
            {
                var index = IndexOf(work.Definition, current.Fields[keyFields[i]].Name);
                if (index < 0)
                {
                    return null;
                }

                parts[i] = CellText.Format(work.Definition.Fields[index].Type, row[index]);
            }

            return string.Join(KeySeparator, parts);
        }

        private static HashSet<string>? BuildKeySet(FileWork work, TableDefinition current, IReadOnlyList<int> keyFields)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in work.Rows)
            {
                if (KeyOf(work, current, keyFields, row) is not { } key)
                {
                    return null;
                }

                set.Add(key);
            }

            return set;
        }

        /// <summary>Spec 5.4 (loc): the file by key, else the only project loc file that is not a copy, else a new file, else the answer of the App.</summary>
        private void ApplyTexts()
        {
            if (_changes.Texts.Count == 0)
            {
                return;
            }

            var holders = new Dictionary<TextChange, SnapshotLocFile>();
            var waiting = new List<TextChange>();
            foreach (var text in _changes.Texts)
            {
                var holder = _snapshot.Locs
                    .OrderBy(static f => f.RelativeFile, StringComparer.Ordinal)
                    .FirstOrDefault(f => f.Loc.Rows.Any(r => string.Equals(r.Key, text.Key, StringComparison.Ordinal)));
                if (holder is null)
                {
                    waiting.Add(text);
                }
                else
                {
                    holders[text] = holder;
                }
            }

            LocWork? target = null;
            var targetResolved = false;
            foreach (var text in _changes.Texts)
            {
                LocWork? work;
                if (holders.TryGetValue(text, out var holder))
                {
                    work = GetLocWork(holder);
                }
                else
                {
                    if (!targetResolved)
                    {
                        target = ResolveLocTarget(waiting);
                        targetResolved = true;
                    }

                    work = target;
                }

                if (work is null)
                {
                    continue;
                }

                var index = work.Rows.FindIndex(r => string.Equals(r.Key, text.Key, StringComparison.Ordinal));
                if (index >= 0)
                {
                    work.Rows[index] = work.Rows[index] with { Text = text.Text };
                }
                else
                {
                    work.Rows.Add(new LocRow(text.Key, text.Text, false));
                }

                work.Changed = true;
            }
        }

        private LocWork GetLocWork(SnapshotLocFile file)
        {
            if (_locWork.TryGetValue(file.RelativeFile, out var existing))
            {
                return existing;
            }

            var work = new LocWork
            {
                RelativeFile = file.RelativeFile,
                Kind = PlannedFileKind.Changed,
                Previous = file.Fingerprint,
                PreviousText = file.Text,
                PackPath = file.Loc.PackPath,
                Rows = [.. file.Loc.Rows],
            };
            _locWork[file.RelativeFile] = work;
            return work;
        }

        private LocWork? ResolveLocTarget(List<TextChange> waiting)
        {
            var candidates = _snapshot.Locs.Where(static f => !f.IsCopy).OrderBy(static f => f.RelativeFile, StringComparer.Ordinal).ToList();
            if (candidates.Count == 1)
            {
                return GetLocWork(candidates[0]);
            }

            var firstLine = waiting.Count > 0 ? waiting[0].Line : 0;
            if (candidates.Count == 0)
            {
                return NewLocFile(firstLine);
            }

            var answer = _answers.TargetFiles.FirstOrDefault(p => string.Equals(p.Key, WriteAnswers.LocKey, StringComparison.Ordinal));
            if (answer.Key is null)
            {
                _targetQuestions.Add(new TargetFileQuestion(WriteAnswers.LocKey, true, candidates.Select(static f => f.RelativeFile).ToList(),
                    waiting.Select(static t => t.Line).ToList()));
                return null;
            }

            var chosen = candidates.FirstOrDefault(f => string.Equals(f.RelativeFile, answer.Value, StringComparison.OrdinalIgnoreCase));
            if (chosen is null)
            {
                Fail(WriteErrorKind.NoAnswer, firstLine, answer.Value, "the answer is not one of the candidate loc files");
                return null;
            }

            return GetLocWork(chosen);
        }

        private LocWork? NewLocFile(int line)
        {
            var name = NewFileName();
            var created = NewFiles.Loc(name);
            if (created.File is null)
            {
                Fail(WriteErrorKind.TableNotWritable, line, null, created.Reason ?? "the loc file cannot be created");
                return null;
            }

            var relativeFile = created.File.RelativeFile;
            if (_locWork.TryGetValue(relativeFile, out var existing))
            {
                return existing;
            }

            // A loc file with this name already exists in the project (as a copy): the tool takes it (Spec 5.4).
            if (_snapshot.Locs.FirstOrDefault(f => string.Equals(f.RelativeFile, relativeFile, StringComparison.OrdinalIgnoreCase)) is { } there)
            {
                return GetLocWork(there);
            }

            var work = new LocWork
            {
                RelativeFile = relativeFile,
                Kind = PlannedFileKind.Created,
                Previous = null,
                PreviousText = null,
                PackPath = ProjectLayout.FromRelativeFile(relativeFile).PackPath,
                Rows = [],
            };
            _locWork[relativeFile] = work;
            return work;
        }

        /// <summary>
        /// Removes the row from its own file and from every file of every layer it overrides: a project file by key, a Vanilla or Mod
        /// file through a copy of that file in the project.
        /// </summary>
        private void DeleteRow(RowChange change, TableDefinition current, IReadOnlyList<int> keyFields)
        {
            var provenance = change.Provenance ?? throw new ArgumentException("A Delete change needs a Provenance.", nameof(change));
            var previous = change.PreviousCells ?? throw new ArgumentException("A Delete change needs PreviousCells.", nameof(change));
            var layers = new List<OverriddenLayer>(provenance.OverriddenLayers.Count + 1) { new(provenance.Source, provenance.EntryPath) };
            layers.AddRange(provenance.OverriddenLayers);

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer.Source.Kind == SourceKind.Project)
                {
                    if (FindProjectFile(layer.EntryPath) is not { } file)
                    {
                        Fail(WriteErrorKind.RowNotInFile, change.Line, null, "the project file of the row is not in the snapshot: " + layer.EntryPath);
                        continue;
                    }

                    var work = GetWork(file);
                    if (!CanMap(work, current, change.Line))
                    {
                        continue;
                    }

                    var index = Locate(work, change, current, keyFields, cellsMatter: i == 0);
                    if (index < 0)
                    {
                        Fail(WriteErrorKind.RowNotInFile, change.Line, work.RelativeFile, "the row is not in the file");
                        continue;
                    }

                    RemoveAt(work, index);
                }
                else if (CopyWork(layer, change.Line) is { } copy && CanMap(copy, current, change.Line))
                {
                    if (!RemoveFromCopy(copy, current, keyFields, previous))
                    {
                        Fail(WriteErrorKind.RowNotInFile, change.Line, copy.RelativeFile, "the row is not in the file");
                    }
                    else if (copy.Kind == PlannedFileKind.Copy)
                    {
                        copy.DeletedKeys.Add(keyFields.Count == 0 ? previous[0] : string.Join('|', keyFields.Select(k => previous[k])));
                    }
                }
            }
        }

        private static void RemoveAt(FileWork work, int index)
        {
            work.Rows.RemoveAt(index);
            work.Texts.RemoveAt(index);
            work.KeySet = null;
            work.Changed = true;
        }

        /// <summary>With key fields every row with the key goes (the game applies the key to all of them); without, the first row that equals the previous cells.</summary>
        private static bool RemoveFromCopy(FileWork work, TableDefinition current, IReadOnlyList<int> keyFields, IReadOnlyList<string> previous)
        {
            var removed = false;
            for (var i = work.Rows.Count - 1; i >= 0; i--)
            {
                var texts = TextsOf(work, i, current)!;
                var matches = keyFields.Count > 0
                    ? keyFields.All(k => string.Equals(texts[k], previous[k], StringComparison.Ordinal))
                    : texts.AsSpan().SequenceEqual(previous.ToArray());
                if (matches)
                {
                    RemoveAt(work, i);
                    removed = true;
                    if (keyFields.Count == 0)
                    {
                        break;
                    }
                }
            }

            return removed;
        }

        /// <summary>The file a deleted row of a Vanilla or Mod layer is removed from: the project file of the same name, else a copy of the pack entry.</summary>
        private FileWork? CopyWork(OverriddenLayer layer, int line)
        {
            var entry = _snapshot.PackEntries.FirstOrDefault(e => string.Equals(e.Source.Path, layer.Source.Path, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.EntryPath, layer.EntryPath, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                Fail(WriteErrorKind.SnapshotProblem, line, layer.EntryPath, "pack entry not in snapshot");
                return null;
            }

            if (_work.TryGetValue(entry.RelativeFile, out var existing))
            {
                return existing;
            }

            if (_snapshot.Tables.FirstOrDefault(f => string.Equals(f.RelativeFile, entry.RelativeFile, StringComparison.OrdinalIgnoreCase)) is { } there)
            {
                return GetWork(there);
            }

            var work = new FileWork
            {
                RelativeFile = entry.RelativeFile,
                Kind = PlannedFileKind.Copy,
                Previous = null,
                PreviousText = null,
                TableName = entry.Table.TableName,
                Version = entry.Table.Version,
                PackPath = entry.Table.PackPath,
                Definition = entry.Table.Definition,
                Rows = [.. entry.Table.Rows],
                SourceEntry = entry.EntryPath,
                CopySource = entry.Source,
                SourceRowCount = entry.Table.Rows.Count,
            };
            work.Texts.AddRange(Enumerable.Repeat<string[]?>(null, work.Rows.Count));
            _work[entry.RelativeFile] = work;
            return work;
        }

        private void SetProjectRow(FileWork work, RowChange change, TableDefinition current, IReadOnlyList<int> keyFields, IReadOnlySet<int> colour)
        {
            if (!ParseCells(change, current, work.RelativeFile, out var values) || !ColourChannelsValid(change, current, colour, values, work.RelativeFile))
            {
                return;
            }

            if (!CanMap(work, current, change.Line))
            {
                return;
            }

            var index = Locate(work, change, current, keyFields, cellsMatter: true);
            if (index < 0)
            {
                Fail(WriteErrorKind.RowNotInFile, change.Line, work.RelativeFile, "the row is not in the file");
                return;
            }

            var row = (object?[])work.Rows[index].Clone();
            foreach (var name in change.ChangedFields)
            {
                var fileIndex = IndexOf(work.Definition, name);
                var currentIndex = IndexOf(current, name);
                if (fileIndex < 0 || currentIndex < 0)
                {
                    Fail(WriteErrorKind.FieldNotInFileVersion, change.Line, work.RelativeFile, $"the field '{name}' is not in version {work.Version} of the file");
                    return;
                }

                var fileType = work.Definition.Fields[fileIndex].Type;
                if (fileType == current.Fields[currentIndex].Type)
                {
                    row[fileIndex] = values[currentIndex];
                }
                else if (CellText.TryParse(fileType, change.Cells[currentIndex], out var converted, out var reason))
                {
                    row[fileIndex] = converted;
                }
                else
                {
                    Fail(WriteErrorKind.InvalidValue, change.Line, work.RelativeFile, $"field '{name}': {reason}");
                    return;
                }
            }

            work.Rows[index] = row;
            work.Texts[index] = null;
            work.KeySet = null;
            work.Changed = true;
        }

        /// <summary>
        /// The index of the row of the change in the file, or -1. With key fields the rows with the key of PreviousCells are the candidates and the
        /// first one that equals PreviousCells wins (else the first candidate); without key fields the first row that equals PreviousCells.
        /// </summary>
        private static int Locate(FileWork work, RowChange change, TableDefinition current, IReadOnlyList<int> keyFields, bool cellsMatter)
        {
            var previous = change.PreviousCells ?? throw new ArgumentException("A Set or Delete change needs PreviousCells.", nameof(change));
            var firstCandidate = -1;
            for (var i = 0; i < work.Rows.Count; i++)
            {
                var texts = TextsOf(work, i, current)!;
                var sameKey = keyFields.Count > 0 && keyFields.All(k => string.Equals(texts[k], previous[k], StringComparison.Ordinal));
                if (keyFields.Count > 0 && !sameKey)
                {
                    continue;
                }

                if (keyFields.Count > 0 && !cellsMatter)
                {
                    return i;
                }

                if (texts.AsSpan().SequenceEqual(previous.ToArray()))
                {
                    return i;
                }

                if (firstCandidate < 0 && sameKey)
                {
                    firstCandidate = i;
                }
            }

            return firstCandidate;
        }

        /// <summary>True when the rows of the file can be read in the layout of the latest definition; otherwise the error is recorded.</summary>
        private bool CanMap(FileWork work, TableDefinition current, int line)
        {
            if (!work.ToCurrentKnown)
            {
                work.ToCurrent = RowLayout.MapFields(current, work.Definition);
                work.ToCurrentKnown = true;
            }

            if (work.ToCurrent is null)
            {
                Fail(WriteErrorKind.TableNotWritable, line, work.RelativeFile, "the table has a field of an unsupported type");
                return false;
            }

            return true;
        }

        /// <summary>The row as text in the layout of the latest definition; null when the layouts cannot be mapped.</summary>
        private static string[]? TextsOf(FileWork work, int index, TableDefinition current)
        {
            if (work.Texts[index] is { } cached)
            {
                return cached;
            }

            if (!work.ToCurrentKnown)
            {
                work.ToCurrent = RowLayout.MapFields(current, work.Definition);
                work.ToCurrentKnown = true;
            }

            if (work.ToCurrent is not { } mapping)
            {
                return null;
            }

            var row = RowLayout.IsIdentity(mapping, current, work.Definition) ? work.Rows[index] : RowLayout.Remap(current, work.Definition, mapping, work.Rows[index]);
            var texts = new string[current.Fields.Count];
            for (var i = 0; i < texts.Length; i++)
            {
                texts[i] = CellText.Format(current.Fields[i].Type, row[i]);
            }

            work.Texts[index] = texts;
            return texts;
        }
    }
}
