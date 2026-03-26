using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public enum AuditSeverity { Ok, Warning, Info }

public record AuditFinding(
    string Key,
    AuditSeverity Severity,
    string CurrentValue,
    string RecommendedValue,
    string Message);

public static class SshdConfigManager
{
    private static readonly (string Key, string Recommended, string BadValue, string Message)[] AuditRules =
    [
        ("PasswordAuthentication", "no", "yes", "Password authentication is enabled. Disable it for key-only access."),
        ("PubkeyAuthentication", "yes", "no", "Public key authentication is disabled. Enable it to allow key-based login."),
        ("PermitRootLogin", "no", "yes", "Root login is permitted. Disable it or set to 'prohibit-password'.")
    ];

    public static List<AuditFinding> Audit(string content)
    {
        var directives = ParseDirectives(content);
        var findings = new List<AuditFinding>();

        foreach (var (key, recommended, badValue, message) in AuditRules)
        {
            if (directives.TryGetValue(key, out var currentValue))
            {
                if (currentValue.Equals(badValue, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new AuditFinding(key, AuditSeverity.Warning, currentValue, recommended, message));
                }
                else
                {
                    findings.Add(new AuditFinding(key, AuditSeverity.Ok, currentValue, recommended, $"{key} is set correctly."));
                }
            }
            else
            {
                findings.Add(new AuditFinding(key, AuditSeverity.Info, "", recommended, $"{key} is not explicitly set. Recommended: {recommended}."));
            }
        }

        return findings;
    }

    public static string SetDirective(string content, string key, string value)
    {
        var lines = content.Split('\n');
        var result = new List<string>();
        bool found = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('#') && !string.IsNullOrEmpty(trimmed))
            {
                var spIndex = trimmed.IndexOf(' ');
                var eqIndex = trimmed.IndexOf('=');
                int splitIndex = -1;

                if (eqIndex >= 0 && (spIndex < 0 || eqIndex < spIndex))
                    splitIndex = eqIndex;
                else if (spIndex >= 0)
                    splitIndex = spIndex;

                if (splitIndex > 0)
                {
                    var lineKey = trimmed[..splitIndex].Trim();
                    if (lineKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add($"{key} {value}");
                        found = true;
                        continue;
                    }
                }
            }

            result.Add(line);
        }

        if (!found)
        {
            // Remove trailing empty entries from split
            while (result.Count > 0 && string.IsNullOrEmpty(result[^1]))
                result.RemoveAt(result.Count - 1);

            result.Add($"{key} {value}");
        }

        var output = string.Join("\n", result);
        if (!output.EndsWith('\n'))
            output += "\n";

        return output;
    }

    public static async Task<string> BackupAndReadAsync(IPlatform platform)
    {
        var path = platform.SshdConfigPath;
        var directory = Path.GetDirectoryName(path);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(directory ?? ".", $"sshd_config.backup_{timestamp}");

        File.Copy(path, backupPath, overwrite: true);

        return await File.ReadAllTextAsync(path);
    }

    public static async Task WriteAsync(IPlatform platform, string content)
    {
        await File.WriteAllTextAsync(platform.SshdConfigPath, content);
    }

    private static Dictionary<string, string> ParseDirectives(string content)
    {
        var directives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var spIndex = trimmed.IndexOf(' ');
            var eqIndex = trimmed.IndexOf('=');
            int splitIndex;

            if (eqIndex >= 0 && (spIndex < 0 || eqIndex < spIndex))
                splitIndex = eqIndex;
            else if (spIndex >= 0)
                splitIndex = spIndex;
            else
                continue;

            var key = trimmed[..splitIndex].Trim();
            var value = trimmed[(splitIndex + 1)..].Trim();
            directives[key] = value;
        }

        return directives;
    }
}
