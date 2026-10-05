namespace TotalWarModTool.Editing;

/// <summary>What a <see cref="RowsDocument"/> holds: a DB table or a loc file.</summary>
public enum DocumentKind { Table, Loc }

/// <summary>Why a cell text is not accepted. ColourChannelOutOfRange: the cell is part of a colour group and is not an integer from 0 to 255.</summary>
public enum CellProblem { None, InvalidValue, ColourChannelOutOfRange }

/// <summary>The outcome of <see cref="RowsDocument.Check"/>. Value is set when Problem is None. Detail is short technical English.</summary>
public sealed record CellCheck(object? Value, CellProblem Problem, string? Detail);

/// <summary>What changed in a document.</summary>
public enum DocumentChangeKind { CellsChanged, RowsInserted, RowsRemoved }

/// <summary>
/// A change of a document, raised after every Do and Undo of an edit. RowIndexes are ascending. For RowsInserted they are the indexes
/// after the insert, for RowsRemoved the indexes before the removal. Column is set for CellsChanged of one column, otherwise -1.
/// </summary>
public sealed record DocumentChange(DocumentChangeKind Kind, IReadOnlyList<int> RowIndexes, int Column);
