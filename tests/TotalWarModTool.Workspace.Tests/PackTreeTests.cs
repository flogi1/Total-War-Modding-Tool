using TotalWarModTool.PackFiles;

namespace TotalWarModTool.Workspace.Tests;

public class PackTreeTests
{
    private static PackEntry E(string path) => new(path, 0, 0, false);
    private static string Dump(PackTreeNode node, int depth = 0) =>
        string.Concat(node.Children.Select(c => new string(' ', depth) + (c.Entry is null ? c.Name + "/" : c.Name) + "\n" + Dump(c, depth + 1)));

    [Fact] public void Folders_come_first_and_names_are_sorted_ignoring_case()
    {
        var root = PackTree.Build([E("script/b.lua"), E("db/x_tables/data__"), E("Zeta.txt"), E("alpha.txt"), E("script/A.lua")], CancellationToken.None);
        Assert.Equal(("", ""), (root.Name, root.Path));
        Assert.Equal("db/\n x_tables/\n  data__\nscript/\n A.lua\n b.lua\nalpha.txt\nZeta.txt\n", Dump(root));
        Assert.Equal("db/x_tables", root.Children[0].Children[0].Path);
        Assert.Equal("db/x_tables/data__", root.Children[0].Children[0].Children[0].Entry!.Path);
    }

    [Fact] public void Odd_paths_do_not_break_the_tree()
    {
        var root = PackTree.Build([E("DB/a"), E("db/b"), E("x//y"), E(""), E("f"), E("f/inner"), E("dup"), E("DUP")], CancellationToken.None);
        Assert.Equal("DB/\n a\n b\nf/\n inner\nx/\n y\ndup\nf\n", Dump(root));
    }

    [Fact] public void Very_deep_path_needs_no_recursion()
    {
        var deep = string.Join('/', Enumerable.Repeat("d", 20000)) + "/file";
        var root = PackTree.Build([E(deep)], CancellationToken.None);
        var node = root; var depth = 0;
        while (node.Children.Count > 0) { node = node.Children[0]; depth++; }
        Assert.Equal((20001, "file"), (depth, node.Name));
    }

    [Fact] public void Cancelled_token_throws() =>
        Assert.Throws<OperationCanceledException>(() => PackTree.Build([E("a")], new CancellationToken(true)));
}
