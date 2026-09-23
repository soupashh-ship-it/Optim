using System.Runtime.InteropServices;
using Optim.Core.Logging;

namespace Optim.Core.Cleanup;

public sealed record CleanupTarget(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> Paths,
    bool IncludeRecycleBin = false);

public sealed record CleanupResult(
    string TargetId,
    string Title,
    long BytesFreed,
    int FilesRemoved,
    int DirectoriesRemoved,
    int Errors);

/// <summary>
/// One-click junk cleanup: temp folders, shader caches, recycle bin.
/// Only deletes inside well-known safe locations; every file op is guarded
/// so a locked file never aborts the run.
/// </summary>
public static class CleanupEngine
{
    public static IReadOnlyList<CleanupTarget> Targets { get; } = new[]
    {
        new CleanupTarget(
            "user-temp", "Temporary files",
            "Your per-user temp folder. Safe to empty at any time.",
            new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp") }),
        new CleanupTarget(
            "windows-temp", "Windows temp files",
            "System-wide temp folder. Needs elevation.",
            new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") }),
        new CleanupTarget(
            "shader-cache", "DirectX shader cache",
            "Cached shaders are rebuilt on demand by games and apps.",
            new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NVIDIA", "DXCache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD", "DxCache")
            }),
        new CleanupTarget(
            "recycle-bin", "Recycle Bin",
            "Permanently empties the Recycle Bin on all drives.",
            Array.Empty<string>(), IncludeRecycleBin: true)
    };

    /// <summary>Estimates reclaimable bytes without deleting anything.</summary>
    public static long Measure(CleanupTarget target)
    {
        if (target.IncludeRecycleBin)
        {
            return MeasureRecycleBin();
        }

        long total = 0;
        foreach (var path in target.Paths)
        {
            total += MeasureDirectory(path);
        }

        return total;
    }

    public static CleanupResult Clean(CleanupTarget target)
    {
        if (target.IncludeRecycleBin)
        {
            var ok = EmptyRecycleBin();
            return new CleanupResult(target.Id, target.Title, 0, 0, 0, ok ? 0 : 1);
        }

        long freed = 0;
        var files = 0;
        var dirs = 0;
        var errors = 0;
        foreach (var path in target.Paths)
        {
            var r = CleanDirectory(path);
            freed += r.BytesFreed;
            files += r.FilesRemoved;
            dirs += r.DirectoriesRemoved;
            errors += r.Errors;
        }

        return new CleanupResult(target.Id, target.Title, freed, files, dirs, errors);
    }

    private static long MeasureRecycleBin()
    {
        // Shell exposes no cheap size API; report 0 and count success instead.
        return 0;
    }

    private static bool EmptyRecycleBin()
    {
        try
        {
            var hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            if (hr == 0)
            {
                FileLogger.Info("Recycle Bin emptied.");
                return true;
            }

            FileLogger.Error($"EmptyRecycleBin HRESULT=0x{hr:X8}");
            return false;
        }
        catch (Exception ex)
        {
            FileLogger.Error($"EmptyRecycleBin: {ex.Message}");
            return false;
        }
    }

    public static long MeasureDirectory(string path)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { }
            }
        }
        catch
        {
        }

        return total;
    }

    public static CleanupResult CleanDirectory(string path)
    {
        long freed = 0;
        var files = 0;
        var dirs = 0;
        var errors = 0;

        IEnumerable<string> entries;
        try
        {
            if (!Directory.Exists(path))
            {
                return new CleanupResult("", "", 0, 0, 0, 0);
            }

            entries = Directory.EnumerateFileSystemEntries(path);
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"Cleanup {path}: {ex.Message}");
            return new CleanupResult("", "", 0, 0, 0, 1);
        }

        foreach (var entry in entries)
        {
            try
            {
                if (File.Exists(entry))
                {
                    long len;
                    try { len = new FileInfo(entry).Length; } catch { len = 0; }
                    File.Delete(entry);
                    freed += len;
                    files++;
                }
                else if (Directory.Exists(entry))
                {
                    freed += MeasureDirectory(entry);
                    Directory.Delete(entry, recursive: true);
                    dirs++;
                }
            }
            catch
            {
                // Locked/in-use files are skipped, never fatal.
                errors++;
            }
        }

        FileLogger.Info($"Cleaned {path}: {files} files, {dirs} folders, {freed} bytes.");
        return new CleanupResult("", "", freed, files, dirs, errors);
    }

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}
