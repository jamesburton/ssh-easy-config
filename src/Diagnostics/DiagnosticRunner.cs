using SshEasyConfig.Commands;
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

        // Layer 4b: Windows MS-linked account checks
        if (OperatingSystem.IsWindows() && _platform.Kind == PlatformKind.Windows)
        {
            try
            {
                var isMsAccount = WindowsAccountHelper.IsMicrosoftLinkedAccount();
                if (isMsAccount)
                {
                    results.Add(new DiagnosticResult(
                        "Windows MS Account",
                        CheckStatus.Pass,
                        "Microsoft-linked account detected — admin authorized_keys required"));

                    var adminKeysPath = WindowsAccountHelper.GetAdminAuthorizedKeysPath();
                    var userKeysPath = Path.Combine(_platform.SshDirectoryPath, "authorized_keys");

                    // Check if administrators_authorized_keys exists and has keys
                    if (!File.Exists(adminKeysPath))
                    {
                        var hasUserKeys = File.Exists(userKeysPath) &&
                            (await File.ReadAllTextAsync(userKeysPath)).Trim().Length > 0;

                        results.Add(new DiagnosticResult(
                            "Admin authorized_keys",
                            CheckStatus.Fail,
                            "administrators_authorized_keys does not exist" +
                                (hasUserKeys ? " (but keys found in user authorized_keys)" : ""),
                            "Run: ssh-easy-config config fix (as Administrator)",
                            AutoFixAvailable: true,
                            FixAction: async () =>
                            {
                                await ConfigCommand.RunFixAsync(_platform);
                            }));
                    }
                    else
                    {
                        // File exists — check if user keys are missing from it
                        var adminContent = "";
                        try
                        {
                            await _platform.RunCommandAsync("icacls",
                                $"\"{adminKeysPath}\" /grant \"BUILTIN\\Administrators:(R)\"");
                            adminContent = await File.ReadAllTextAsync(adminKeysPath);
                        }
                        catch { }

                        if (string.IsNullOrWhiteSpace(adminContent))
                        {
                            results.Add(new DiagnosticResult(
                                "Admin authorized_keys",
                                CheckStatus.Warn,
                                "administrators_authorized_keys exists but is empty or unreadable",
                                "Run: ssh-easy-config config fix (as Administrator)",
                                AutoFixAvailable: true,
                                FixAction: async () => await ConfigCommand.RunFixAsync(_platform)));
                        }
                        else if (File.Exists(userKeysPath))
                        {
                            // Check for keys in user file that aren't in admin file
                            var userContent = await File.ReadAllTextAsync(userKeysPath);
                            var userKeys = userContent.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                                .Select(l => l.Trim().Split(' ').Length >= 2 ? l.Trim().Split(' ')[0] + " " + l.Trim().Split(' ')[1] : "")
                                .Where(k => k.Length > 0)
                                .ToHashSet();

                            var adminKeys = adminContent.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                                .Select(l => l.Trim().Split(' ').Length >= 2 ? l.Trim().Split(' ')[0] + " " + l.Trim().Split(' ')[1] : "")
                                .Where(k => k.Length > 0)
                                .ToHashSet();

                            var missingKeys = userKeys.Except(adminKeys).ToList();
                            if (missingKeys.Count > 0)
                            {
                                results.Add(new DiagnosticResult(
                                    "Admin authorized_keys",
                                    CheckStatus.Warn,
                                    $"{missingKeys.Count} key(s) in authorized_keys not in administrators_authorized_keys",
                                    "Run: ssh-easy-config config fix (as Administrator)",
                                    AutoFixAvailable: true,
                                    FixAction: async () => await ConfigCommand.RunFixAsync(_platform)));
                            }
                            else
                            {
                                results.Add(new DiagnosticResult(
                                    "Admin authorized_keys",
                                    CheckStatus.Pass,
                                    "administrators_authorized_keys has all keys"));
                            }
                        }
                        else
                        {
                            results.Add(new DiagnosticResult(
                                "Admin authorized_keys",
                                CheckStatus.Pass,
                                "administrators_authorized_keys is configured"));
                        }
                    }

                    // Check Match block (may already be checked above, but be explicit for MS accounts)
                    if (File.Exists(_platform.SshdConfigPath))
                    {
                        var content = await File.ReadAllTextAsync(_platform.SshdConfigPath);
                        if (!WindowsAccountHelper.SshdConfigHasMatchBlock(content))
                        {
                            // Only add if not already added by the general sshd_config check above
                            if (!results.Any(r => r.CheckName == "sshd: Match Group administrators"))
                            {
                                results.Add(new DiagnosticResult(
                                    "sshd: Match Group administrators",
                                    CheckStatus.Fail,
                                    "Match block required for MS account admin SSH — missing from sshd_config",
                                    "Run: ssh-easy-config config fix (as Administrator)",
                                    AutoFixAvailable: true,
                                    FixAction: async () => await ConfigCommand.RunFixAsync(_platform)));
                            }
                        }
                    }
                }
            }
            catch { }
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
