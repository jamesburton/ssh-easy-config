using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public record SshHostEntry(
    string Alias,
    string HostName,
    string? User,
    int Port = 22,
    string? IdentityFile = null,
    bool ForwardAgent = false);

public static class SshConfigManager
{
    public static List<SshHostEntry> ParseHosts(string content)
    {
        var hosts = new List<SshHostEntry>();
        if (string.IsNullOrWhiteSpace(content))
            return hosts;

        string? currentAlias = null;
        string? hostName = null;
        string? user = null;
        int port = 22;
        string? identityFile = null;
        bool forwardAgent = false;

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var (key, value) = ParseDirective(trimmed);
            if (key == null)
                continue;

            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                // Save previous host if valid
                if (currentAlias != null && hostName != null)
                {
                    hosts.Add(new SshHostEntry(currentAlias, hostName, user, port, identityFile, forwardAgent));
                }

                currentAlias = value;
                hostName = null;
                user = null;
                port = 22;
                identityFile = null;
                forwardAgent = false;
            }
            else if (key.Equals("HostName", StringComparison.OrdinalIgnoreCase))
            {
                hostName = value;
            }
            else if (key.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                user = value;
            }
            else if (key.Equals("Port", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out var p))
                    port = p;
            }
            else if (key.Equals("IdentityFile", StringComparison.OrdinalIgnoreCase))
            {
                identityFile = value;
            }
            else if (key.Equals("ForwardAgent", StringComparison.OrdinalIgnoreCase))
            {
                forwardAgent = value?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
            }
        }

        // Save last host
        if (currentAlias != null && hostName != null)
        {
            hosts.Add(new SshHostEntry(currentAlias, hostName, user, port, identityFile, forwardAgent));
        }

        return hosts;
    }

    public static string AddHost(string existingContent, SshHostEntry entry)
    {
        var block = new System.Text.StringBuilder();
        block.AppendLine($"Host {entry.Alias}");
        block.AppendLine($"    HostName {entry.HostName}");

        if (!string.IsNullOrEmpty(entry.User))
            block.AppendLine($"    User {entry.User}");

        if (entry.Port != 22)
            block.AppendLine($"    Port {entry.Port}");

        if (!string.IsNullOrEmpty(entry.IdentityFile))
            block.AppendLine($"    IdentityFile {entry.IdentityFile}");

        if (entry.ForwardAgent)
            block.AppendLine("    ForwardAgent yes");

        var hostBlock = block.ToString();

        if (string.IsNullOrWhiteSpace(existingContent))
            return hostBlock;

        var result = existingContent.TrimEnd();
        return result + "\n\n" + hostBlock;
    }

    public static string RemoveHost(string content, string alias)
    {
        var lines = content.Split('\n');
        var result = new System.Text.StringBuilder();
        bool skipping = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            var (key, value) = ParseDirective(trimmed);

            if (key != null && key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                if (value == alias)
                {
                    skipping = true;
                    continue;
                }
                else
                {
                    skipping = false;
                }
            }

            if (!skipping)
            {
                result.AppendLine(lines[i]);
            }
        }

        // Clean up double blank lines
        var text = result.ToString();
        while (text.Contains("\n\n\n"))
            text = text.Replace("\n\n\n", "\n\n");

        return text.TrimStart('\n');
    }

    public static string GetConfigPath(IPlatform platform)
    {
        return Path.Combine(platform.SshDirectoryPath, "config");
    }

    public static async Task<string> ReadConfigAsync(IPlatform platform)
    {
        var path = GetConfigPath(platform);
        if (!File.Exists(path))
            return "";
        return await File.ReadAllTextAsync(path);
    }

    public static async Task WriteConfigAsync(IPlatform platform, string content)
    {
        var path = GetConfigPath(platform);
        var directory = Path.GetDirectoryName(path);
        if (directory != null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, content);
        await platform.SetFilePermissionsAsync(path, SshFileKind.Config);
    }

    private static (string? key, string? value) ParseDirective(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            return (null, null);

        // Try "Key=Value" syntax
        var eqIndex = line.IndexOf('=');
        var spIndex = line.IndexOf(' ');

        int splitIndex;
        if (eqIndex >= 0 && (spIndex < 0 || eqIndex < spIndex))
            splitIndex = eqIndex;
        else if (spIndex >= 0)
            splitIndex = spIndex;
        else
            return (line, null);

        var key = line[..splitIndex].Trim();
        var value = line[(splitIndex + 1)..].Trim();
        return (key, value);
    }
}
