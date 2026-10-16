using System.Runtime.ExceptionServices;

namespace ParadoxModTool.Shell.Tests;

/// <summary>WPF-Steuerelemente brauchen einen STA-Thread; xUnit läuft auf MTA.</summary>
internal static class Sta
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
