using TotalWarModTool.Localization;

namespace TotalWarModTool.App;

/// <summary>The UI texts of the app shell: one property per text, each with its English inline fallback.</summary>
public sealed class AppTexts(UiText ui) : TextsBase(ui)
{
    /// <summary>Product name. Not translated.</summary>
    public const string AppName = "Total War Modding Tool";

    public string AppTitle => T("app.title", AppName);

    public string MenuTools => T("menu.tools", "Tools");

    public string MenuOpenLogFolder => T("menu.tools.openLogFolder", "Open log folder");
}
