using System.Net.Sockets;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Diagnostics;

public static class SshServiceCheck
{
    public static async Task<DiagnosticResult> CheckBannerAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token);

            using var stream = client.GetStream();
            stream.ReadTimeout = (int)timeout.TotalMilliseconds;

            var buffer = new byte[256];
            var bytesRead = await stream.ReadAsync(buffer, cts.Token);
            var banner = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            if (banner.StartsWith("SSH-", StringComparison.Ordinal))
            {
                return new DiagnosticResult(
                    "SSH Banner",
                    CheckStatus.Pass,
                    $"SSH service identified: {banner}");
            }

            return new DiagnosticResult(
                "SSH Banner",
                CheckStatus.Warn,
                $"Port {port} responded but banner does not look like SSH: {banner}",
                "Verify that an SSH server is running on this port");
        }
        catch (Exception ex)
        {
            return new DiagnosticResult(
                "SSH Banner",
                CheckStatus.Fail,
                $"Could not read SSH banner from {host}:{port}: {ex.Message}");
        }
    }

    public static async Task<DiagnosticResult> CheckLocalServiceAsync(IPlatform platform)
    {
        try
        {
            var running = await platform.IsSshServiceRunningAsync();
            if (running)
            {
                return new DiagnosticResult(
                    "Local SSH Service",
                    CheckStatus.Pass,
                    "SSH service is running");
            }

            return new DiagnosticResult(
                "Local SSH Service",
                CheckStatus.Fail,
                "SSH service is not running",
                "Start the SSH service: sudo systemctl start sshd (Linux) or enable OpenSSH Server (Windows)",
                AutoFixAvailable: true);
        }
        catch (Exception ex)
        {
            return new DiagnosticResult(
                "Local SSH Service",
                CheckStatus.Fail,
                $"Could not check SSH service status: {ex.Message}");
        }
    }
}
