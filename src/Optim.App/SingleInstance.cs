using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Windows.AppLifecycle;

namespace Optim.App;

/// <summary>
/// Single-instance handling that surfaces the running window instead of
/// vanishing.
///
/// Two copies would fight over the journal and the live system state, so only one
/// may run. The old guard simply exited the second process, which is
/// indistinguishable from an app that refuses to open: the user taps the icon,
/// something starts, and nothing appears. A launch that lands on a running copy
/// now hands its activation over, and the running copy brings its window to the
/// front. If the activation API is unavailable for any reason, the running
/// window is located and focused directly before giving up, and only the journal
/// guard's silent exit remains as the last resort.
/// </summary>
internal static class SingleInstance
{
    private const string InstanceKey = "Optim";

    /// <summary>Window class WinUI 3 uses for its top-level desktop window.</summary>
    private const string WinUIWindowClass = "WinUIDesktopWin32WindowClass";

    /// <summary>
    /// The registered instance of the running copy: the primary subscribes to its
    /// <c>Activated</c> event to receive forwarded launches.
    /// </summary>
    public static AppInstance? Primary { get; private set; }

    /// <summary>
    /// True when this process owns the single-instance slot and should show its
    /// window. False means another copy is already running and this process has
    /// forwarded its activation to it (or, failing that, tried to focus it).
    /// </summary>
    public static bool TryAcquire(string windowTitle)
    {
        try
        {
            var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (instance.IsCurrent)
            {
                Primary = instance;
                return true;
            }

            var forwarded = ForwardActivation(instance, windowTitle);
            Optim.Core.Logging.FileLogger.Info(
                forwarded
                    ? "Activation forwarded to the running instance."
                    : "Another instance is running; its window could not be surfaced.");
            return false;
        }
        catch (Exception ex)
        {
            // The activation API is unavailable here (it is not supported in every
            // hosting/hardening configuration). Fall back to the journal guard so
            // two copies still never write concurrently, at the cost of the window
            // not being raised.
            Optim.Core.Logging.FileLogger.Warn($"Single-instance API unavailable: {ex.Message}");
            return MutexGuard.TryAcquire();
        }
    }

    /// <summary>
    /// Hands this launch to the running copy so it can raise its window. Falls
    /// back to finding the window and focusing it directly when redirection does
    /// not report success.
    /// </summary>
    private static bool ForwardActivation(AppInstance primary, string windowTitle)
    {
        try
        {
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            var redirected = new ManualResetEventSlim(false);

            // Redirection must not run on this thread: it blocks while the target
            // processes the activation, and blocking the UI thread here would
            // deadlock before it can.
            _ = Task.Run(() =>
            {
                try
                {
                    primary.RedirectActivationToAsync(args).AsTask().Wait(TimeSpan.FromSeconds(8));
                }
                catch (Exception ex)
                {
                    Optim.Core.Logging.FileLogger.Warn($"Activation redirect failed: {ex.Message}");
                }
                finally
                {
                    redirected.Set();
                }
            });

            if (redirected.Wait(TimeSpan.FromSeconds(10)))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Activation redirect unavailable: {ex.Message}");
        }

        // Last resort: the running copy is up but never heard about us, so raise
        // its window from here.
        return FocusExistingWindow(windowTitle);
    }

    /// <summary>
    /// Finds the running Optim window by class and title and brings it to the
    /// front, un-minimizing it first (a minimized window cannot be activated into
    /// view, and the foreground lock grants this process the right because the
    /// user just launched it).
    /// </summary>
    private static bool FocusExistingWindow(string windowTitle)
    {
        try
        {
            var found = IntPtr.Zero;
            EnumWindows((hwnd, _) =>
            {
                if (IsOptimWindow(hwnd, windowTitle))
                {
                    found = hwnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            if (found == IntPtr.Zero)
            {
                return false;
            }

            ShowWindow(found, SW_RESTORE);
            return SetForegroundWindow(found);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Focus existing window: {ex.Message}");
            return false;
        }
    }

    private static bool IsOptimWindow(IntPtr hwnd, string windowTitle)
    {
        if (!IsWindowVisible(hwnd))
        {
            return false;
        }

        var title = new StringBuilder(64);
        GetWindowText(hwnd, title, title.Capacity);
        if (!string.Equals(title.ToString(), windowTitle, StringComparison.Ordinal))
        {
            return false;
        }

        var className = new StringBuilder(64);
        GetClassName(hwnd, className, className.Capacity);
        return string.Equals(className.ToString(), WinUIWindowClass, StringComparison.Ordinal);
    }

    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    /// <summary>
    /// Fallback guard: a process-wide mutex. Only used when the activation API is
    /// unavailable, so the two mechanisms never fight each other.
    /// </summary>
    private static class MutexGuard
    {
        private static Mutex? _mutex;

        public static bool TryAcquire()
        {
            try
            {
                _mutex = new Mutex(initiallyOwned: true, "Global\\OptimApp-SingleInstance", out var createdNew);
                return createdNew;
            }
            catch
            {
                return false;
            }
        }
    }
}
