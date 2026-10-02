namespace TotalWarModTool.App;

/// <summary>Tells whether Windows currently uses its dark app theme. An interface so tests do not depend on the registry.</summary>
public interface ISystemThemeSource
{
    bool IsDark();
}
