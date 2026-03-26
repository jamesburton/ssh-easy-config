using System.Net;
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
