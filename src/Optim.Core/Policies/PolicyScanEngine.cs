using Microsoft.Win32;
using System.Text;
using Optim.Core.Tweaks;

namespace Optim.Core.Policies;

public sealed record PolicyFinding(string Path, string ValueName, object? Value, bool IsPolicyKey);

/// <summary>
/// Scans the Policies trees for overrides so users can see everything that is
/// being forced by policy — including keys this app or other tools set.
/// </summary>
public static class PolicyScanEngine
{
    private static readonly (RegistryKey Root, string Name, string Path)[] Trees =
    {
        (Registry.LocalMachine, "HKLM", @"SOFTWARE\Policies\Microsoft\Windows"),
        (Registry.CurrentUser, "HKCU", @"Software\Policies\Microsoft\Windows"),
        (Registry.LocalMachine, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies"),
        (Registry.CurrentUser, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies")
    };

    public static IReadOnlyList<PolicyFinding> Scan()
    {
        var findings = new List<PolicyFinding>();
        foreach (var (root, hiveName, tree) in Trees)
        {
            try
            {
                ScanTree(root, hiveName, tree, findings, depth: 0);
            }
            catch (Exception ex)
            {
                Optim.Core.Logging.FileLogger.Warn($"Policy scan {tree}: {ex.Message}");
            }
        }

        return findings;
    }

    public static string ExportReport(IEnumerable<PolicyFinding> findings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Optim policy scan — " + DateTime.Now);
        sb.AppendLine(new string('=', 40));
        foreach (var f in findings)
        {
            var friendly = FriendlyNameFor(f);
            sb.AppendLine(friendly is null
                ? $"{f.Path} :: {f.ValueName} = {f.Value ?? "(default)"}"
                : $"{friendly}{Environment.NewLine}  {f.Path} :: {f.ValueName} = {f.Value ?? "(default)"}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Plain-language label for well-known policy values (documented Windows
    /// policy registry values). Null when the value is not in the known list —
    /// the UI then shows the raw path.
    /// </summary>
    public static string? FriendlyNameFor(PolicyFinding finding)
    {
        var rel = RelativePath(finding.Path);
        if (rel is null)
        {
            return null;
        }

        return FriendlyNames.TryGetValue(rel + "|" + finding.ValueName, out var name) ? name : null;
    }

    /// <summary>Stable journal id so "Revert all" can restore a removed policy value.</summary>
    public static string PolicyIdFor(PolicyFinding finding) => "policy:" + finding.Path + "::" + finding.ValueName;

    /// <summary>
    /// Removes one policy value with journal-before-write (restorable via
    /// "Revert all"), then prunes the key when it is left completely empty.
    /// Returns success plus a human-readable outcome.
    /// </summary>
    public static (bool Ok, string Message) Remove(PolicyFinding finding, RegistryTweakEngine engine)
    {
        var hive = finding.Path.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase)
            ? RegistryOperation.HKLM
            : finding.Path.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase)
                ? RegistryOperation.HKCU
                : null;
        if (hive is null)
        {
            return (false, "Unknown hive — refusing to touch it.");
        }

        var keyPath = finding.Path.Substring(5);
        var valueName = finding.ValueName == "(Default)" ? null : finding.ValueName;

        try
        {
            engine.DeleteValueJournaled(PolicyIdFor(finding), hive, keyPath, valueName);
            var roots = Trees.Where(t => t.Name == hive).Select(t => t.Path);
            engine.DeleteKeyTreeIfEmpty(hive, keyPath, roots);
            return (true, "Removed and journaled — Settings → Revert all can restore it.");
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"Policy remove {finding.Path} [{finding.ValueName}]: {ex.Message}");
            return (false, "Removal failed — see logs.");
        }
    }

    /// <summary>Path relative to the scan roots, e.g. "Windows\WindowsUpdate\AU".</summary>
    private static string? RelativePath(string fullPath)
    {
        foreach (var (_, _, root) in Trees)
        {
            foreach (var prefix in new[] { "HKLM\\" + root, "HKCU\\" + root })
            {
                if (fullPath.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    return fullPath.Substring(prefix.Length + 1);
                }

                if (fullPath.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return "";
                }
            }
        }

        return null;
    }

    // Key: relative path + "|" + value name. Values are documented Windows
    // policy registry values; labels are our own wording.
    private static readonly Dictionary<string, string> FriendlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"WindowsUpdate\AU|AUOptions"] = "Windows Update automatic-update behavior",
        [@"WindowsUpdate\AU|NoAutoRebootWithLoggedOnUsers"] = "Block auto-restart while signed in",
        [@"WindowsUpdate|ExcludeWUDriversInQualityUpdate"] = "Exclude drivers from Windows Update",
        [@"Windows Search|AllowCortana"] = "Cortana",
        [@"Windows Search|ConnectedSearchUseWeb"] = "Search web results and highlights",
        [@"DataCollection|AllowTelemetry"] = "Diagnostic data (telemetry) level",
        [@"Explorer|DisableNotificationCenter"] = "Notification center",
        [@"Explorer|DisableSearchBoxSuggestions"] = "Taskbar search suggestions",
        [@"Explorer|NoDriveTypeAutoRun"] = "AutoRun / AutoPlay behavior",
        [@"CloudContent|DisableWindowsConsumerFeatures"] = "Suggested apps and consumer features",
        [@"DeliveryOptimization|DODownloadMode"] = "Delivery Optimization download mode",
        [@"System|EnableActivityFeed"] = "Timeline activity feed",
        [@"System|PublishUserActivities"] = "Publish user activities",
        [@"System|UploadUserActivities"] = "Upload user activities",
        [@"System|DisableAcrylicBackgroundOnLogon"] = "Acrylic effect on sign-in screen",
        [@"Dsh|AllowNewsAndInterests"] = "News and interests",
        [@"WindowsCopilot|TurnOffWindowsCopilot"] = "Windows Copilot",
        [@"WindowsAI|DisableAIDataAnalysis"] = "AI data analysis",
        [@"SQMClient\Windows|CEIPEnable"] = "Customer Experience Improvement Program",
        [@"GameDVR|AllowGameDVR"] = "Game DVR",
        [@"Windows Chat|ChatIcon"] = "Chat taskbar icon",
        [@"Windows Defender|DisableAntiSpyware"] = "Microsoft Defender Antivirus",
        [@"Windows Defender\Real-Time Protection|DisableRealtimeMonitoring"] = "Defender real-time protection",
    };

    private static void ScanTree(RegistryKey root, string hiveName, string path, List<PolicyFinding> into, int depth)
    {
        if (depth > 6)
        {
            return;
        }

        using var key = SafeOpen(root, path);
        if (key is null)
        {
            return;
        }

        foreach (var name in SafeNames(key.GetValueNames))
        {
            try
            {
                into.Add(new PolicyFinding($"{hiveName}\\{path}", name.Length == 0 ? "(Default)" : name,
                    key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), true));
            }
            catch (Exception ex)
            {
                Optim.Core.Logging.FileLogger.Warn($"Policy value {path}\\{name}: {ex.Message}");
            }
        }

        // One denied subkey must not abort the rest of the tree.
        foreach (var sub in SafeNames(key.GetSubKeyNames))
        {
            try
            {
                ScanTree(root, hiveName, $@"{path}\{sub}", into, depth + 1);
            }
            catch (Exception ex)
            {
                Optim.Core.Logging.FileLogger.Warn($"Policy key {path}\\{sub}: {ex.Message}");
            }
        }
    }

    private static RegistryKey? SafeOpen(RegistryKey root, string path)
    {
        try { return root.OpenSubKey(path); }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Warn($"Policy open {path}: {ex.Message}");
            return null;
        }
    }

    private static string[] SafeNames(Func<string[]> getter)
    {
        try { return getter(); }
        catch { return Array.Empty<string>(); }
    }
}
