using System.Management;

namespace Optim.Core.SystemInfo;

public sealed record OsInfo(string Caption, string Version, string Build, string Architecture, string InstallDate);
public sealed record CpuInfo(string Name, int Cores, int LogicalProcessors, string MaxClockMhz);
public sealed record GpuInfo(string Name, string DriverVersion, long AdapterRamBytes);
public sealed record MemorySlot(string BankLabel, string Capacity, string Speed, long CapacityBytes);
public sealed record DiskInfo(string Model, long SizeBytes, string Interface);
public sealed record SystemInventory(OsInfo Os, CpuInfo Cpu, IReadOnlyList<GpuInfo> Gpus, IReadOnlyList<MemorySlot> Memory, IReadOnlyList<DiskInfo> Disks, string HostName, string UserName);

/// <summary>Reads hardware and OS inventory through WMI.</summary>
public static class SystemInfoEngine
{
    public static SystemInventory Read()
    {
        var os = Query("Win32_OperatingSystem", "Caption,Version,BuildNumber,OSArchitecture,InstallDate");
        var cpu = Query("Win32_Processor", "Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed");
        var gpu = Query("Win32_VideoController", "Name,DriverVersion,AdapterRAM");
        var mem = Query("Win32_PhysicalMemory", "BankLabel,Capacity,Speed");
        var disk = Query("Win32_DiskDrive", "Model,Size,InterfaceType");

        var osRow = os.FirstOrDefault() ?? new Dictionary<string, object?>();
        var cpuRow = cpu.FirstOrDefault() ?? new Dictionary<string, object?>();

        return new SystemInventory(
            new OsInfo(
                Str(osRow, "Caption", "Windows"),
                Str(osRow, "Version", "?"),
                Str(osRow, "BuildNumber", "?"),
                Str(osRow, "OSArchitecture", "?"),
                Str(osRow, "InstallDate", "?")),
            new CpuInfo(
                Str(cpuRow, "Name", "Unknown CPU"),
                Int(cpuRow, "NumberOfCores"),
                Int(cpuRow, "NumberOfLogicalProcessors"),
                $"{Int(cpuRow, "MaxClockSpeed")} MHz"),
            gpu.Select(r => new GpuInfo(
                Str(r, "Name", "Unknown GPU"),
                Str(r, "DriverVersion", "?"),
                Long(r, "AdapterRAM"))).ToList(),
            mem.Select(r =>
            {
                var bytes = Long(r, "Capacity");
                return new MemorySlot(
                    Str(r, "BankLabel", "Bank"),
                    FormatBytes(bytes),
                    $"{Int(r, "Speed")} MHz",
                    bytes);
            }).ToList(),
            disk.Select(r => new DiskInfo(
                Str(r, "Model", "Disk"),
                Long(r, "Size"),
                Str(r, "InterfaceType", "?"))).ToList(),
            Environment.MachineName,
            Environment.UserName);
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        <= 0 => "—",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
        < 1024L * 1024 * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:F1} GB",
        _ => $"{bytes / 1024.0 / 1024 / 1024 / 1024:F2} TB"
    };

    private static List<Dictionary<string, object?>> Query(string wmiClass, string properties)
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {properties} FROM {wmiClass}");
            foreach (var o in searcher.Get())
            {
                var row = new Dictionary<string, object?>();
                foreach (var p in properties.Split(','))
                {
                    row[p.Trim()] = o[p.Trim()] is System.Management.ManagementBaseObject ? null : o[p.Trim()];
                }
                rows.Add(row);
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"WMI query {wmiClass} failed: {ex.Message}");
        }
        return rows;
    }

    private static string Str(Dictionary<string, object?> row, string key, string fallback) =>
        row.TryGetValue(key, out var v) && v is not null ? v.ToString() ?? fallback : fallback;

    private static int Int(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && uint.TryParse(v?.ToString(), out var i) ? (int)i : 0;

    private static long Long(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && long.TryParse(v?.ToString(), out var l) ? l : 0;
}
