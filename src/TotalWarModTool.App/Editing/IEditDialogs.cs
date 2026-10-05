namespace TotalWarModTool.App;

/// <summary>Everything the editing commands ask or tell the user. The fake in the tests records the calls.</summary>
public interface IEditDialogs
{
    /// <summary>Tells the user that something could not be done. Shown as a warning.</summary>
    void ShowMessage(string title, string message);
}
