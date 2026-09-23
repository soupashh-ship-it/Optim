using Microsoft.Win32;

namespace Optim.Core.Startup;

public sealed record StartupItem(string Name, string Command, string Location, bool Enabled, bool IsMachineWide = false, bool CanToggle = true);

/// <summary>Enumerates startup entries from Run and RunOnce keys, the per-user
/// and common startup folders. RunOnce rows are listed read-only: Windows runs
/// them once and deletes them, so toggling or deleting from an optimizer would
/// change one-time installer behavior.</summary>
public static class StartupEngine
{
    private const string RunUser = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunMachine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceUser = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string RunOnceMachine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    // 32-bit apps write Run under the Wow6432Node view on 64-bit Windows.
    private const string RunMachineWow64 = @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceMachineWow64 = @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ApprovedUser = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedMachine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static IReadOnlyList<StartupItem> List()
    {
        var items = new List<StartupItem>();

        CollectFromRun(Registry.CurrentUser, RunUser, "HKCU\\Run", items);
        CollectFromRun(Registry.LocalMachine, RunMachine, "HKLM\\Run", items);
        // 32-bit autostarts only exist on 64-bit Windows; skip the duplicate
        // view otherwise.
        if (Environment.Is64BitOperatingSystem)
        {
            CollectFromRun(Registry.LocalMachine, RunMachineWow64, "HKLM\\Run (32-bit)", items);
        }
        // RunOnce entries run a single time and Windows deletes them; they have
        // no StartupApproved flag, so they are surfaced read-only.
        CollectFromRun(Registry.CurrentUser, RunOnceUser, "HKCU\\RunOnce", items, readOnly: true);
        CollectFromRun(Registry.LocalMachine, RunOnceMachine, "HKLM\\RunOnce", items, readOnly: true);
        if (Environment.Is64BitOperatingSystem)
        {
            CollectFromRun(Registry.LocalMachine, RunOnceMachineWow64, "HKLM\\RunOnce (32-bit)", items, readOnly: true);
        }

        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(folder))
            {
                foreach (var f in Directory.EnumerateFiles(folder))
                {
                    // Folder shortcuts do not have StartupApproved flags: the
                    // toggle is disabled and Delete removes the file instead.
                    items.Add(new StartupItem(Path.GetFileNameWithoutExtension(f), f, "Startup folder", true, CanToggle: false));
                }
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Startup folder: {ex.Message}");
        }

        try
        {
            var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            if (Directory.Exists(common))
            {
                foreach (var f in Directory.EnumerateFiles(common))
                {
                    // All-users folder shortcuts: also file-backed, no approval
                    // flag. Machine-wide because every account runs them.
                    items.Add(new StartupItem(Path.GetFileNameWithoutExtension(f), f, "Common startup folder", true, IsMachineWide: true, CanToggle: false));
                }
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Common startup folder: {ex.Message}");
        }

        return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CollectFromRun(RegistryKey root, string path, string label, List<StartupItem> items, bool readOnly = false)
    {
        var isMachine = ReferenceEquals(root, Registry.LocalMachine);
        try
        {
            using var key = root.OpenSubKey(path);
            if (key is null)
            {
                return;
            }

            foreach (var name in key.GetValueNames())
            {
                var command = key.GetValue(name)?.ToString() ?? "";
                // RunOnce has no approval flag; list enabled but untoggleable so
                // bulk Enable/Disable skips these rows too.
                var enabled = readOnly || IsApproved(root, name);
                items.Add(new StartupItem(name, command, label, enabled, isMachine, CanToggle: !readOnly));
            }
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Run key {label}: {ex.Message}");
        }
    }

    private static bool IsApproved(RegistryKey root, string name)
    {
        try
        {
            var approvedPath = ReferenceEquals(root, Registry.LocalMachine) ? ApprovedMachine : ApprovedUser;
            using var key = root.OpenSubKey(approvedPath);
            var raw = key?.GetValue(name) as byte[];
            if (raw is { Length: >= 4 })
            {
                var flags = BitConverter.ToInt32(raw, 0);
                return (flags & 1) == 0; // bit 0 set = disabled
            }
        }
        catch
        {
        }

        return true;
    }

    /// <summary>Enables or disables a Run entry via its matching StartupApproved flags.</summary>
    public static bool SetEnabled(string name, bool enabled, bool isMachineWide = false)
    {
        try
        {
            var root = isMachineWide ? Registry.LocalMachine : Registry.CurrentUser;
            var approvedPath = isMachineWide ? ApprovedMachine : ApprovedUser;
            using var key = root.CreateSubKey(approvedPath, writable: true);
            if (key is null)
            {
                return false;
            }

            var flags = enabled ? (byte)0x02 : (byte)0x03;
            key.SetValue(name, new byte[] { flags, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, RegistryValueKind.Binary);
            return true;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"SetEnabled {name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Adds a new startup entry to the per-user Run key.</summary>
    public static bool Add(string name, string command, bool isMachineWide = false)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        try
        {
            var root = isMachineWide ? Registry.LocalMachine : Registry.CurrentUser;
            var runPath = isMachineWide ? RunMachine : RunUser;
            using var key = root.CreateSubKey(runPath, writable: true);
            if (key is null)
            {
                return false;
            }

            key.SetValue(name.Trim(), command.Trim(), RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Startup add {name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes a startup entry. Run-key values are deleted from the registry;
    /// Startup-folder rows delete the shortcut file itself (requires the full
    /// file path as <paramref name="name"/> when <paramref name="isFolderFile"/>).
    /// </summary>
    public static bool Remove(string name, bool isMachineWide = false, bool isFolderFile = false, string? folderPath = null)
    {
        if (isFolderFile)
        {
            return RemoveFolderFile(name, folderPath);
        }

        try
        {
            var root = isMachineWide ? Registry.LocalMachine : Registry.CurrentUser;
            var runPath = isMachineWide ? RunMachine : RunUser;
            var approvedPath = isMachineWide ? ApprovedMachine : ApprovedUser;

            var removed = false;
            using (var key = root.OpenSubKey(runPath, writable: true))
            {
                if (key?.GetValue(name) is not null)
                {
                    key.DeleteValue(name, throwOnMissingValue: false);
                    removed = true;
                }
            }

            using (var approved = root.OpenSubKey(approvedPath, writable: true))
            {
                approved?.DeleteValue(name, throwOnMissingValue: false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Startup remove {name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Deletes a Startup-folder shortcut file. Only files inside the
    /// user's Startup folder are eligible.</summary>
    public static bool RemoveFolderFile(string name, string? folderPath)
    {
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (!Directory.Exists(folder))
            {
                return false;
            }

            string target = string.IsNullOrWhiteSpace(folderPath) || !Path.IsPathRooted(folderPath)
                ? Path.Combine(folder, name)
                : folderPath;
            var full = Path.GetFullPath(target);
            if (!full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !full.Equals(folder, StringComparison.OrdinalIgnoreCase))
            {
                Optim.Core.Logging.FileLogger.Warn($"Startup folder delete refused outside Startup folder: {full}");
                return false;
            }

            if (File.Exists(full))
            {
                File.Delete(full);
                return true;
            }

            // The grid passes the display name; try the common shortcut extension.
            var shortcut = Path.ChangeExtension(full, ".lnk");
            if (!full.Equals(shortcut, StringComparison.OrdinalIgnoreCase) && File.Exists(shortcut))
            {
                File.Delete(shortcut);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Startup folder remove {name}: {ex.Message}");
            return false;
        }
    }
}
