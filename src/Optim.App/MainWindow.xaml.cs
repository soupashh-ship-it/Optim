using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Optim.Core.SystemInfo;
using Windows.Graphics;

namespace Optim.App;

public sealed partial class MainWindow : Window
{
    /// <summary>Guard so the state is saved only after the saved bounds were
    /// applied, never overwriting them with the default 1100×720 first show.</summary>
    private bool _boundsRestored;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Optim";
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        // Restore first: resizing to the default and then to the saved bounds
        // makes the window visibly jump and re-lay-out on every launch.
        RestoreWindowBounds();

        // Persist size/position: every move/resize ends in one final Changed
        // event, and a 300ms debounce keeps the registry write out of the drag.
        AppWindow.Changed += (_, _) => ScheduleSaveBounds();

        // Apply the saved theme to the whole window (Settings only writes HKCU).
        try
        {
            var saved = App.AppSettings.GetString("Theme");
            if (!string.IsNullOrEmpty(saved)
                && Enum.TryParse<ElementTheme>(saved, out var theme)
                && Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
                ApplyCaptionButtons(theme);
            }
            else
            {
                ApplyCaptionButtons(ElementTheme.Default);
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"MainWindow theme: {ex.Message}");
        }
    }

    /// <summary>HKCU key holding the last window placement.</summary>
    private const string BoundsPath = "Software\\Optim\\Window";

    /// <summary>Default window size, used when nothing usable is saved.</summary>
    private static readonly SizeInt32 DefaultSize = new(1100, 720);

    /// <summary>Applies the saved size/position, or a centered default window.</summary>
    private void RestoreWindowBounds()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(BoundsPath);
            var saved = ReadSavedBounds(key);
            var displays = Displays();

            if (saved is not WindowPlacement.WindowRect rect)
            {
                // Nothing usable saved: first run, or a placement that only ever
                // held a sentinel. One centered window, no move.
                CenterOnPrimary(DefaultSize, displays);
                return;
            }

            // A saved rect that is not on any current display must be ignored: the
            // window would come back alive but parked where nobody can reach it.
            if (!WindowPlacement.IsUsable(rect, displays))
            {
                Optim.Core.Logging.FileLogger.Warn(
                    $"Saved window bounds {rect.X},{rect.Y} {rect.Width}x{rect.Height} are not on any display; centering instead.");
                CenterOnPrimary(DefaultSize, displays);
                return;
            }

            var (w, h) = WindowPlacement.ClampSize(rect.Width, rect.Height);
            AppWindow.Resize(new SizeInt32(w, h));
            AppWindow.Move(new PointInt32(rect.X, rect.Y));
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Restore bounds: {ex.Message}");
        }
        finally
        {
            _boundsRestored = true;
        }
    }

    /// <summary>
    /// Reads the saved placement, rejecting the values Windows reports for a
    /// parked (minimized) window - the origin it uses then is -32000, which is
    /// what previously got stored and restored as an off-screen position.
    /// </summary>
    private static WindowPlacement.WindowRect? ReadSavedBounds(Microsoft.Win32.RegistryKey? key)
    {
        if (key?.GetValue("W") is not int w || key.GetValue("H") is not int h
            || key.GetValue("X") is not int x || key.GetValue("Y") is not int y)
        {
            return null;
        }

        var rect = new WindowPlacement.WindowRect(x, y, w, h);
        return WindowPlacement.HasRealGeometry(rect) ? rect : null;
    }

    /// <summary>The usable rectangle of every attached display.</summary>
    private static IReadOnlyList<WindowPlacement.DisplayBounds> Displays()
    {
        var list = new List<WindowPlacement.DisplayBounds>();
        try
        {
            foreach (var area in DisplayArea.FindAll())
            {
                var work = area.WorkArea;
                list.Add(new WindowPlacement.DisplayBounds(work.X, work.Y, work.Width, work.Height));
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Enumerate displays: {ex.Message}");
        }

        return list;
    }

    /// <summary>Places the window centered on the primary display.</summary>
    private void CenterOnPrimary(SizeInt32 size, IReadOnlyList<WindowPlacement.DisplayBounds> displays)
    {
        try
        {
            var primary = displays.Count > 0
                ? displays[0]
                : new WindowPlacement.DisplayBounds(0, 0, 1280, 720);

            var (width, height) = WindowPlacement.ClampSize(size.Width, size.Height);
            width = Math.Min(width, Math.Max(primary.Width, WindowPlacement.MinWidth));
            height = Math.Min(height, Math.Max(primary.Height, WindowPlacement.MinHeight));

            var (x, y) = WindowPlacement.Center(primary, width, height);
            AppWindow.Resize(new SizeInt32(width, height));
            AppWindow.Move(new PointInt32(x, y));
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Center window: {ex.Message}");
        }
    }

    /// <summary>
    /// Brings the window to the front, restoring it first if it is minimized.
    /// Used on launch and whenever a second launch is forwarded here, because a
    /// click on the app icon has to surface the window that is already open.
    /// </summary>
    public void BringToFront()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            // A minimized window cannot be activated into view. SW_RESTORE also
            // puts it back in the state it was minimized from (including
            // maximized), which restoring through the presenter does not promise.
            ShowWindow(hwnd, SW_RESTORE);

            // Recover a window parked off every display (monitor unplugged, or a
            // stale saved position) before showing it, or it stays invisible.
            EnsureOnScreen();

            Activate();

            // Activation alone is often not enough when the request comes from
            // another process: the window is raised but stays behind the focused
            // one, so claim the foreground explicitly.
            SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Bring to front: {ex.Message}");
        }
    }

    /// <summary>Centers the window on the primary display if it is off every one.</summary>
    private void EnsureOnScreen()
    {
        try
        {
            var pos = AppWindow.Position;
            var size = AppWindow.Size;
            var rect = new WindowPlacement.WindowRect(pos.X, pos.Y, size.Width, size.Height);
            if (WindowPlacement.IsUsable(rect, Displays()))
            {
                return;
            }

            Optim.Core.Logging.FileLogger.Warn($"Window at {pos.X},{pos.Y} is off every display; centering it.");
            CenterOnPrimary(DefaultSize, Displays());
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Ensure on screen: {ex.Message}");
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveTimer;

    private void ScheduleSaveBounds()
    {
        if (!_boundsRestored)
        {
            return;
        }

        try
        {
            _saveTimer ??= DispatcherQueue.CreateTimer();
            _saveTimer.Stop();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(300);
            _saveTimer.Tick -= SaveTimer_Tick;
            _saveTimer.Tick += SaveTimer_Tick;
            _saveTimer.Start();
        }
        catch
        {
            // Persistence must never break the window.
        }
    }

    private void SaveTimer_Tick(object? sender, object e)
    {
        try
        {
            (_saveTimer ?? throw new InvalidOperationException()).Stop();

            // A minimized window reports the -32000 sentinel origin at a minimum
            // sized rect. Saving that is what used to park Optim off-screen on
            // the next launch, so the placement is simply not recorded here.
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
            {
                return;
            }

            // Position/Size are plain properties (no P/Invoke needed). A
            // maximized window saves its maximized rect; restoring that
            // un-maximized is imperfect but harmless.
            var pos = AppWindow.Position;
            var size = AppWindow.Size;
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(BoundsPath, writable: true);
            if (key is null)
            {
                return;
            }

            key.SetValue("X", pos.X, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("Y", pos.Y, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("W", size.Width, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("H", size.Height, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Save bounds: {ex.Message}");
        }
    }

    /// <summary>Keeps the title-bar caption buttons readable on light/dark themes.</summary>
    public void ApplyCaptionButtons(ElementTheme theme)
    {
        try
        {
            var dark = theme == ElementTheme.Dark
                || (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
            var fg = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            var bar = AppWindow.TitleBar;
            bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonForegroundColor = fg;
            bar.ButtonHoverBackgroundColor = dark
                ? Windows.UI.Color.FromArgb(25, 255, 255, 255)
                : Windows.UI.Color.FromArgb(15, 0, 0, 0);
            bar.ButtonHoverForegroundColor = fg;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Caption buttons: {ex.Message}");
        }
    }
}
