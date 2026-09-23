using System.Runtime.InteropServices;

namespace Optim.Core.SystemDeployment;

/// <summary>
/// Best-effort System Restore point creation via srclient.dll (no extra
/// dependencies). Returns false when System Protection is off or the call
/// fails — callers must surface that instead of assuming safety.
/// </summary>
public static class RestorePoint
{
    private const int BeginSystemChange = 100;
    private const int EndSystemChange = 101;
    private const int ModifySettings = 12;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RestorePointInfo
    {
        public int EventType;
        public int RestorePointType;
        public long SequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StateManagerStatus
    {
        public int Status;
        public long SequenceNumber;
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SRSetRestorePointW(ref RestorePointInfo spec, out StateManagerStatus status);

    public static bool TryCreate(string description)
    {
        try
        {
            var safe = (description ?? "Optim").Trim();
            if (safe.Length > 60)
            {
                safe = safe[..60];
            }

            var begin = new RestorePointInfo
            {
                EventType = BeginSystemChange,
                RestorePointType = ModifySettings,
                Description = safe
            };
            if (!SRSetRestorePointW(ref begin, out var beginStatus) || beginStatus.Status != 0)
            {
                return false;
            }

            var end = new RestorePointInfo
            {
                EventType = EndSystemChange,
                RestorePointType = ModifySettings,
                SequenceNumber = beginStatus.SequenceNumber,
                Description = safe
            };
            return SRSetRestorePointW(ref end, out var endStatus) && endStatus.Status == 0;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"RestorePoint: {ex.Message}");
            return false;
        }
    }
}
