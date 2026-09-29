using Microsoft.UI.Dispatching;

namespace Optim.App.Services;

/// <summary>
/// Coalesces a burst of calls (search-as-you-type) into one run after the input
/// pauses.
///
/// Rebuilding a few hundred rows per keystroke is work the user never sees: each
/// keystroke queued a list reset that the next keystroke immediately superseded.
/// Waiting for a short gap keeps the caret and the typed text perfectly
/// responsive while the list settles once, at the end.
/// </summary>
internal sealed class Debouncer
{
    private readonly DispatcherQueueTimer _timer;
    private Action? _pending;

    public Debouncer(DispatcherQueue queue, TimeSpan delay)
    {
        _timer = queue.CreateTimer();
        _timer.Interval = delay;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Take()?.Invoke();
        };
    }

    /// <summary>Queues the action, replacing anything still waiting.</summary>
    public void Run(Action action)
    {
        _pending = action;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>
    /// Runs a waiting action now (Enter applies immediately for muscle memory)
    /// and clears it, so a later tick cannot apply the same filter twice.
    /// </summary>
    public void Flush()
    {
        _timer.Stop();
        Take()?.Invoke();
    }

    private Action? Take()
    {
        var pending = _pending;
        _pending = null;
        return pending;
    }
}
