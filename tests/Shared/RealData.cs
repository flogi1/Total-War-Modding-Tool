// Root namespace on purpose: resolves unqualified from every TotalWarModTool.<Module>.Tests namespace.
// BCL and xunit only: this file is compiled into every test project, including those that do not reference product libraries.
using System.Diagnostics;
using System.Text;

namespace TotalWarModTool;

/// <summary>Access to real game data for invariant and parity tests. Everything is null or empty when the fixture is missing.</summary>
public static class RealData
{
    private const int RpfmTimeoutMilliseconds = 180_000;
    private const string WorkshopAppId = "1142710";

    /// <summary>TWMT_WH3_DIR when set and its data folder exists, else null.</summary>
    public static string? Wh3Dir
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("TWMT_WH3_DIR");
            return !string.IsNullOrWhiteSpace(dir) && Directory.Exists(Path.Combine(dir, "data")) ? dir : null;
        }
    }

    /// <summary>TWMT_RPFM_CLI when set and the file exists, else null.</summary>
    public static string? RpfmCli
    {
        get
        {
            var file = Environment.GetEnvironmentVariable("TWMT_RPFM_CLI");
            return !string.IsNullOrWhiteSpace(file) && File.Exists(file) ? file : null;
        }
    }

    /// <summary>The data folder of the game; empty when <see cref="Wh3Dir"/> is null.</summary>
    public static string Wh3Data => Wh3Dir is { } dir ? Path.Combine(dir, "data") : string.Empty;

    /// <summary>The workshop folder of the game (library\steamapps\workshop\content\appid) when it exists, else null.</summary>
    public static string? Wh3Workshop
    {
        get
        {
            // Wh3Dir is <library>\steamapps\common\<game folder>.
            var library = Wh3Dir is { } dir
                ? Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dir))))
                : null;
            if (library is null)
            {
                return null;
            }

            var workshop = Path.Combine(library, "steamapps", "workshop", "content", WorkshopAppId);
            return Directory.Exists(workshop) ? workshop : null;
        }
    }

    /// <summary>Extracts a folder of a pack (the folder "/" is the whole pack) with rpfm_cli into <paramref name="destDir"/>.</summary>
    public static void RpfmExtractFolder(string packPath, string folderInPack, string destDir, string? schemaPathForTsv = null)
    {
        var arguments = new List<string> { "--game", "warhammer_3", "pack", "extract", "--pack-path", packPath };
        if (schemaPathForTsv is not null)
        {
            arguments.Add("--tables-as-tsv");
            arguments.Add(schemaPathForTsv);
        }

        arguments.Add("--folder-path");
        arguments.Add($"{folderInPack};{destDir}");
        RunRpfm(destDir, arguments);
    }

    /// <summary>Extracts single files of a pack with rpfm_cli into <paramref name="destDir"/>.</summary>
    public static void RpfmExtractFiles(string packPath, IEnumerable<string> filesInPack, string destDir)
    {
        var arguments = new List<string> { "--game", "warhammer_3", "pack", "extract", "--pack-path", packPath };
        foreach (var file in filesInPack)
        {
            arguments.Add("--file-path");
            arguments.Add($"{file};{destDir}");
        }

        RunRpfm(destDir, arguments);
    }

    /// <summary>Creates an empty pack with rpfm_cli.</summary>
    public static void RpfmCreatePack(string packPath)
    {
        // RunRpfm creates its first argument as a folder, so it gets the folder of the pack.
        RunRpfm(Path.GetDirectoryName(packPath)!, ["--game", "warhammer_3", "pack", "create", "--pack-path", packPath]);
    }

    /// <summary>Adds a folder from disk to a pack with rpfm_cli; TSV files are imported to binary with the schema.</summary>
    public static void RpfmAddFolder(string packPath, string folderOnDisk, string folderInPack, string schemaPathForTsv)
    {
        RunRpfm(Path.GetDirectoryName(packPath)!,
        [
            "--game", "warhammer_3", "pack", "add", "--pack-path", packPath,
            "--tsv-to-binary", schemaPathForTsv, "--folder-path", $"{folderOnDisk};{folderInPack}",
        ]);
    }

    private static void RunRpfm(string destDir, List<string> arguments)
    {
        var cli = RpfmCli ?? throw new InvalidOperationException("TWMT_RPFM_CLI is not set or the file does not exist.");
        Directory.CreateDirectory(destDir);

        var startInfo = new ProcessStartInfo(cli)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var output = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => { lock (output) { output.AppendLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { lock (output) { output.AppendLine(e.Data); } };
        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(RpfmTimeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException($"rpfm_cli did not finish within {RpfmTimeoutMilliseconds / 1000} s and was killed.");
        }

        // The parameterless overload waits until the asynchronous output readers have drained.
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            string text;
            lock (output)
            {
                text = output.ToString();
            }

            throw new InvalidOperationException($"rpfm_cli exited with code {process.ExitCode}: {text}");
        }
    }
}

/// <summary>A fact that is skipped when the game data (TWMT_WH3_DIR) is not available.</summary>
public sealed class RealDataFactAttribute : FactAttribute
{
    public RealDataFactAttribute()
    {
        if (RealData.Wh3Dir is null)
        {
            Skip = "TWMT_WH3_DIR is not set or its data folder does not exist.";
        }
    }
}

/// <summary>A fact that is skipped when the game data or rpfm_cli (TWMT_RPFM_CLI) is not available.</summary>
public sealed class RpfmFactAttribute : FactAttribute
{
    public RpfmFactAttribute()
    {
        if (RealData.Wh3Dir is null)
        {
            Skip = "TWMT_WH3_DIR is not set or its data folder does not exist.";
        }
        else if (RealData.RpfmCli is null)
        {
            Skip = "TWMT_RPFM_CLI is not set or the file does not exist.";
        }
    }
}
