using System.IO;
using System.Security;
using Microsoft.Win32;

namespace TotalWarModTool.App;

/// <summary>Reads the Windows app theme from the registry: dark when <c>AppsUseLightTheme</c> is 0. Light when the value cannot be read.</summary>
public sealed class RegistrySystemThemeSource : ISystemThemeSource
{
    public bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
