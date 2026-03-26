using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace SshEasyConfig.Platform;

[SupportedOSPlatform("windows")]
public class WindowsPlatform : IPlatform
{
    public PlatformKind Kind => PlatformKind.Windows;

    public string SshDirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public string SshdConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ssh", "sshd_config");

    public string AuthorizedKeysFilename =>
        IsAdminUser() ? "administrators_authorized_keys" : "authorized_keys";

    public bool IsElevated => IsAdminUser();

    public PackageManager PackageManager => PackageManager.WinGet;

    public FirewallType FirewallType => FirewallType.WindowsFirewall;

    public async Task SetFilePermissionsAsync(string path, SshFileKind kind)
    {
        var currentUser = Environment.UserName;

        // Remove inheritance
        await RunCommandAsync("icacls", $"\"{path}\" /inheritance:r");

        // Grant only current user full control
        await RunCommandAsync("icacls", $"\"{path}\" /grant:r \"{currentUser}:(F)\"");

        // For private keys, ensure no other users have access
        if (kind == SshFileKind.PrivateKey)
        {
            // Remove all other permissions — icacls /inheritance:r already did this,
            // and we only granted the current user above
        }
    }

    public async Task<bool> CheckFilePermissionsAsync(string path, SshFileKind kind)
    {
        try
        {
            var result = await RunCommandAsync("icacls", $"\"{path}\"");
            var currentUser = Environment.UserName;

            // On Windows, SSH requires that private keys are not accessible by
            // other regular users. SYSTEM, Administrators, and the current user
            // having access is acceptable and normal.
            if (kind == SshFileKind.PrivateKey || kind == SshFileKind.AuthorizedKeys)
            {
                var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        continue;
                    if (trimmed.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (trimmed.Contains("Successfully processed", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // These are all acceptable ACL entries on Windows
                    if (trimmed.Contains(currentUser, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (trimmed.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (trimmed.Contains("BUILTIN\\Administrators", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (trimmed.Contains("NT AUTHORITY\\SYSTEM", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Any other user/group = too open
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task RestartSshServiceAsync()
    {
        await RunCommandAsync("net", "stop sshd");
        await RunCommandAsync("net", "start sshd");
    }

    public async Task<bool> IsSshServiceRunningAsync()
    {
        try
        {
            var result = await RunCommandAsync("sc", "query sshd");
            return result.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> RunCommandAsync(string command, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"Command '{command} {arguments}' failed with exit code {process.ExitCode}: {error}");
        }

        return output;
    }

    public async Task<(int ExitCode, string StdOut, string StdErr)> TryRunCommandAsync(string command, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, output, error);
    }

    internal static bool IsAdminUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
