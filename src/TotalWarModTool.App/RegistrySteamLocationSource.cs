using System.IO;
using System.Security;
using Microsoft.Win32;
using TotalWarModTool.GameProfiles;

namespace TotalWarModTool.App;

/// <summary>
/// Reads the Steam installation folder from the registry: <c>HKCU\Software\Valve\Steam</c> value <c>SteamPath</c>, else
/// <c>HKLM\SOFTWARE\WOW6432Node\Valve\Steam</c> value <c>InstallPath</c>. Null when neither exists or the registry cannot be read.
/// </summary>
public sealed class RegistrySteamLocationSource : ISteamLocationSource
{
    public string? GetSteamPath() =>
        Read(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath")
        ?? Read(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");

    private static string? Read(RegistryKey root, string subKey, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue(valueName) is string { Length: > 0 } path ? path : null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
