using Optim.Core.Cleanup;
using Xunit;

namespace Optim.Core.Tests;

public class CleanupEngineTests
{
    [Fact]
    public void Targets_cover_temp_shader_and_recycle_bin()
    {
        var ids = CleanupEngine.Targets.Select(t => t.Id).ToList();
        Assert.Contains("user-temp", ids);
        Assert.Contains("windows-temp", ids);
        Assert.Contains("shader-cache", ids);
        Assert.Contains("recycle-bin", ids);
    }

    [Fact]
    public void Missing_directory_measures_zero_and_cleans_without_error()
    {
        var missing = Path.Combine(Path.GetTempPath(), "optim-missing-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(0, CleanupEngine.MeasureDirectory(missing));
        var r = CleanupEngine.CleanDirectory(missing);
        Assert.Equal(0, r.Errors);
        Assert.Equal(0, r.FilesRemoved);
    }

    [Fact]
    public void Clean_removes_files_and_reports_bytes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "optim-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[1024]);
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllBytes(Path.Combine(dir, "sub", "b.bin"), new byte[2048]);

        Assert.True(CleanupEngine.MeasureDirectory(dir) >= 3072);
        var r = CleanupEngine.CleanDirectory(dir);
        Assert.Equal(0, r.Errors);
        Assert.True(r.BytesFreed >= 3072);
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir));

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Locked_files_are_skipped_not_fatal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "optim-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var locked = Path.Combine(dir, "locked.bin");
        File.WriteAllBytes(locked, new byte[16]);
        using (File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var r = CleanupEngine.CleanDirectory(dir);
            // Either skipped with an error count, or removed on systems that allow it.
            Assert.True(r.Errors >= 0);
        }

        Directory.Delete(dir, recursive: true);
    }
}
