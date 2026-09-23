using System.Diagnostics;

namespace Optim.Core.Repair;

public enum RepairKind
{
    /// <summary>Read-only diagnostics: safe, no system changes.</summary>
    Check,
    /// <summary>Repairs/changes system state: run a check first when paired.</summary>
    Repair,
    /// <summary>Reports or opens a tool; neither checks nor repairs.</summary>
    Tool
}

public sealed record RepairAction(
    string Id, string Title, string Description, string FileName, string Arguments, bool NeedsElevation,
    RepairKind Kind = RepairKind.Tool,
    /// <summary>For Repair actions: check action Ids that should run first.</summary>
    string[]? PairedCheckIds = null);

/// <summary>Runs external system-repair tools with live output and cancellation.</summary>
public sealed class RepairEngine
{
    public static IReadOnlyList<RepairAction> Catalog { get; } = new[]
    {
        new RepairAction("sfc", "System File Checker", "Scans and repairs protected Windows system files.",
            "sfc.exe", "/scannow", true, RepairKind.Repair),
        new RepairAction("dism-health", "DISM component store check", "Quick check of the component store for corruption.",
            "dism.exe", "/Online /Cleanup-Image /CheckHealth", true, RepairKind.Check),
        new RepairAction("dism-scan", "DISM component store scan", "Deeper scan of the component store for corruption (slower).",
            "dism.exe", "/Online /Cleanup-Image /ScanHealth", true, RepairKind.Check),
        new RepairAction("dism-restore", "DISM restore health", "Repairs the component store from Windows Update.",
            "dism.exe", "/Online /Cleanup-Image /RestoreHealth", true, RepairKind.Repair,
            new[] { "dism-health", "dism-scan" }),
        new RepairAction("chkdsk-readonly", "Disk check (read-only scan)", "Scans a fixed drive for file-system errors without fixing them.",
            "chkdsk.exe", "{drive} /scan", true, RepairKind.Check),
        new RepairAction("battery-report", "Battery health report", "Generates a battery report; open it from the temp folder when done.",
            "powercfg.exe", "/batteryreport /output \"" + Path.Combine(Path.GetTempPath(), "optim-battery-report.html") + "\"", false),
        new RepairAction("memory-diagnostic", "Memory Diagnostic", "Opens the Windows Memory Diagnostic scheduler (needs a reboot to test).",
            "mdsched.exe", "", false),
        new RepairAction("event-viewer", "System event logs", "Opens Event Viewer for troubleshooting.",
            "mmc.exe", "eventvwr.msc", false)
    };

    /// <summary>Fixed drives offered for drive-selecting actions such as chkdsk.</summary>
    public static IReadOnlyList<string> FixedDriveNames()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.Name.TrimEnd('\\'))
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Resolves {drive} placeholders in the action arguments before running.
    /// Returns null with a log entry when a placeholder cannot be filled.
    /// </summary>
    public RepairAction? ResolvePlaceholders(RepairAction action, string? drive)
    {
        if (!action.Arguments.Contains("{drive}"))
        {
            return action;
        }

        if (string.IsNullOrWhiteSpace(drive))
        {
            Optim.Core.Logging.FileLogger.Warn($"Repair '{action.Id}' needs a drive selection.");
            return null;
        }

        return action with { Arguments = action.Arguments.Replace("{drive}", drive) };
    }

    /// <summary>Runs an action, streaming output lines as they arrive. Returns the exit code.</summary>
    public async Task<int> RunAsync(RepairAction action, Action<string> onOutputLine, CancellationToken token)
    {
        // NOTE: Verb="runas" requires UseShellExecute=true (which forbids output
        // redirection), so it is deliberately not used here. The app itself runs
        // elevated, which is what gives sfc/dism/chkdsk their privileges.
        var psi = new ProcessStartInfo(action.FileName, action.Arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };

        try
        {
            p.Start();
        }
        catch (Exception ex)
        {
            onOutputLine($"[error] could not start {action.FileName}: {ex.Message}");
            throw;
        }

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        try
        {
            await p.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return p.ExitCode;
    }
}
