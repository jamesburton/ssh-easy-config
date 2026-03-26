using System.Runtime.Versioning;
using System.Security.Principal;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public static class WindowsAccountHelper
{
    private const string MatchGroupBlock = """
        # SSH access for administrators with separate authorized_keys file
        Match Group administrators
            AuthorizedKeysFile __PROGRAMDATA__/ssh/administrators_authorized_keys
        """;

    /// <summary>
    /// Returns the path to the administrators_authorized_keys file used by Windows OpenSSH
    /// for members of the Administrators group.
    /// </summary>
    public static string GetAdminAuthorizedKeysPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ssh",
            "administrators_authorized_keys");
    }

    /// <summary>
    /// Checks whether the current Windows user account is linked to a Microsoft account.
    /// Examines WindowsIdentity claims for "MicrosoftAccount" and falls back to checking
    /// the registry ProfileList for a Guid value.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool IsMicrosoftLinkedAccount()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();

            // Check claims for Microsoft account indicator
            foreach (var claim in identity.Claims)
            {
                if (claim.Value.Contains("MicrosoftAccount", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (claim.Issuer?.Contains("MicrosoftAccount", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }

            // Check registry for profile GUID (indicates MS-linked account)
            var sid = identity.User?.Value;
            if (sid != null)
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
                if (key != null)
                {
                    var guidValue = key.GetValue("Guid");
                    if (guidValue != null)
                        return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the sshd_config Match Group block for administrators.
    /// </summary>
    public static string GetMatchGroupBlock()
    {
        return MatchGroupBlock;
    }

    /// <summary>
    /// Checks if the given sshd_config content already contains the Match Group administrators block.
    /// </summary>
    public static bool SshdConfigHasMatchBlock(string content)
    {
        return content.Contains("Match Group administrators", StringComparison.OrdinalIgnoreCase) &&
               content.Contains("administrators_authorized_keys", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates the administrators_authorized_keys file, adds the given public key,
    /// and sets the required ACLs (SYSTEM and BUILTIN\Administrators only).
    /// </summary>
    public static async Task SetupAdminAuthorizedKeysAsync(IPlatform platform, string publicKey)
    {
        var path = GetAdminAuthorizedKeysPath();
        var directory = Path.GetDirectoryName(path)!;

        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        // Read existing content or start fresh
        var existingContent = File.Exists(path) ? await File.ReadAllTextAsync(path) : "";

        // Add the key using AuthorizedKeysManager
        var updatedContent = AuthorizedKeysManager.AddKey(existingContent, publicKey);
        await File.WriteAllTextAsync(path, updatedContent);

        // Set ACLs: remove inheritance, grant SYSTEM and Administrators full control
        await platform.RunCommandAsync("icacls",
            $"\"{path}\" /inheritance:r /grant \"SYSTEM:(F)\" /grant \"BUILTIN\\Administrators:(F)\"");
    }

    /// <summary>
    /// Reads sshd_config, checks for the Match Group administrators block, and appends it if missing.
    /// Creates a backup before modifying.
    /// </summary>
    public static async Task EnsureMatchBlockAsync(IPlatform platform)
    {
        var sshdConfigPath = platform.SshdConfigPath;
        var content = await File.ReadAllTextAsync(sshdConfigPath);

        if (SshdConfigHasMatchBlock(content))
            return;

        // Create backup
        var directory = Path.GetDirectoryName(sshdConfigPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(directory ?? ".", $"sshd_config.backup_{timestamp}");
        File.Copy(sshdConfigPath, backupPath, overwrite: true);

        // Append the match block
        if (!content.EndsWith('\n'))
            content += "\n";
        content += "\n" + GetMatchGroupBlock() + "\n";

        await File.WriteAllTextAsync(sshdConfigPath, content);
    }
}
