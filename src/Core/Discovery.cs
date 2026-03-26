using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Makaretu.Dns;

namespace SshEasyConfig.Core;

public static class Discovery
{
    public const string ServiceType = "_ssh-easy._tcp.local";
    private const string ServiceName = "_ssh-easy._tcp";

    public record ServiceProfile(string HostName, int Port, string InstanceName, List<string> Addresses);

    public static ServiceProfile CreateServiceProfile(string hostName, int port)
    {
        var instanceName = $"ssh-easy-config on {hostName}";
        return new ServiceProfile(hostName, port, instanceName, []);
    }

    public static async Task<IDisposable> AdvertiseAsync(ServiceProfile profile)
    {
        var mdns = new MulticastService();
        var sd = new ServiceDiscovery(mdns);
        var serviceProfile = new Makaretu.Dns.ServiceProfile(
            profile.InstanceName, ServiceName, (ushort)profile.Port);
        sd.Advertise(serviceProfile);
        mdns.Start();
        return new AdvertisementHandle(mdns, sd);
    }

    public static async Task<List<ServiceProfile>> BrowseAsync(TimeSpan timeout)
    {
        var results = new List<ServiceProfile>();
        using var mdns = new MulticastService();
        var sd = new ServiceDiscovery(mdns);
        using var cts = new CancellationTokenSource(timeout);

        sd.ServiceInstanceDiscovered += (_, args) =>
        {
            var msg = args.Message;
            string? hostName = null;
            int port = 0;
            var addresses = new List<string>();

            foreach (var record in msg.AdditionalRecords)
            {
                if (record is SRVRecord srv)
                {
                    hostName = srv.Target.ToString();
                    port = srv.Port;
                }
                else if (record is ARecord a)
                {
                    addresses.Add(a.Address.ToString());
                }
                else if (record is AAAARecord aaaa)
                {
                    addresses.Add(aaaa.Address.ToString());
                }
            }

            if (hostName is not null && port > 0)
                results.Add(new ServiceProfile(hostName, port, args.ServiceInstanceName.ToString(), addresses));
        };

        mdns.Start();
        sd.QueryServiceInstances(ServiceName);
        try { await Task.Delay(timeout, cts.Token); }
        catch (OperationCanceledException) { }
        return results;
    }

    /// <summary>
    /// Gets all local IP addresses suitable for advertising.
    /// </summary>
    public static List<string> GetLocalAddresses()
    {
        var addresses = new List<string>();
        try
        {
            var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var addr in hostEntry.AddressList)
            {
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ||
                    addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    var s = addr.ToString();
                    // Skip loopback and link-local
                    if (!IPAddress.IsLoopback(addr) && !s.StartsWith("169.254.") && !s.StartsWith("fe80:"))
                        addresses.Add(s);
                }
            }
        }
        catch { }
        return addresses;
    }

    /// <summary>
    /// Gets all known hostnames/addresses for this machine, suitable for the
    /// advertise-host selection prompt. Includes machine name, mDNS .local name,
    /// Tailscale FQDN (if available), and local IP addresses.
    /// </summary>
    public static List<(string Address, string Label)> GetAdvertiseOptions()
    {
        var options = new List<(string Address, string Label)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string address, string label)
        {
            if (seen.Add(address))
                options.Add((address, label));
        }

        // 1. Machine name
        Add(Environment.MachineName, "Machine name");

        // 2. mDNS .local name
        var mdnsName = $"{Environment.MachineName}.local";
        Add(mdnsName, "mDNS");

        // 3. Tailscale FQDN and IPs (if available)
        var ts = GetTailscaleInfo();
        if (ts is not null)
        {
            if (ts.Value.Fqdn is not null)
                Add(ts.Value.Fqdn, "Tailscale");
            foreach (var ip in ts.Value.Addresses)
                Add(ip, "Tailscale IP");
        }

        // 4. Local IP addresses
        foreach (var addr in GetLocalAddresses())
            Add(addr, "Local IP");

        return options;
    }

    /// <summary>
    /// Detects Tailscale FQDN and IP addresses via `tailscale status --json`.
    /// Returns null if Tailscale is not installed or not running.
    /// </summary>
    public static (string? Fqdn, List<string> Addresses)? GetTailscaleInfo()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "tailscale",
                Arguments = "status --json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return null;

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Self", out var self))
                return null;

            string? fqdn = null;
            if (self.TryGetProperty("DNSName", out var dnsName))
            {
                fqdn = dnsName.GetString()?.TrimEnd('.');
                if (string.IsNullOrEmpty(fqdn))
                    fqdn = null;
            }

            var addresses = new List<string>();
            if (self.TryGetProperty("TailscaleIPs", out var ips) && ips.ValueKind == JsonValueKind.Array)
            {
                foreach (var ip in ips.EnumerateArray())
                {
                    var addr = ip.GetString();
                    if (addr is not null)
                        addresses.Add(addr);
                }
            }

            return (fqdn, addresses);
        }
        catch
        {
            return null;
        }
    }

    private sealed class AdvertisementHandle(MulticastService mdns, ServiceDiscovery sd) : IDisposable
    {
        public void Dispose()
        {
            sd.Dispose();
            mdns.Stop();
            mdns.Dispose();
        }
    }
}
