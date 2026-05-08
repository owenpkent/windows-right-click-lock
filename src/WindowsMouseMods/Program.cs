using System.Diagnostics;
using WindowsMouseMods.UI;

namespace WindowsMouseMods;

internal static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    private static void Main()
    {
        const string mutexName = "Global\\WindowsMouseMods.SingleInstance";
        _singleInstanceMutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            // Another instance is already running.
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(
            e.Exception.ToString(), "Windows Mouse Mods — error",
            MessageBoxButtons.OK, MessageBoxIcon.Error);

        try
        {
            using var ctx = new TrayApplicationContext();
            Application.Run(ctx);
        }
        finally
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
    }
}
