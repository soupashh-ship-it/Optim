using System.ServiceProcess;
using Optim.Core.Tweaks;

namespace Optim.Core.ServicesMgmt;

public sealed record ServiceEntry(string Name, string DisplayName, ServiceStartMode StartMode, ServiceControllerStatus Status, bool CanModify, bool IsCritical, bool IsRunning, string Description, bool StartModeKnown, bool IsDelayedAuto);

/// <summary>
/// Service management. Every service is controllable — start/stop/restart and
/// start-type changes — EXCEPT a small refuse-list of boot-critical services
/// (RPC, security accounts, event log, …) that can stop Windows from booting
/// or blur the line into breaking the OS. Successful start-type changes are
/// journaled so "Revert all" restores them.
/// </summary>
public sealed class ServiceEngine
{
    private readonly ChangeJournal? _journal;

    /// <summary>Historic vetted list, still used to seed the search index.
    /// It no longer gates UI changes; <see cref="Critical"/> does.</summary>
    private static readonly HashSet<string> Vetted = new(StringComparer.OrdinalIgnoreCase)
    {
        "DiagTrack", "dmwappushservice", "SysMain", "WSearch", "MapsBroker",
        "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc",
        "RetailDemo", "RemoteRegistry", "WMPNetworkSvc", "Fax", "lfsvc",
        "PhoneSvc", "TapiSrv", "WpcMonSvc", "stisvc", "TrkWks", "WerSvc"
    };

    public ServiceEngine(ChangeJournal? journal = null) => _journal = journal;

    /// <summary>Boot- and security-critical services: the refuse-list. Changing
    /// these can stop Windows from booting entirely, so every mutation refuses
    /// them — the UI renders them read-only with a badge.</summary>
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "DcomLaunch", "RpcEptMapper", "SamSs", "lsm",
        "BrokerInfrastructure", "CoreMessagingRegistrar", "SystemEventsBroker",
        "TimeBrokerSvc", "EventLog", "PlugPlay", "Power", "ProfSvc",
        "Schedule", "CryptSvc", "Dhcp", "Dnscache", "Nsi",
        "WinDefend", "WdNisSvc", "WdBoot", "Sense", "MpsSvc", "BFE",
        "Winmgmt", "LanmanServer", "LanmanWorkstation", "Netlogon"
    };

    public static bool IsVetted(string? serviceName) =>
        !string.IsNullOrWhiteSpace(serviceName) && Vetted.Contains(serviceName.Trim());

    /// <summary>Vetted names for the search index (routes to the Services page).</summary>
    public static IReadOnlyList<string> VettedNames => Vetted.ToList();

    public static bool IsCritical(string? serviceName) =>
        !string.IsNullOrWhiteSpace(serviceName) && Critical.Contains(serviceName.Trim());

    public IReadOnlyList<ServiceEntry> List()
    {
        var result = new List<ServiceEntry>();
        var controllers = ServiceController.GetServices();
        foreach (var sc in controllers)
        {
            try
            {
                var mode = SafeStartMode(sc);
                var running = false;
                try { running = sc.Status == ServiceControllerStatus.Running; } catch { }
                var critical = IsCritical(sc.ServiceName);
                var delayed = mode.Mode == ServiceStartMode.Automatic && IsDelayedAuto(sc.ServiceName);
                result.Add(new ServiceEntry(sc.ServiceName, SafeDisplayName(sc), mode.Mode, StatusOf(sc), !critical, critical, running, ServiceDescription(sc.ServiceName), mode.Known, delayed));
            }
            catch (Exception ex)
            {
                string name;
                try { name = sc.ServiceName; }
                catch { name = "?"; }
                Optim.Core.Logging.FileLogger.Warn($"Service {name}: {ex.Message}");
            }
            finally
            {
                sc.Dispose();
            }
        }

        return result.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string SafeDisplayName(ServiceController sc)
    {
        try { return sc.DisplayName; }
        catch { return sc.ServiceName; }
    }

    private static string ServiceDescription(string serviceName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            var raw = key?.GetValue("Description")?.ToString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "";
            }

            return Environment.ExpandEnvironmentVariables(raw);
        }
        catch
        {
            return "";
        }
    }

    private static ServiceControllerStatus StatusOf(ServiceController sc)
    {
        try { return sc.Status; }
        catch { return ServiceControllerStatus.Stopped; }
    }

    public async Task<bool> StartAsync(string serviceName)
    {
        return await ControlAsync(serviceName, sc => sc.Start(), "start");
    }

    public async Task<bool> StopAsync(string serviceName)
    {
        return await ControlAsync(serviceName, sc => sc.Stop(), "stop");
    }

    public async Task<bool> RestartAsync(string serviceName)
    {
        return await ControlAsync(serviceName, sc => sc.Start(), "restart", stopFirst: true);
    }

    private static async Task<bool> ControlAsync(
        string serviceName, Action<ServiceController> action, string verb, bool stopFirst = false)
    {
        if (IsCritical(serviceName))
        {
            Optim.Core.Logging.FileLogger.Warn($"Service {serviceName} {verb} refused: boot-critical.");
            return false;
        }

        try
        {
            using var sc = new ServiceController(serviceName);
            if (stopFirst)
            {
                try { sc.Stop(); } catch { }
            }

            action(sc);
            // SCM operations are asynchronous; WaitForStatus avoids reporting
            // success while the service is still transitioning.
            var target = verb switch
            {
                "start" => ServiceControllerStatus.Running,
                "stop" => ServiceControllerStatus.Stopped,
                _ => ServiceControllerStatus.Running
            };
            var timeout = stopFirst ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(15);
            await Task.Run(() =>
            {
                try { sc.WaitForStatus(target, timeout); }
                catch { }
            });
            sc.Refresh();
            var ok = sc.Status == target
                // A restart where the service was already stopped is fine.
                || (stopFirst && sc.Status == ServiceControllerStatus.Stopped && verb == "restart");
            if (!ok)
            {
                Optim.Core.Logging.FileLogger.Warn($"Service {serviceName} {verb}: status still {sc.Status} after wait.");
            }
            return ok;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Service {serviceName} {verb}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads the start type without lying: an unreadable start type reports
    /// <see cref="ServiceStartMode.Manual"/> with <c>Known=false</c> instead of
    /// masquerading as Disabled.
    /// </summary>
    private static (ServiceStartMode Mode, bool Known) SafeStartMode(ServiceController sc)
    {
        try { return (sc.StartType, true); }
        catch { return (ServiceStartMode.Manual, false); }
    }

    /// <summary>
    /// Reads the DelayedAutostart flag (only meaningful for Automatic start).
    /// ServiceController reports delayed services as plain Automatic.
    /// </summary>
    private static bool IsDelayedAuto(string serviceName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key?.GetValue("DelayedAutostart") is 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Sets the start type via the registry (SCM ChangeStartMode equivalent).
    /// Boot-critical services are refused; everything else is allowed and journaled.
    /// <paramref name="delayedStart"/> only applies to Automatic mode.</summary>
    public bool SetStartType(string serviceName, ServiceStartMode mode, bool delayedStart = false)
    {
        // Validate the mode first so bad input never reaches the guards below.
        var (value, delayed) = mode switch
        {
            ServiceStartMode.Automatic => (2, (bool?)delayedStart),
            ServiceStartMode.Manual => (3, (bool?)false),
            ServiceStartMode.Disabled => (4, (bool?)false),
            // Boot/System are kernel start modes no user tweak should write.
            _ => throw new ArgumentOutOfRangeException(nameof(mode), $"Unsupported start mode {mode}.")
        };

        if (IsCritical(serviceName))
        {
            throw new InvalidOperationException($"'{serviceName}' is boot-critical and cannot be changed from Optim.");
        }

        var keyPath = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
            if (key is null)
            {
                Optim.Core.Logging.FileLogger.Error($"SetStartType {serviceName}: service key not found.");
                return false;
            }

            var before = _journal?.Snapshot().Count ?? 0;
            try
            {
                if (_journal is not null)
                {
                    // Journal the previous Start value so "Revert all" restores it.
                    // Only plain integer values are journaled; anything exotic is
                    // left unjournaled rather than risking a wrong restore.
                    var raw = key.GetValue("Start");
                    int? prev = raw switch
                    {
                        int i => i,
                        long l => (int)l,
                        _ => null
                    };
                    if (prev.HasValue)
                    {
                        _journal.Append(new JournalEntry(
                            $"service.{serviceName}", RegistryOperation.HKLM, keyPath, "Start",
                            RegistryValueHint.DWord, prev.Value.ToString(), ValueExisted: true));
                    }
                    else
                    {
                        Optim.Core.Logging.FileLogger.Warn($"SetStartType {serviceName}: previous Start value not journaled (unexpected type).");
                    }
                }

                key.SetValue("Start", value, Microsoft.Win32.RegistryValueKind.DWord);

                // Delayed start only means something for Automatic; keep the
                // flag consistent instead of leaving a stale value behind.
                if (delayed == true)
                {
                    key.SetValue("DelayedAutostart", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
                else if (key.GetValue("DelayedAutostart") is not null)
                {
                    key.SetValue("DelayedAutostart", 0, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch
            {
                _journal?.TruncateTo(before);
                throw;
            }

            Optim.Core.Logging.FileLogger.Info($"Service {serviceName} start type -> {mode}");
            return true;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"SetStartType {serviceName}: {ex.Message}");
            return false;
        }
    }
}
