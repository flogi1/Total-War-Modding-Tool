using TotalWarModTool.PackFiles;
using TotalWarModTool.Workspace;

namespace TotalWarModTool.App;

/// <summary>The index of an open pack and its folder tree. Both are built from the same read, so the tree always matches the index.</summary>
public sealed record PackContent(PackIndex Index, PackTreeNode Tree);
