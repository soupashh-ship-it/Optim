namespace Optim.App.Services;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// App-wide toast notifications (Growl-style): any page can post a short
/// transient message and the shell renders it top-right with auto-dismiss.
/// </summary>
public static class ToastService
{
    public static event Action<string, ToastKind>? ToastRequested;

    public static void Show(string message, ToastKind kind = ToastKind.Info) =>
        ToastRequested?.Invoke(message, kind);
}
