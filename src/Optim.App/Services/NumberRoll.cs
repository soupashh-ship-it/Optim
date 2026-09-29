using System.Globalization;
using Microsoft.UI.Xaml.Controls;

namespace Optim.App.Services;

/// <summary>
/// Rolls a numeric value in a <see cref="TextBlock"/> from where it is to where
/// it should be, instead of snapping to it.
///
/// Live figures that jump on every sample read as a stutter even when nothing is
/// slow, which is why the graphs already glide. This gives the numbers beside
/// them the same treatment: a few hundred milliseconds of easing, driven by plain
/// text updates, so it costs no more than the snap it replaces.
/// </summary>
internal static class NumberRoll
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(320);

    /// <summary>~30fps. Smooth to the eye, and a third of the text updates of a
    /// per-frame roll for a value that is only ever read at a glance.</summary>
    private const int FrameMilliseconds = 33;

    /// <summary>Latest roll started per target, so a newer value cancels the one
    /// still animating instead of the two fighting over the same text.</summary>
    private static readonly Dictionary<TextBlock, long> Generation = new();

    /// <summary>Starts (or restarts) a roll to <paramref name="value"/>.</summary>
    public static void To(TextBlock target, double value, Func<double, string> format)
    {
        var from = Parse(target.Text);
        if (double.IsNaN(from) || Math.Abs(from - value) < 0.5)
        {
            // Nothing to roll: the previous text was not a number (a placeholder
            // like "—"), or it already shows this value.
            target.Text = format(value);
            return;
        }

        var generation = NextGeneration(target);
        var queue = target.DispatcherQueue;
        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(FrameMilliseconds);
        timer.IsRepeating = true;
        var started = Environment.TickCount64;

        timer.Tick += (_, _) =>
        {
            if (!IsCurrent(target, generation))
            {
                timer.Stop();
                return;
            }

            var progress = (Environment.TickCount64 - started) / Duration.TotalMilliseconds;
            if (progress >= 1)
            {
                timer.Stop();
                target.Text = format(value);
                return;
            }

            // Ease out: moves off the old value immediately, then settles.
            var eased = 1 - Math.Pow(1 - progress, 3);
            target.Text = format(from + ((value - from) * eased));
        };

        timer.Start();
    }

    private static long NextGeneration(TextBlock target)
    {
        var next = Generation.TryGetValue(target, out var current) ? current + 1 : 1;
        Generation[target] = next;
        return next;
    }

    private static bool IsCurrent(TextBlock target, long generation) =>
        Generation.TryGetValue(target, out var current) && current == generation;

    /// <summary>Reads the leading number out of formatted text ("42%", "1,204"),
    /// returning NaN when there is no number to roll from.</summary>
    private static double Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return double.NaN;
        }

        var digits = text
            .TakeWhile(c => char.IsDigit(c) || c == '-' || c == '.' || c == ',')
            .Select(c => c == ',' ? ' ' : c)
            .Where(c => c != ' ')
            .ToArray();

        return digits.Length > 0
            && double.TryParse(new string(digits), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : double.NaN;
    }
}
