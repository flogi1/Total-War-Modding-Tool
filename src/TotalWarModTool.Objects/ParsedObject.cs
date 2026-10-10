namespace TotalWarModTool.Objects;

public enum DiagnosticSeverity
{
    Error,
    Warning,
}

public enum DiagnosticKind
{
    UnknownLineForm,
    HeadChanged,
    UnknownField,
    UnknownSection,
    HiddenSection,
    FieldWithoutRow,
    SentenceMismatch,
    FieldRepeated,
    ControlCharacter,
    UnterminatedText,
    RootFieldMissing,
    EmptyText,
    KeyRepeated,
    DuplicateCountExceeded,
    RootKeyChanged,
    InvalidValue,
    ReferenceNotFound,
    TextWithoutOwnerKey,
}

/// <summary>Line is 1-based. Detail is short technical English (the App translates by Kind and shows Detail only in logs).</summary>
public sealed record BlockDiagnostic(int Line, DiagnosticSeverity Severity, DiagnosticKind Kind, string Detail);

public sealed record ParsedField(string Name, string Text, int Line);

public sealed record ParsedText(string Column, string Text, int Line);

/// <summary>Fields hold the key fields and field lines of the row (never the field that points at the root). Texts are its loc lines.</summary>
public sealed record ParsedRow(IReadOnlyList<ParsedField> Fields, IReadOnlyList<ParsedText> Texts, int Line);

/// <summary>FieldName is the field of SourceTable that points at the root (resolved by the parser from the section head and the graph).</summary>
public sealed record ParsedSection(string SourceTable, string FieldName, IReadOnlyList<ParsedRow> Rows, int Line);

public sealed record ParsedObject(ObjectId Id, IReadOnlyList<ParsedField> Root, IReadOnlyList<ParsedText> Texts, IReadOnlyList<ParsedSection> Sections,
    IReadOnlyList<BlockDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
