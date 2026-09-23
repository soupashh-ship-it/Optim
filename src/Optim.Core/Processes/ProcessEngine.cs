using System.Collections.Concurrent;
using System.Diagnostics;

namespace Optim.Core.Processes;

public sealed record ProcessEntry(int Id, string Name, double CpuPercent, long WorkingSetBytes, string? WindowTitle, int Threads);

/// <summary>
/// Live process enumeration with resource usage and end-task support.
/// CPU% is computed from two consecutive snapshots (per-process processor
/// time delta over wall time), so the first snapshot reports 0 by design.
/// </summary>
public static class ProcessEngine
{
    private static readonly ConcurrentDictionary<int, (long Ticks, DateTime At)> LastCpuSample = new();

    public static IReadOnlyList<ProcessEntry> Snapshot()
    {
        var list = new List<ProcessEntry>();
        var now = DateTime.UtcNow;
        var cores = Math.Max(1, Environment.ProcessorCount);
        var seen = new HashSet<int>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                double cpu = 0;
                try
                {
                    var ticks = p.TotalProcessorTime.Ticks;
                    if (LastCpuSample.TryGetValue(p.Id, out var prev) && now > prev.At)
                    {
                        var elapsedSeconds = (now - prev.At).TotalSeconds;
                        if (elapsedSeconds > 0.2)
                        {
                            // Total across all cores: 100% == one full core,
                            // matching Task Manager's default display.
                            cpu = Math.Clamp(
                                (ticks - prev.Ticks) / (elapsedSeconds * cores) * 100.0,
                                0, 100 * cores);
                        }
                    }

                    LastCpuSample[p.Id] = (ticks, now);
                }
                catch
                {
                    // TotalProcessorTime is access-denied for protected
                    // processes; CPU stays 0 rather than inventing a value.
                }

                seen.Add(p.Id);
                list.Add(new ProcessEntry(
                    p.Id,
                    p.ProcessName,
                    Math.Round(cpu, 1),
                    p.WorkingSet64,
                    SafeTitle(p),
                    ThreadCount(p)));
            }
            catch
            {
                // Process may have exited between enumeration and read.
            }
            finally
            {
                p.Dispose();
            }
        }

        // Prune samples for exited PIDs so the dictionary cannot grow forever.
        foreach (var key in LastCpuSample.Keys)
        {
            if (!seen.Contains(key))
            {
                LastCpuSample.TryRemove(key, out _);
            }
        }

        return list.OrderByDescending(p => p.WorkingSetBytes).ToList();
    }

    private static int ThreadCount(Process p)
    {
        try { return p.Threads.Count; }
        catch { return 0; }
    }

    private static string? SafeTitle(Process p)
    {
        try { return string.IsNullOrWhiteSpace(p.MainWindowTitle) ? null : p.MainWindowTitle; }
        catch { return null; }
    }

    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "csrss", "wininit", "services", "lsass", "smss",
        "winlogon", "fontdrvhost", "dwm", "explorer"
    };

    public static bool IsCritical(int processId, string? name)
    {
        if (processId is 0 or 4)
        {
            return true;
        }

        return !string.IsNullOrEmpty(name) && Critical.Contains(name);
    }

    /// <summary>Kills a single process. Critical system processes are refused.</summary>
    public static bool EndTask(int processId)
    {
        try
        {
            using var p = Process.GetProcessById(processId);
            if (IsCritical(p.Id, p.ProcessName))
            {
                Optim.Core.Logging.FileLogger.Warn($"EndTask refused for critical process {p.ProcessName} ({p.Id})");
                return false;
            }

            // Single-process kill: tree-kill can wipe child processes the user
            // did not select (and escalate a misclick into a crash/BSOD).
            p.Kill();
            return p.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"EndTask {processId}: {ex.Message}");
            return false;
        }
    }
}
