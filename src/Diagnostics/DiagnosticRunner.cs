using SshEasyConfig.Platform;

namespace SshEasyConfig.Diagnostics;

public class DiagnosticRunner
{
    private readonly IPlatform _platform;

    public DiagnosticRunner(IPlatform platform)
    {
        _platform = platform;
    }

    public async Task<List<DiagnosticResult>> RunAllAsync(string? host, int port = 22)
    {
        var results = new List<DiagnosticResult>();
        var timeout = TimeSpan.FromSeconds(5);

        // Layer 1: Network checks (if host provided)
        if (host != null)
        {
            var dnsResults = await NetworkCheck.CheckDnsAsync(host);
            results.AddRange(dnsResults);

            // Stop network checks if DNS failed
            if (dnsResults.Any(r => r.Status == CheckStatus.Fail))
                goto localChecks;

            var portResult = await NetworkCheck.CheckPortAsync(host, port, timeout);
            results.Add(portResult);

            if (portResult.Status == CheckStatus.Fail)
                goto localChecks;
        }

        // Layer 2: SSH service checks
        if (host != null)
        {
            var bannerResult = await SshServiceCheck.CheckBannerAsync(host, port, timeout);
            results.Add(bannerResult);
        }

    localChecks:
        // Always check local SSH service
        var localService = await SshServiceCheck.CheckLocalServiceAsync(_platform);
        results.Add(localService);

        // Layer 3: Auth check
        var keyResult = await AuthCheck.CheckEd25519KeyExistsAsync(_platform);
        results.Add(keyResult);

        // Layer 4: Config and permissions
        var permResults = await ConfigCheck.CheckFilePermissionsAsync(_platform);
        results.AddRange(permResults);

        var clientConfigResults = await ConfigCheck.CheckClientConfigAsync(_platform);
        results.AddRange(clientConfigResults);

        // sshd_config check (if file exists)
        if (File.Exists(_platform.SshdConfigPath))
        {
            try
            {
                var sshdContent = await File.ReadAllTextAsync(_platform.SshdConfigPath);
                var sshdResults = ConfigCheck.CheckSshdConfig(sshdContent, _platform);
                results.AddRange(sshdResults);
            }
            catch (Exception ex)
            {
                results.Add(new DiagnosticResult(
                    "sshd_config",
                    CheckStatus.Warn,
                    $"Could not read sshd_config: {ex.Message}"));
            }
        }

        // Layer 5: WSL checks
        var wslResults = WslCheck.Check(_platform);
        results.AddRange(wslResults);

        return results;
    }
}
