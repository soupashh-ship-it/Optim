using System.Diagnostics;

namespace Optim.Core.Metrics;

public sealed record UsageSample(float CpuPercent, float RamPercent, float DiskPercent, long RamUsedBytes, long RamTotalBytes)
{
    /// <summary>False when the disk performance counter is unavailable on this machine.</summary>
    public bool DiskAvailable => !float.IsNaN(DiskPercent);
}

/// <summary>
/// Live usage sampling for the Home page. Performance counters for CPU/disk,
/// GlobalMemoryStatusEx-style math for RAM.
/// </summary>
public sealed class MetricsEngine : IDisposable
{
    private readonly PerformanceCounter? _cpu;
    private readonly PerformanceCounter? _disk;

    public MetricsEngine()
    {
        // Each counter stands on its own: one unavailable counter must not
        // discard the other.
        try
        {
            _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            _cpu.NextValue();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"CPU performance counter unavailable: {ex.Message}");
            _cpu = null;
        }

        try
        {
            _disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", readOnly: true);
            _disk.NextValue();
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Disk performance counter unavailable: {ex.Message}");
            _disk = null;
        }
    }

    public UsageSample Sample()
    {
        // RAM via native memory status (accurate, no counter needed)
        var total = 0L;
        var used = 0L;
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            total = (long)memStatus.ullTotalPhys;
            used = (long)(memStatus.ullTotalPhys - memStatus.ullAvailPhys);
        }

        var cpu = 0f;
        try
        {
            cpu = _cpu?.NextValue() ?? 0f;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"CPU sample failed: {ex.Message}");
        }

        float disk;
        try
        {
            disk = _disk is null ? float.NaN : Math.Clamp(_disk.NextValue(), 0, 100);
        }
        catch
        {
            disk = float.NaN;
        }

        return new UsageSample(
            Math.Clamp(cpu, 0, 100),
            total > 0 ? (float)(100.0 * used / total) : 0f,
            Math.Clamp(disk, 0, 100),
            used,
            total);
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _disk?.Dispose();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
