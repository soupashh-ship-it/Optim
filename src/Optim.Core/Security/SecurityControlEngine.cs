using Microsoft.Win32;

namespace Optim.Core.Security;

/// <summary>
/// Local security-policy controls backing the Security page toggles. Every
/// write is a Windows-policy value, so tamper protection / Defender itself may
/// refuse or revert the change; failures are reported honestly instead of
/// pretending success. Enabling always means "remove the disabling policy and
/// restore Windows defaults", never "force something on that Windows did not
/// put there".
/// </summary>
public static class SecurityControlEngine
{
    public const string DefenderPolicies = @"SOFTWARE\Policies\Microsoft\Windows Defender";
    public const string DefenderRealtimePolicies = @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection";
    public const string SmartScreenExplorer = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
    public const string SmartScreenPolicy = @"SOFTWARE\Policies\Microsoft\Windows\System";
    public const string UacPolicies = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

    /// <summary>Enable/disable Defender antispyware via its policy value. Enabling deletes the policy (Windows default is on).</summary>
    public static (bool Ok, string Detail) SetDefenderEnabled(bool enable)
    {
        const string valueName = "DisableAntiSpyware";
        try
        {
            if (enable)
            {
                using var key = Registry.LocalMachine.OpenSubKey(DefenderPolicies, writable: true);
                key?.DeleteValue(valueName, throwOnMissingValue: false);
            }
            else
            {
                using var key = Registry.LocalMachine.CreateSubKey(DefenderPolicies, writable: true);
                key.SetValue(valueName, 1, RegistryValueKind.DWord);
            }

            Optim.Core.Logging.FileLogger.Info($"Security policy: Defender antivirus {(enable ? "policy removed (default on)" : "disabled by policy")}.");
            return (true, enable
                ? "Policy removed — Windows default (Defender on) applies. A restart may be needed."
                : "Defender disabled by policy. Tamper protection may refuse or undo this — that is Windows protecting itself, by design.");
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"SetDefenderEnabled({enable}): {ex.Message}");
            return (false, $"Registry write failed: {ex.Message}. Run elevated and check tamper protection.");
        }
    }

    /// <summary>Enable/disable Defender real-time monitoring via its policy value.</summary>
    public static (bool Ok, string Detail) SetRealtimeEnabled(bool enable)
    {
        const string valueName = "DisableRealtimeMonitoring";
        try
        {
            if (enable)
            {
                using var key = Registry.LocalMachine.OpenSubKey(DefenderRealtimePolicies, writable: true);
                key?.DeleteValue(valueName, throwOnMissingValue: false);
            }
            else
            {
                using var key = Registry.LocalMachine.CreateSubKey(DefenderRealtimePolicies, writable: true);
                key.SetValue(valueName, 1, RegistryValueKind.DWord);
            }

            Optim.Core.Logging.FileLogger.Info($"Security policy: real-time protection {(enable ? "policy removed (default on)" : "disabled by policy")}.");
            return (true, enable
                ? "Policy removed — real-time protection returns to Windows default (on)."
                : "Real-time monitoring disabled by policy. Tamper protection may undo this.");
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"SetRealtimeEnabled({enable}): {ex.Message}");
            return (false, $"Registry write failed: {ex.Message}. Run elevated and check tamper protection.");
        }
    }

    /// <summary>
    /// Enable/disable SmartScreen for apps and files. Enabling restores the
    /// default ("Warn") and removes any force-off policy; disabling writes the
    /// Explorer value Windows itself uses.
    /// </summary>
    public static (bool Ok, string Detail) SetSmartScreenEnabled(bool enable)
    {
        try
        {
            using (var explorer = Registry.LocalMachine.CreateSubKey(SmartScreenExplorer, writable: true))
            {
                explorer.SetValue("SmartScreenEnabled", enable ? "Warn" : "Off", RegistryValueKind.String);
            }

            if (enable)
            {
                using var policy = Registry.LocalMachine.OpenSubKey(SmartScreenPolicy, writable: true);
                policy?.DeleteValue("EnableSmartScreen", throwOnMissingValue: false);
            }

            Optim.Core.Logging.FileLogger.Info($"Security policy: SmartScreen {(enable ? "enabled (Warn)" : "disabled")}.");
            return (true, enable ? "SmartScreen restored to its default state." : "SmartScreen turned off for apps and files.");
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"SetSmartScreenEnabled({enable}): {ex.Message}");
            return (false, $"Registry write failed: {ex.Message}. Run elevated first.");
        }
    }

    /// <summary>
    /// Enable/disable UAC (EnableLUA). Applies after a restart; disabling UAC
    /// removes the elevation prompt for every app and is not recommended.
    /// </summary>
    public static (bool Ok, string Detail) SetUacEnabled(bool enable)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(UacPolicies, writable: true);
            key.SetValue("EnableLUA", enable ? 1 : 0, RegistryValueKind.DWord);

            Optim.Core.Logging.FileLogger.Info($"Security policy: UAC {(enable ? "enabled" : "disabled")} (restart required).");
            return (true, enable
                ? "UAC enabled — sign out or restart to apply."
                : "UAC disabled — restart to apply. Every app can now change the system silently.");
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"SetUacEnabled({enable}): {ex.Message}");
            return (false, $"Registry write failed: {ex.Message}. Run elevated first.");
        }
    }
}
