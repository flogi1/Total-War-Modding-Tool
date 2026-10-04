namespace TotalWarModTool.Projects.Tests;

internal static class JunctionHelper
{
    /// <summary>Creates a directory junction; false when that is not possible in this environment (the test then returns early).</summary>
    public static bool TryCreate(string link, string target)
    {
        var info = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add("mklink");
        info.ArgumentList.Add("/J");
        info.ArgumentList.Add(link);
        info.ArgumentList.Add(target);
        try
        {
            using var process = System.Diagnostics.Process.Start(info)!;
            process.StandardInput.Close();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
