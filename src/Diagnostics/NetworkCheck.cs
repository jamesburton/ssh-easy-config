using System.Net;
using System.Net.Sockets;

namespace SshEasyConfig.Diagnostics;

public static class NetworkCheck
{
    public static async Task<List<DiagnosticResult>> CheckDnsAsync(string host)
    {
        var results = new List<DiagnosticResult>();

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            var addressList = string.Join(", ", addresses.Select(a => a.ToString()));
            results.Add(new DiagnosticResult(
                "DNS Resolution",
                CheckStatus.Pass,
                $"{host} resolves to {addressList}"));
        }
        catch (SocketException ex)
        {
            results.Add(new DiagnosticResult(
                "DNS Resolution",
                CheckStatus.Fail,
                $"Cannot resolve {host}: {ex.Message}",
                "Check hostname spelling, DNS settings, or /etc/hosts"));
        }

        return results;
    }

    public static async Task<DiagnosticResult> CheckPortAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token);
            return new DiagnosticResult(
                "Port Connectivity",
                CheckStatus.Pass,
                $"Port {port} on {host} is open");
        }
        catch (OperationCanceledException)
        {
            return new DiagnosticResult(
                "Port Connectivity",
                CheckStatus.Fail,
                $"Connection to {host}:{port} timed out (possible firewall)",
                "Check firewall rules and ensure the port is open");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return new DiagnosticResult(
                "Port Connectivity",
                CheckStatus.Fail,
                $"Connection to {host}:{port} refused (SSH not running)",
                "Start the SSH service on the remote host");
        }
        catch (SocketException ex)
        {
            return new DiagnosticResult(
                "Port Connectivity",
                CheckStatus.Fail,
                $"Connection to {host}:{port} failed: {ex.Message}",
                "Check network connectivity and firewall settings");
        }
    }
}
