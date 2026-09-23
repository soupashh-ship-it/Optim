using System.Net.NetworkInformation;
using System.Text;

namespace Optim.Core.Network;

public sealed record NetworkAdapterInfo(string Name, string InterfaceType, string Status, string IpAddress, string DnsServers);

public sealed record DnsProfile(string Name, string Primary, string Secondary, string PrimaryV6, string SecondaryV6);

/// <summary>Adapter info plus DNS management via netsh (IPv4 and IPv6).</summary>
public sealed class NetworkEngine
{
    public static IReadOnlyList<DnsProfile> KnownProfiles { get; } = new[]
    {
        new DnsProfile("Cloudflare", "1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"),
        new DnsProfile("Cloudflare Family", "1.1.1.3", "1.0.0.3", "2606:4700:4700::1113", "2606:4700:4700::1003"),
        new DnsProfile("Google", "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"),
        new DnsProfile("Quad9", "9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::fe:9"),
        new DnsProfile("OpenDNS", "208.67.222.222", "208.67.220.220", "2620:119:35::35", "2620:119:53::53"),
        new DnsProfile("AdGuard", "94.140.14.14", "94.140.15.15", "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff")
    };

    public static IReadOnlyList<NetworkAdapterInfo> ListAdapters()
    {
        var list = new List<NetworkAdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var props = nic.GetIPProperties();
                var ip = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString() ?? "—";
                var dns = string.Join(", ", props.DnsAddresses
                    .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.ToString()));

                list.Add(new NetworkAdapterInfo(nic.Name, nic.NetworkInterfaceType.ToString(), nic.OperationalStatus.ToString(), ip, string.IsNullOrEmpty(dns) ? "automatic" : dns));
            }
            catch (Exception ex)
            {
                Optim.Core.Logging.FileLogger.Warn($"Adapter {nic.Name}: {ex.Message}");
            }
        }

        return list;
    }

    /// <summary>
    /// Applies a DNS profile to both stacks, then verifies by re-reading the
    /// adapter. Returns which families were set so the UI can report honestly
    /// (IPv6 may legitimately be absent on an IPv4-only interface).
    /// </summary>
    public async Task<(bool V4, bool V6, string Detail)> ApplyDnsAsync(string adapterName, DnsProfile profile)
    {
        if (adapterName.Contains('"'))
        {
            throw new ArgumentException("Adapter name contains an invalid character.", nameof(adapterName));
        }

        var primary = await RunAsync("netsh", $@"interface ip set dns name=""{adapterName}"" source=static address={profile.Primary} validate=no");
        var secondary = primary
            && await RunAsync("netsh", $@"interface ip add dns name=""{adapterName}"" address={profile.Secondary} index=2 validate=no");
        var v4 = primary && secondary;

        // IPv6 is best-effort: absence of an IPv6 stack is not a failure.
        var v6Primary = await RunAsync("netsh", $@"interface ipv6 set dnsservers name=""{adapterName}"" source=static address={profile.PrimaryV6} validate=no");
        var v6Secondary = v6Primary
            && await RunAsync("netsh", $@"interface ipv6 add dnsservers name=""{adapterName}"" address={profile.SecondaryV6} index=2 validate=no");
        var v6 = v6Primary && v6Secondary;

        var detail = VerifyDns(adapterName, profile, v4, v6);
        return (v4, v6, detail);
    }

    public async Task<bool> ResetDnsAsync(string adapterName)
    {
        if (adapterName.Contains('"'))
        {
            throw new ArgumentException("Adapter name contains an invalid character.", nameof(adapterName));
        }

        var v4 = await RunAsync("netsh", $@"interface ip set dns name=""{adapterName}"" source=dhcp");
        // IPv6 DHCP reset wipes any static v6 servers too.
        var v6 = await RunAsync("netsh", $@"interface ipv6 set dnsservers name=""{adapterName}"" source=dhcp");
        return v4 && v6;
    }

    public static async Task<int> FlushDnsAsync()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null)
            {
                return -1;
            }

            var exited = await Task.Run(() => p.WaitForExit(30_000));
            if (!exited)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return -1;
            }

            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"ipconfig /flushdns: {ex.Message}");
            return -1;
        }
    }

    /// <summary>Re-reads the adapter and reports which expected servers are live.</summary>
    private static string VerifyDns(string adapterName, DnsProfile profile, bool v4, bool v6)
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Name.Equals(adapterName, StringComparison.OrdinalIgnoreCase));
            if (nic is null)
            {
                return "Adapter not found after apply.";
            }

            var servers = nic.GetIPProperties().DnsAddresses.Select(a => a.ToString()).ToList();
            var want = new[] { profile.Primary, profile.Secondary };
            var live = want.Count(w => servers.Contains(w, StringComparer.OrdinalIgnoreCase));
            var v6hint = v6 ? " IPv6 set." : " IPv6 unchanged/absent.";
            return v4
                ? $"Verified {live}/2 IPv4 servers live on '{adapterName}'.{v6hint}"
                : $"IPv4 apply failed on '{adapterName}'.{v6hint}";
        }
        catch (Exception ex)
        {
            return $"Applied, but verification read failed: {ex.Message}";
        }
    }

    private static async Task<bool> RunAsync(string fileName, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(fileName, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null)
            {
                return false;
            }

            // Bound the wait: a hung netsh (e.g. a validation prompt) must not hang the UI forever.
            var exited = await Task.Run(() => p.WaitForExit(30_000));
            if (!exited)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                Optim.Core.Logging.FileLogger.Error($"{fileName} {args}: timed out.");
                return false;
            }

            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Optim.Core.Logging.FileLogger.Error($"{fileName} {args}: {ex.Message}");
            return false;
        }
    }
}
