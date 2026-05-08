using System.Security.AccessControl;
using System.Security.Principal;
using WindowsRightClickLock.Native;
using WindowsRightClickLock.UI;

namespace WindowsRightClickLock;

internal static class Program
{
    // Local\ scopes the kernel objects to the current logon session, preventing cross-session
    // squatting and unauthenticated cross-session signaling. See docs/security-review.md (H1).
    private const string MutexName = "Local\\WindowsRightClickLock.SingleInstance";
    private const string ShowEventName = "Local\\WindowsRightClickLock.Show";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _showEvent;
    private static Thread? _showListenerThread;
    private static SynchronizationContext? _uiSyncContext;
    private static volatile bool _shuttingDown;

    /// <summary>Set by TrayApplicationContext so the show-listener can route to the UI.</summary>
    internal static TrayApplicationContext? ActiveContext { get; set; }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--preview-icons")
        {
            var dir = args.Length > 1 ? args[1] : "preview";
            if (!IsSafeRelativePath(dir))
            {
                Console.Error.WriteLine(
                    "--preview-icons requires a relative path with no '..' segments. " +
                    "Absolute paths and traversal are rejected. See docs/security-review.md (M4).");
                return;
            }
            UI.PreviewIcons.WriteToDisk(dir);
            return;
        }

        // ACL the named objects to the current user only. Defense-in-depth on top of the Local\
        // namespace: even other processes running as the same user in the same session need to
        // be granted access explicitly. See docs/security-review.md (H2).
        var sid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot resolve current user SID.");

        var mutexSec = new MutexSecurity();
        mutexSec.AddAccessRule(new MutexAccessRule(sid, MutexRights.FullControl, AccessControlType.Allow));
        _singleInstanceMutex = MutexAcl.Create(initiallyOwned: true, MutexName, out var createdNew, mutexSec);
        if (!createdNew)
        {
            // Another instance owns the mutex; signal it to show its window, then exit.
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
            MessageBox.Show(e.Exception.ToString(), "Windows Right-Click Lock - error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        try
        {
            var eventSec = new EventWaitHandleSecurity();
            eventSec.AddAccessRule(new EventWaitHandleAccessRule(sid,
                EventWaitHandleRights.FullControl, AccessControlType.Allow));
            _showEvent = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset,
                ShowEventName, out _, eventSec);
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

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (Path.IsPathRooted(path)) return false;
        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var seg in segments)
        {
            if (seg == "..") return false;
        }
        return true;
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
            Name = "WindowsRightClickLock.ShowListener",
        };
        _showListenerThread.Start();
    }
}
