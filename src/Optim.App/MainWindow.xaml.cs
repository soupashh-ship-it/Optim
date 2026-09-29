using Microsoft.UI;
using Microsoft.UI.Xaml;
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

    /// <summary>Default window size, used when nothing is saved yet.</summary>
    private static readonly SizeInt32 DefaultSize = new(1100, 720);

    /// <summary>Applies the saved size/position, or the default size on first run.</summary>
    private void RestoreWindowBounds()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(BoundsPath);
            if (key?.GetValue("W") is not int w || key.GetValue("H") is not int h
                || key.GetValue("X") is not int x || key.GetValue("Y") is not int y)
            {
                // Nothing saved: first run. One resize, no move.
                AppWindow.Resize(DefaultSize);
                _boundsRestored = true; // save freely from here on
                return;
            }

            // Bounds sanity: refuse nonsense saved by a since-changed monitor
            // setup (clamping to at least the default size).
            w = Math.Clamp(w, 640, 10000);
            h = Math.Clamp(h, 480, 10000);
            AppWindow.Resize(new SizeInt32(w, h));
            AppWindow.Move(new PointInt32(x, y));
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
            // Position/Size are plain properties (no P/Invoke needed). A
            // maximized window saves its maximized rect; restoring that
            // un-maximized is imperfect but harmless.
            var pos = AppWindow.Position;
            var size = AppWindow.Size;
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(BoundsPath, writable: true);
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
