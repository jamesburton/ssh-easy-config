using System.Diagnostics;

namespace SshEasyConfig.Platform;

public class LinuxPlatform : IPlatform
{
    public virtual PlatformKind Kind => PlatformKind.Linux;

    public virtual string SshDirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public virtual string SshdConfigPath => "/etc/ssh/sshd_config";

    public virtual string AuthorizedKeysFilename => "authorized_keys";

    public virtual bool IsElevated
    {
        get
        {
            try
            {
                var result = TryRunCommandAsync("id", "-u").GetAwaiter().GetResult();
                return result.ExitCode == 0 && result.StdOut.Trim() == "0";
            }
            catch
            {
                return false;
            }
        }
    }

    public virtual PackageManager PackageManager
    {
        get
        {
            if (File.Exists("/usr/bin/apt") || File.Exists("/usr/bin/apt-get"))
                return PackageManager.Apt;
            if (File.Exists("/usr/bin/dnf"))
                return PackageManager.Dnf;
            if (File.Exists("/usr/bin/yum"))
                return PackageManager.Yum;
            return PackageManager.None;
        }
    }

    public virtual FirewallType FirewallType
    {
        get
        {
            try
            {
                var result = TryRunCommandAsync("which", "ufw").GetAwaiter().GetResult();
                if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut))
                    return FirewallType.Ufw;

                result = TryRunCommandAsync("which", "firewall-cmd").GetAwaiter().GetResult();
                if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut))
                    return FirewallType.Firewalld;

                return FirewallType.Iptables;
            }
            catch
            {
                return FirewallType.Iptables;
            }
        }
    }

    public virtual async Task SetFilePermissionsAsync(string path, SshFileKind kind)
    {
        var mode = kind switch
        {
            SshFileKind.SshDirectory => "700",
            SshFileKind.PrivateKey => "600",
            SshFileKind.AuthorizedKeys => "600",
            SshFileKind.Config => "644",
            _ => "644"
        };

        await RunCommandAsync("chmod", $"{mode} \"{path}\"");
    }

    public virtual async Task<bool> CheckFilePermissionsAsync(string path, SshFileKind kind)
    {
        var expectedMode = kind switch
        {
            SshFileKind.SshDirectory => "700",
            SshFileKind.PrivateKey => "600",
            SshFileKind.AuthorizedKeys => "600",
            SshFileKind.Config => "644",
            _ => "644"
        };

        try
        {
            var result = await RunCommandAsync("stat", $"-c %a \"{path}\"");
            return result.Trim() == expectedMode;
        }
        catch
        {
            return false;
        }
    }

    public virtual async Task RestartSshServiceAsync()
    {
        await RunCommandAsync("sudo", "systemctl restart sshd");
    }

    public virtual async Task<bool> IsSshServiceRunningAsync()
    {
        try
        {
            var result = await RunCommandAsync("systemctl", "is-active sshd");
            return result.Trim() == "active";
        }
        catch
        {
            return false;
        }
    }

    public virtual async Task<string> RunCommandAsync(string command, string arguments)
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

    public virtual async Task<(int ExitCode, string StdOut, string StdErr)> TryRunCommandAsync(string command, string arguments)
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
}
