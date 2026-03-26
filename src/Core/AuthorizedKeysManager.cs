using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public static class AuthorizedKeysManager
{
    /// <summary>
    /// Extracts the key fingerprint (type + data) from a public key line,
    /// ignoring any trailing comment.
    /// </summary>
    private static string GetKeyFingerprint(string keyLine)
    {
        var parts = keyLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0]} {parts[1]}" : keyLine.Trim();
    }

    /// <summary>
    /// Adds a public key line to the authorized_keys content.
    /// Deduplicates by key type and key data (first two space-separated parts),
    /// preserving the existing entry if a duplicate is found.
    /// Returns normalized content with a trailing newline.
    /// </summary>
    public static string AddKey(string existingContent, string publicKeyLine)
    {
        var lines = existingContent.Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var newFingerprint = GetKeyFingerprint(publicKeyLine);
        var alreadyExists = lines.Any(l =>
            !l.TrimStart().StartsWith('#') &&
            GetKeyFingerprint(l) == newFingerprint);

        if (!alreadyExists)
        {
            lines.Add(publicKeyLine);
        }

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    /// Removes entries whose key type and data start with the given prefix.
    /// Preserves comments and other entries.
    /// </summary>
    public static string RemoveKey(string existingContent, string keyTypeAndData)
    {
        var lines = existingContent.Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Where(l => l.TrimStart().StartsWith('#') || !l.StartsWith(keyTypeAndData))
            .ToList();

        return lines.Count > 0
            ? string.Join('\n', lines) + "\n"
            : "";
    }

    /// <summary>
    /// Gets the path to the authorized_keys file for the given platform.
    /// </summary>
    public static string GetPath(IPlatform platform)
    {
        return Path.Combine(platform.SshDirectoryPath, platform.AuthorizedKeysFilename);
    }

    /// <summary>
    /// Reads the authorized_keys file content. Returns empty string if the file does not exist.
    /// </summary>
    public static async Task<string> ReadAsync(IPlatform platform)
    {
        var path = GetPath(platform);
        if (!File.Exists(path))
            return "";

        return await File.ReadAllTextAsync(path);
    }

    /// <summary>
    /// Writes the authorized_keys content to disk, creating the SSH directory
    /// if needed and setting appropriate file permissions.
    /// </summary>
    public static async Task WriteAsync(IPlatform platform, string content)
    {
        var sshDir = platform.SshDirectoryPath;
        if (!Directory.Exists(sshDir))
        {
            Directory.CreateDirectory(sshDir);
            await platform.SetFilePermissionsAsync(sshDir, SshFileKind.SshDirectory);
        }

        var path = GetPath(platform);
        await File.WriteAllTextAsync(path, content);
        await platform.SetFilePermissionsAsync(path, SshFileKind.AuthorizedKeys);
    }
}
