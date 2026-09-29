namespace Optim.Core.SystemInfo;

/// <summary>
/// Rules for restoring a saved window placement.
///
/// A position saved on a monitor that is no longer attached, or the sentinel
/// origin Windows reports for a minimized window, must never be applied: the
/// process would start normally with its window parked somewhere the user cannot
/// see, which is indistinguishable from the app failing to open at all.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// Coordinates at or below this are Windows' "parked window" sentinel rather
    /// than a real position (a minimized window reports -32000). Real
    /// multi-monitor layouts stay well above it: a display to the left of the
    /// primary sits around -1920 to -7680.
    /// </summary>
    public const int SentinelCoordinate = -30_000;

    public const int MinWidth = 640;
    public const int MinHeight = 480;
    public const int MaxDimension = 10_000;

    /// <summary>
    /// How much of the window has to land on a display before a saved position is
    /// trusted: enough to see and drag the title bar.
    /// </summary>
    public const int MinVisibleWidth = 120;
    public const int MinVisibleHeight = 40;

    /// <summary>A display's usable (work) area.</summary>
    public readonly record struct DisplayBounds(int X, int Y, int Width, int Height);

    /// <summary>A window rectangle.</summary>
    public readonly record struct WindowRect(int X, int Y, int Width, int Height);

    /// <summary>
    /// True when a saved rectangle is a real position, with a size we are willing
    /// to restore, and enough of it on one of the given displays to be reachable.
    /// </summary>
    public static bool IsUsable(WindowRect saved, IReadOnlyList<DisplayBounds> displays)
    {
        if (!HasRealGeometry(saved))
        {
            return false;
        }

        foreach (var display in displays)
        {
            var overlapX = Math.Min(saved.X + saved.Width, display.X + display.Width) - Math.Max(saved.X, display.X);
            var overlapY = Math.Min(saved.Y + saved.Height, display.Y + display.Height) - Math.Max(saved.Y, display.Y);
            if (overlapX >= MinVisibleWidth && overlapY >= MinVisibleHeight)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Positive size and a position above the parked-window sentinel. Zero or
    /// negative sizes come from corrupt saves, and the stored values are read from
    /// a registry hive the user can edit.
    /// </summary>
    public static bool HasRealGeometry(WindowRect saved) =>
        saved.Width > 0
        && saved.Height > 0
        && saved.X > SentinelCoordinate
        && saved.Y > SentinelCoordinate;

    /// <summary>Clamps a saved size into the range every display can show.</summary>
    public static (int Width, int Height) ClampSize(int width, int height) =>
        (Math.Clamp(width, MinWidth, MaxDimension), Math.Clamp(height, MinHeight, MaxDimension));

    /// <summary>
    /// Top-left point that centers a window of the given size on a display. The
    /// offset is floored at zero so a window larger than its display still starts
    /// at the display's own origin instead of hanging off the top-left.
    /// </summary>
    public static (int X, int Y) Center(DisplayBounds display, int width, int height) =>
        (display.X + Math.Max(0, (display.Width - width) / 2),
         display.Y + Math.Max(0, (display.Height - height) / 2));
}
