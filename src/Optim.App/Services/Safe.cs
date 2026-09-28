using Microsoft.UI.Xaml;

namespace Optim.App.Services;

public enum SafeKind
{
    /// <summary>User invoked the action; failures deserve a toast.</summary>
    Action,
    /// <summary>Loaders and tickers; log only, never interrupt the user.</summary>
    Silent
}

/// <summary>
/// One standard catch for fire-and-forget work. The crash post-mortems showed
/// every <c>async void</c> crash is an exception escaping an event handler, so
/// the guard has to be standardized, not re-typed per handler — this is the
/// single place that decides how a stray exception becomes a log line (and for
/// user actions a toast) instead of a dead process.
/// </summary>
public static class Safe
{
    /// <summary>
    /// Runs <paramref name="body"/> inside the standard handler guard: log the
    /// failure, toast the user for <see cref="SafeKind.Action"/> kinds. Returns
    /// a completed task so the caller can stay in a <c>void</c>-returning event
    /// handler without the body being <c>async void</c>.
    /// </summary>
    public static async void Run(SafeKind kind, Func<Task> body)
    {
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            try
            {
                Optim.Core.Logging.FileLogger.Error($"Unhandled ({kind}): {ex}");
            }
            catch
            {
            }

            if (kind == SafeKind.Action)
            {
                try
                {
                    ToastService.Show("Something went wrong — see logs.", ToastKind.Error);
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>Synchronous flavor for handlers with no awaits.</summary>
    public static void Run(SafeKind kind, Action body)
    {
        Run(kind, () =>
        {
            body();
            return Task.CompletedTask;
        });
    }
}
