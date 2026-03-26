using SshEasyConfig.Core;
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

        // Layer 0: sshd installed check
        var sshdInstalled = await SshServerInstaller.IsSshdInstalledAsync(_platform);
        if (sshdInstalled)
        {
            results.Add(new DiagnosticResult(
                "sshd Installed",
                CheckStatus.Pass,
                "SSH server is installed"));
        }
        else
        {
            results.Add(new DiagnosticResult(
                "sshd Installed",
                CheckStatus.Fail,
                "SSH server is not installed",
                "Install with: ssh-easy-config setup",
                AutoFixAvailable: true,
                FixAction: async () => await SshServerInstaller.InstallAsync(_platform)));
        }

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
        if (localService.Status == CheckStatus.Fail && localService.AutoFixAvailable)
        {
            localService = localService with
            {
                FixAction = async () => await SshServerInstaller.StartAsync(_platform)
            };
        }
        results.Add(localService);

        // Firewall check
        var firewallOpen = await FirewallManager.IsPort22OpenAsync(_platform);
        if (firewallOpen)
        {
            results.Add(new DiagnosticResult(
                "Firewall Port 22",
                CheckStatus.Pass,
                "Port 22 is open in the firewall"));
        }
        else if (_platform.FirewallType != FirewallType.None)
        {
            results.Add(new DiagnosticResult(
                "Firewall Port 22",
                CheckStatus.Fail,
                "Port 22 appears blocked in the firewall",
                "Open port 22 in the firewall",
                AutoFixAvailable: true,
                FixAction: async () => await FirewallManager.OpenPort22Async(_platform)));
        }

        // Layer 3: Auth check
        var keyResult = await AuthCheck.CheckEd25519KeyExistsAsync(_platform);
        results.Add(keyResult);

        // Layer 4: Config and permissions
        var permResults = await ConfigCheck.CheckFilePermissionsAsync(_platform);
        // Add fix actions for permission failures
        for (var i = 0; i < permResults.Count; i++)
        {
            var r = permResults[i];
            if (r.AutoFixAvailable && r.Status != CheckStatus.Pass)
            {
                var path = GetPathFromPermissionCheck(r.CheckName);
                var kind = GetFileKindFromPermissionCheck(r.CheckName);
                if (path != null && kind != null)
                {
                    var capturedPath = path;
                    var capturedKind = kind.Value;
                    permResults[i] = r with
                    {
                        FixAction = async () => await _platform.SetFilePermissionsAsync(capturedPath, capturedKind)
                    };
                }
            }
        }
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

                // Windows: check for missing Match block
                if (OperatingSystem.IsWindows() && _platform.Kind == PlatformKind.Windows)
                {
                    if (!WindowsAccountHelper.SshdConfigHasMatchBlock(sshdContent))
                    {
                        results.Add(new DiagnosticResult(
                            "sshd: Match Group administrators",
                            CheckStatus.Warn,
                            "Match block for administrators is missing from sshd_config",
                            "Add Match Group administrators block to sshd_config",
                            AutoFixAvailable: true,
                            FixAction: async () =>
                            {
                                await WindowsAccountHelper.EnsureMatchBlockAsync(_platform);
                                await _platform.RestartSshServiceAsync();
                            }));
                    }
                }
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

    private string? GetPathFromPermissionCheck(string checkName)
    {
        if (checkName == ".ssh Directory Permissions")
            return _platform.SshDirectoryPath;

        if (checkName.StartsWith("Key Permissions: "))
        {
            var keyFile = checkName["Key Permissions: ".Length..];
            return Path.Combine(_platform.SshDirectoryPath, keyFile);
        }

        return null;
    }

    private SshFileKind? GetFileKindFromPermissionCheck(string checkName)
    {
        if (checkName == ".ssh Directory Permissions")
            return SshFileKind.SshDirectory;

        if (checkName.StartsWith("Key Permissions: "))
            return SshFileKind.PrivateKey;

        return null;
    }
}
