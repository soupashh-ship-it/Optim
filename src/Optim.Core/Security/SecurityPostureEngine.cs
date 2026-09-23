using Microsoft.Win32;

namespace Optim.Core.Security;

public sealed record SecurityItem(string Name, string State, bool IsRecommended, string Detail);

/// <summary>Read-only view of local security posture. This page never flips settings.</summary>
public static class SecurityPostureEngine
{
    public static IReadOnlyList<SecurityItem> Read()
    {
        var items = new List<SecurityItem>
        {
            DefenderStatus(),
            SmartScreenStatus(),
            UacStatus(),
            FirewallStatus(),
            DefenderRealtimeStatus(),
            TamperProtectionStatus()
        };
        return items;
    }

    private static SecurityItem DefenderStatus()
    {
        // Absent policy key genuinely means "not disabled by policy".
        // Only an unreadable key (e.g. denied when unelevated) is Unknown.
        return TryReadDword(@"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware") switch
        {
            (_, false) => Unknown("Microsoft Defender Antivirus"),
            (1, _) => new SecurityItem("Microsoft Defender Antivirus", "Disabled by policy", false,
                "A policy key is disabling Defender."),
            _ => new SecurityItem("Microsoft Defender Antivirus", "Enabled (policy)", true,
                "Defender is not disabled by group policy.")
        };
    }

    private static SecurityItem DefenderRealtimeStatus()
    {
        return TryReadDword(@"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring") switch
        {
            (_, false) => Unknown("Real-time protection"),
            (1, _) => new SecurityItem("Real-time protection", "Off by policy", false,
                "Real-time monitoring has been disabled."),
            _ => new SecurityItem("Real-time protection", "On", true,
                "Real-time scanning is active.")
        };
    }

    private static SecurityItem TamperProtectionStatus()
    {
        return TryReadDword(@"SOFTWARE\Microsoft\Windows Defender\Features", "TamperProtection") switch
        {
            (_, false) => Unknown("Tamper protection"),
            (5, _) => new SecurityItem("Tamper protection", "On", true, "Blocks unauthorized changes to Defender."),
            (null, _) => Unknown("Tamper protection"),
            _ => new SecurityItem("Tamper protection", "Off", false, "Blocks unauthorized changes to Defender.")
        };
    }

    private static SecurityItem SmartScreenStatus()
    {
        return TryReadDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled") switch
        {
            (_, false) => Unknown("SmartScreen"),
            (null, _) => new SecurityItem("SmartScreen", "On (default)", true,
                "Screens downloads and apps against reputation."),
            (2, _) => new SecurityItem("SmartScreen", "Off", false,
                "Screens downloads and apps against reputation."),
            _ => new SecurityItem("SmartScreen", "On", true,
                "Screens downloads and apps against reputation.")
        };
    }

    private static SecurityItem UacStatus()
    {
        return TryReadDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA") switch
        {
            (_, false) => Unknown("User Account Control"),
            (1, _) => new SecurityItem("User Account Control", "On", true,
                "UAC elevation prompts are active."),
            (null, _) => new SecurityItem("User Account Control", "On (default)", true,
                "UAC elevation prompts are active."),
            _ => new SecurityItem("User Account Control", "Off", false,
                "UAC has been disabled — not recommended.")
        };
    }

    private static SecurityItem FirewallStatus()
    {
        // Check all three profiles: reporting "On" while the public profile is
        // off would be a fail-open lie.
        var domain = TryReadDword(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\DomainProfile", "EnableFirewall");
        var privateProfile = TryReadDword(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile", "EnableFirewall");
        var publicProfile = TryReadDword(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile", "EnableFirewall");
        if (!domain.Readable || !privateProfile.Readable || !publicProfile.Readable)
        {
            return Unknown("Windows Firewall");
        }

        if (domain.Value is null || privateProfile.Value is null || publicProfile.Value is null)
        {
            return Unknown("Windows Firewall");
        }

        var ok = domain.Value == 1 && privateProfile.Value == 1 && publicProfile.Value == 1;
        return new SecurityItem("Windows Firewall (all profiles)", ok ? "On" : "Off", ok,
            ok ? "Filters inbound connections on all network profiles." : "At least one firewall profile is off.");
    }

    private static SecurityItem Unknown(string name) =>
        new(name, "Unknown", false, "Could not be read — run elevated to get a definitive answer.");

    /// <summary>Reads a HKLM DWORD. Value null = key/value absent; Readable false = exception (denied).</summary>
    private static (int? Value, bool Readable) TryReadDword(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key?.GetValue(name) is not { } raw)
            {
                return (null, true);
            }

            int? parsed = raw switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var p) => p,
                byte[] b when b.Length >= 4 => BitConverter.ToInt32(b, 0),
                _ => null
            };
            return (parsed, true);
        }
        catch
        {
            return (null, false);
        }
    }
}
