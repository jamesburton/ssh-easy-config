using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Diagnostics;

public static class ConfigCheck
{
    public static async Task<List<DiagnosticResult>> CheckClientConfigAsync(IPlatform platform)
    {
        var results = new List<DiagnosticResult>();
        var configPath = SshConfigManager.GetConfigPath(platform);

        // Check config file exists
        if (!File.Exists(configPath))
        {
            results.Add(new DiagnosticResult(
                "SSH Client Config",
                CheckStatus.Warn,
                $"No SSH config file found at {configPath}",
                "Create one with: ssh-easy-config pair"));
            return results;
        }

        results.Add(new DiagnosticResult(
            "SSH Client Config",
            CheckStatus.Pass,
            $"SSH config file exists at {configPath}"));

        // Check permissions
        try
        {
            var permOk = await platform.CheckFilePermissionsAsync(configPath, SshFileKind.Config);
            results.Add(new DiagnosticResult(
                "Config Permissions",
                permOk ? CheckStatus.Pass : CheckStatus.Warn,
                permOk ? "Config file permissions are correct" : "Config file permissions may be too open",
                permOk ? null : "Run: ssh-easy-config fix-permissions"));
        }
        catch (Exception ex)
        {
            results.Add(new DiagnosticResult(
                "Config Permissions",
                CheckStatus.Warn,
                $"Could not check config permissions: {ex.Message}"));
        }

        // Basic syntax parse
        try
        {
            var content = await File.ReadAllTextAsync(configPath);
            var hosts = SshConfigManager.ParseHosts(content);
            results.Add(new DiagnosticResult(
                "Config Syntax",
                CheckStatus.Pass,
                $"Config parses successfully ({hosts.Count} host entries)"));
        }
        catch (Exception ex)
        {
            results.Add(new DiagnosticResult(
                "Config Syntax",
                CheckStatus.Fail,
                $"Config file has syntax issues: {ex.Message}"));
        }

        return results;
    }

    public static List<DiagnosticResult> CheckSshdConfig(string sshdContent, IPlatform platform)
    {
        var findings = SshdConfigManager.Audit(sshdContent);
        var results = new List<DiagnosticResult>();

        foreach (var finding in findings)
        {
            var status = finding.Severity switch
            {
                AuditSeverity.Ok => CheckStatus.Pass,
                AuditSeverity.Warning => CheckStatus.Warn,
                AuditSeverity.Info => CheckStatus.Pass,
                _ => CheckStatus.Pass
            };

            var suggestion = finding.Severity == AuditSeverity.Warning
                ? $"Set '{finding.Key} {finding.RecommendedValue}' in sshd_config"
                : null;

            results.Add(new DiagnosticResult(
                $"sshd: {finding.Key}",
                status,
                finding.Message,
                suggestion,
                AutoFixAvailable: finding.Severity == AuditSeverity.Warning));
        }

        return results;
    }

    public static async Task<List<DiagnosticResult>> CheckFilePermissionsAsync(IPlatform platform)
    {
        var results = new List<DiagnosticResult>();

        // Check .ssh directory
        if (Directory.Exists(platform.SshDirectoryPath))
        {
            try
            {
                var dirOk = await platform.CheckFilePermissionsAsync(
                    platform.SshDirectoryPath, SshFileKind.SshDirectory);
                results.Add(new DiagnosticResult(
                    ".ssh Directory Permissions",
                    dirOk ? CheckStatus.Pass : CheckStatus.Warn,
                    dirOk ? ".ssh directory permissions are correct" : ".ssh directory permissions may be too open",
                    dirOk ? null : "Run: chmod 700 ~/.ssh",
                    AutoFixAvailable: !dirOk));
            }
            catch (Exception ex)
            {
                results.Add(new DiagnosticResult(
                    ".ssh Directory Permissions",
                    CheckStatus.Warn,
                    $"Could not check .ssh directory permissions: {ex.Message}"));
            }
        }
        else
        {
            results.Add(new DiagnosticResult(
                ".ssh Directory",
                CheckStatus.Warn,
                $".ssh directory not found at {platform.SshDirectoryPath}",
                "Create it with: mkdir -p ~/.ssh && chmod 700 ~/.ssh"));
        }

        // Check private keys
        if (Directory.Exists(platform.SshDirectoryPath))
        {
            var keyFiles = new[] { "id_ed25519", "id_rsa", "id_ecdsa" };
            foreach (var keyFile in keyFiles)
            {
                var keyPath = Path.Combine(platform.SshDirectoryPath, keyFile);
                if (!File.Exists(keyPath))
                    continue;

                try
                {
                    var keyOk = await platform.CheckFilePermissionsAsync(keyPath, SshFileKind.PrivateKey);
                    results.Add(new DiagnosticResult(
                        $"Key Permissions: {keyFile}",
                        keyOk ? CheckStatus.Pass : CheckStatus.Warn,
                        keyOk
                            ? $"{keyFile} permissions are correct"
                            : $"{keyFile} permissions may be too open",
                        keyOk ? null : $"Run: chmod 600 ~/.ssh/{keyFile}",
                        AutoFixAvailable: !keyOk));
                }
                catch (Exception ex)
                {
                    results.Add(new DiagnosticResult(
                        $"Key Permissions: {keyFile}",
                        CheckStatus.Warn,
                        $"Could not check {keyFile} permissions: {ex.Message}"));
                }
            }
        }

        return results;
    }
}
