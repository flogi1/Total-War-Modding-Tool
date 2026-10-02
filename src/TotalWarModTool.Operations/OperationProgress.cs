namespace TotalWarModTool.Operations;

/// <summary>
/// Payload of <see cref="IProgress{T}"/> for long operations. <paramref name="Total"/> is <see langword="null"/> when the amount is unknown;
/// <paramref name="Item"/> is the element currently being processed.
/// </summary>
public readonly record struct OperationProgress(long Done, long? Total, string? Item);
