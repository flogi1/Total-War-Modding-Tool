using TotalWarModTool.Schema;

namespace TotalWarModTool.Tables;

public enum TsvProblem
{
    None, MissingMetaLine, InvalidMetaLine, TableNotInSchema, VersionNotInSchema, UnsupportedFieldType,
    MissingColumn, UnknownColumn, DuplicateColumn, WrongCellCount, InvalidValue,
}

/// <summary>Line is 1-based; Detail is short technical English, not a UI text.</summary>
public sealed record TsvDiagnosis(TsvProblem Problem, int Line, string Detail);

/// <summary>Text is null when the table cannot be written without loss; Detail then says why (row, field).</summary>
public sealed record TsvWriteResult(string? Text, string? Detail);

public sealed record TsvTable(string TableName, int Version, string PackPath, TableDefinition Definition, IReadOnlyList<object?[]> Rows);

/// <summary>Exactly one of <paramref name="Table"/> and <paramref name="Problem"/> is not null.</summary>
public sealed record TsvTableResult(TsvTable? Table, TsvDiagnosis? Problem);

public sealed record TsvLoc(string PackPath, IReadOnlyList<LocRow> Rows);

/// <summary>Exactly one of <paramref name="Loc"/> and <paramref name="Problem"/> is not null.</summary>
public sealed record TsvLocResult(TsvLoc? Loc, TsvDiagnosis? Problem);
