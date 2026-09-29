using Optim.Core.SystemInfo;
using Xunit;

namespace Optim.Core.Tests;

/// <summary>
/// Covers the rules that keep a restored window reachable. The bug this guards
/// against: a minimized window reports the origin -32000, that got saved, and
/// every later launch restored the window off-screen - alive but invisible, which
/// reads as "the app will not open".
/// </summary>
public class WindowPlacementTests
{
    private static readonly WindowPlacement.DisplayBounds Primary =
        new(0, 0, 1920, 1040);

    private static readonly WindowPlacement.DisplayBounds SecondaryLeft =
        new(-1920, 0, 1920, 1040);

    [Fact]
    public void Rejects_the_minimized_window_sentinel_origin()
    {
        var parked = new WindowPlacement.WindowRect(-32000, -32000, 640, 480);

        Assert.False(WindowPlacement.HasRealGeometry(parked));
        Assert.False(WindowPlacement.IsUsable(parked, new[] { Primary }));
    }

    [Fact]
    public void Accepts_a_window_that_still_lands_on_a_display()
    {
        var saved = new WindowPlacement.WindowRect(120, 80, 1100, 720);

        Assert.True(WindowPlacement.IsUsable(saved, new[] { Primary }));
    }

    [Fact]
    public void Accepts_a_position_on_a_display_left_of_the_primary()
    {
        // Negative coordinates are legitimate for a monitor left of the primary;
        // only the sentinel depth is nonsense.
        var saved = new WindowPlacement.WindowRect(-1600, 60, 1100, 720);

        Assert.True(WindowPlacement.IsUsable(saved, new[] { Primary, SecondaryLeft }));
    }

    [Fact]
    public void Rejects_a_position_on_a_monitor_that_is_no_longer_attached()
    {
        // Saved on the left-hand display, which is now unplugged: only the
        // primary remains, so the rect is no longer reachable.
        var saved = new WindowPlacement.WindowRect(-1600, 60, 1100, 720);

        Assert.False(WindowPlacement.IsUsable(saved, new[] { Primary }));
    }

    [Fact]
    public void Rejects_bounds_that_barely_touch_a_display()
    {
        // Only a few pixels on screen: there is nothing to grab and drag back.
        var sliver = new WindowPlacement.WindowRect(1910, 500, 1100, 720);

        Assert.False(WindowPlacement.IsUsable(sliver, new[] { Primary }));
    }

    [Fact]
    public void Rejects_zero_and_negative_sizes()
    {
        Assert.False(WindowPlacement.HasRealGeometry(new WindowPlacement.WindowRect(100, 100, 0, 720)));
        Assert.False(WindowPlacement.HasRealGeometry(new WindowPlacement.WindowRect(100, 100, 1100, -5)));
    }

    [Fact]
    public void Rejects_any_position_when_no_display_is_known()
    {
        // Display enumeration can fail; falling back to centering is the safe
        // outcome, so nothing is treated as usable.
        var saved = new WindowPlacement.WindowRect(120, 80, 1100, 720);

        Assert.False(WindowPlacement.IsUsable(saved, Array.Empty<WindowPlacement.DisplayBounds>()));
    }

    [Fact]
    public void Clamps_saved_sizes_into_a_restorable_range()
    {
        Assert.Equal((640, 480), WindowPlacement.ClampSize(10, 10));
        Assert.Equal((1100, 720), WindowPlacement.ClampSize(1100, 720));
        Assert.Equal((10000, 10000), WindowPlacement.ClampSize(50000, 50000));
    }

    [Fact]
    public void Centers_a_window_inside_a_display()
    {
        var (x, y) = WindowPlacement.Center(Primary, 1100, 720);

        Assert.Equal(410, x);
        Assert.Equal(160, y);
    }

    [Fact]
    public void Centers_without_a_negative_offset_on_a_display_smaller_than_the_window()
    {
        // A window wider than its display must start at the display origin, not
        // hang off the left edge where the title bar would be unreachable.
        var small = new WindowPlacement.DisplayBounds(0, 0, 800, 600);
        var (x, y) = WindowPlacement.Center(small, 1100, 720);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }
}
