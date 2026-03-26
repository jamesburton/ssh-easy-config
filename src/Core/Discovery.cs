using Makaretu.Dns;

namespace SshEasyConfig.Core;

public static class Discovery
{
    public const string ServiceType = "_ssh-easy._tcp.local";
    private const string ServiceName = "_ssh-easy._tcp";

    public record ServiceProfile(string HostName, int Port, string InstanceName);

    public static ServiceProfile CreateServiceProfile(string hostName, int port)
    {
        var instanceName = $"ssh-easy-config on {hostName}";
        return new ServiceProfile(hostName, port, instanceName);
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
            foreach (var record in msg.AdditionalRecords)
            {
                if (record is SRVRecord srv)
                {
                    hostName = srv.Target.ToString();
                    port = srv.Port;
                }
            }
            if (hostName is not null && port > 0)
                results.Add(new ServiceProfile(hostName, port, args.ServiceInstanceName.ToString()));
        };

        mdns.Start();
        sd.QueryServiceInstances(ServiceName);
        try { await Task.Delay(timeout, cts.Token); }
        catch (OperationCanceledException) { }
        return results;
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
