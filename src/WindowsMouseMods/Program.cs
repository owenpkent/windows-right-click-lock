using WindowsMouseMods.Native;
using WindowsMouseMods.UI;

namespace WindowsMouseMods;

internal static class Program
{
    private const string MutexName = "Global\\WindowsMouseMods.SingleInstance";
    private const string ShowEventName = "Global\\WindowsMouseMods.Show";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _showEvent;
    private static Thread? _showListenerThread;
    private static SynchronizationContext? _uiSyncContext;
    private static volatile bool _shuttingDown;

    /// <summary>Set by TrayApplicationContext so the show-listener can route to the UI.</summary>
    internal static TrayApplicationContext? ActiveContext { get; set; }

    [STAThread]
    private static void Main()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Another instance owns the mutex — signal it to show its window, then exit.
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                {
                    using (existing) existing.Set();
                }
            }
            catch
            {
                // Best-effort. Even if signaling fails, we're done.
            }
            return;
        }

        // Emergency release on any abnormal exit so a crash doesn't leave RMB stuck "down".
        AppDomain.CurrentDomain.UnhandledException += (_, _) => InputInjector.EmergencyRelease();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => InputInjector.EmergencyRelease();
        Application.ApplicationExit += (_, _) => InputInjector.EmergencyRelease();

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            // Release first in case the exception came from the controller while locked.
            InputInjector.EmergencyRelease();
            MessageBox.Show(e.Exception.ToString(), "Windows Mouse Mods — error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _uiSyncContext = new WindowsFormsSynchronizationContext();
            StartShowListener();

            using var ctx = new TrayApplicationContext();
            ActiveContext = ctx;
            Application.Run(ctx);
        }
        finally
        {
            _shuttingDown = true;
            _showEvent?.Set(); // wake the listener so it can exit
            _showListenerThread?.Join(TimeSpan.FromSeconds(1));
            _showEvent?.Dispose();
            InputInjector.EmergencyRelease();
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
    }

    private static void StartShowListener()
    {
        _showListenerThread = new Thread(() =>
        {
            while (!_shuttingDown)
            {
                _showEvent!.WaitOne();
                if (_shuttingDown) break;
                _uiSyncContext?.Post(_ => ActiveContext?.ShowMainForm(), null);
            }
        })
        {
            IsBackground = true,
            Name = "WindowsMouseMods.ShowListener",
        };
        _showListenerThread.Start();
    }
}
