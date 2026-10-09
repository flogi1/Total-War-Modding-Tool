using System.Text.RegularExpressions;

namespace TotalWarModTool.Objects;

public sealed record UsedMods(IReadOnlyList<string> WorkingDirectories, IReadOnlyList<string> ModNames);

/// <summary>Parser for the used_mods.txt the game writes into its install folder.</summary>
public static partial class UsedModsFile
{
    // add_working_directory "path"; / mod "name.pack"; (the semicolon is optional, anything after the closing quote is ignored)
    [GeneratedRegex("^(add_working_directory|mod)\\s+\"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex LinePattern();

    /// <summary>Parses lines like add_working_directory "path"; and mod "name.pack"; in file order. Unknown lines are ignored.</summary>
    public static UsedMods Parse(string text)
    {
        var directories = new List<string>();
        var mods = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            var match = LinePattern().Match(rawLine.Trim());
            if (!match.Success)
            {
                continue;
            }

            var value = match.Groups[2].Value;
            if (value.Length == 0)
            {
                continue;
            }

            (match.Groups[1].Value == "mod" ? mods : directories).Add(value);
        }

        return new UsedMods(directories, mods);
    }
}
