using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public static class SshServerInstaller
{
    /// <summary>
    /// Checks whether the SSH server (sshd) is installed on the current platform.
    /// Returns true if the service is running, sshd_config exists, or platform-specific checks pass.
    /// </summary>
    public static async Task<bool> IsSshdInstalledAsync(IPlatform platform)
    {
        // Check if service is already running
        if (await platform.IsSshServiceRunningAsync())
            return true;

        // Check if sshd_config exists
        if (File.Exists(platform.SshdConfigPath))
            return true;

        // Platform-specific checks
        return platform.Kind switch
        {
            PlatformKind.Windows => await CheckWindowsSshdInstalledAsync(platform),
            PlatformKind.Linux or PlatformKind.Wsl => await CheckLinuxSshdInstalledAsync(platform),
            PlatformKind.MacOS => true, // macOS ships with sshd
            _ => false
        };
    }

    /// <summary>
    /// Checks whether the SSH service is enabled to start on boot.
    /// </summary>
    public static async Task<bool> IsSshdEnabledAsync(IPlatform platform)
    {
        return platform.Kind switch
        {
            PlatformKind.Windows => await CheckWindowsSshdEnabledAsync(platform),
            PlatformKind.MacOS => await CheckMacOsSshdEnabledAsync(platform),
            PlatformKind.Linux or PlatformKind.Wsl => await CheckLinuxSshdEnabledAsync(platform),
            _ => false
        };
    }

    /// <summary>
    /// Returns the platform-appropriate SSH service name.
    /// </summary>
    public static string GetSshServiceName(PlatformKind kind)
    {
        return kind switch
        {
            PlatformKind.Windows => "sshd",
            PlatformKind.MacOS => "com.openssh.sshd",
            PlatformKind.Linux or PlatformKind.Wsl => GetLinuxServiceName(),
            _ => "sshd"
        };
    }

    /// <summary>
    /// Returns the command and arguments needed to install the SSH server on the given platform.
    /// </summary>
    public static (string Command, string Arguments) GetInstallCommand(PlatformKind kind, PackageManager packageManager)
    {
        return kind switch
        {
            PlatformKind.Windows => ("powershell", "-Command \"Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0\""),
            PlatformKind.MacOS => ("sudo", "systemsetup -setremotelogin on"),
            PlatformKind.Linux or PlatformKind.Wsl => packageManager switch
            {
                PackageManager.Apt => ("sudo", "apt install -y openssh-server"),
                PackageManager.Dnf => ("sudo", "dnf install -y openssh-server"),
                PackageManager.Yum => ("sudo", "yum install -y openssh-server"),
                _ => throw new InvalidOperationException($"Unsupported package manager: {packageManager}")
            },
            _ => throw new InvalidOperationException($"Unsupported platform: {kind}")
        };
    }

    /// <summary>
    /// Installs the SSH server using the appropriate platform command.
    /// </summary>
    public static async Task InstallAsync(IPlatform platform)
    {
        var (command, arguments) = GetInstallCommand(platform.Kind, platform.PackageManager);
        await platform.RunCommandAsync(command, arguments);
    }

    /// <summary>
    /// Starts the SSH service.
    /// </summary>
    public static async Task StartAsync(IPlatform platform)
    {
        switch (platform.Kind)
        {
            case PlatformKind.Windows:
                await platform.RunCommandAsync("sc", "start sshd");
                break;
            case PlatformKind.MacOS:
                await platform.RunCommandAsync("sudo", "systemsetup -setremotelogin on");
                break;
            case PlatformKind.Linux:
            case PlatformKind.Wsl:
                var serviceName = GetSshServiceName(platform.Kind);
                var result = await platform.TryRunCommandAsync("systemctl", $"start {serviceName}");
                if (result.ExitCode != 0)
                {
                    // Fallback to service command
                    await platform.RunCommandAsync("sudo", $"service {serviceName} start");
                }
                break;
            default:
                throw new InvalidOperationException($"Unsupported platform: {platform.Kind}");
        }
    }

    /// <summary>
    /// Enables the SSH service to start on boot.
    /// </summary>
    public static async Task EnableAsync(IPlatform platform)
    {
        switch (platform.Kind)
        {
            case PlatformKind.Windows:
                await platform.RunCommandAsync("sc", "config sshd start= auto");
                break;
            case PlatformKind.MacOS:
                await platform.RunCommandAsync("sudo", "systemsetup -setremotelogin on");
                break;
            case PlatformKind.Linux:
            case PlatformKind.Wsl:
                var serviceName = GetSshServiceName(platform.Kind);
                await platform.RunCommandAsync("sudo", $"systemctl enable {serviceName}");
                break;
            default:
                throw new InvalidOperationException($"Unsupported platform: {platform.Kind}");
        }
    }

    // ── SSH Client ─────────────────────────────────────────────────────

    /// <summary>
    /// Finds the ssh client binary path, or null if not installed.
    /// </summary>
    public static string? FindSshClientPath(IPlatform platform)
    {
        if (platform.Kind == PlatformKind.Windows)
        {
            var system32Ssh = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "OpenSSH", "ssh.exe");
            if (File.Exists(system32Ssh)) return system32Ssh;

            var progSsh = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "OpenSSH", "ssh.exe");
            if (File.Exists(progSsh)) return progSsh;

            // Check PATH
            var (ec, stdout, _) = platform.TryRunCommandAsync("where", "ssh").GetAwaiter().GetResult();
            if (ec == 0 && !string.IsNullOrWhiteSpace(stdout))
                return stdout.Trim().Split('\n')[0].Trim();

            return null;
        }

        // Linux/macOS: check which
        var result = platform.TryRunCommandAsync("which", "ssh").GetAwaiter().GetResult();
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut)
            ? result.StdOut.Trim()
            : null;
    }

    /// <summary>
    /// Returns the command to install the SSH client.
    /// </summary>
    public static (string Command, string Arguments) GetClientInstallCommand(PlatformKind kind, PackageManager pm)
    {
        return kind switch
        {
            PlatformKind.Windows => ("powershell",
                "-Command \"Add-WindowsCapability -Online -Name OpenSSH.Client~~~~0.0.1.0\""),
            PlatformKind.Linux or PlatformKind.Wsl => pm switch
            {
                PackageManager.Apt => ("sudo", "apt install -y openssh-client"),
                PackageManager.Dnf => ("sudo", "dnf install -y openssh-clients"),
                PackageManager.Yum => ("sudo", "yum install -y openssh-clients"),
                _ => throw new InvalidOperationException($"Unsupported package manager: {pm}")
            },
            PlatformKind.MacOS => ("echo", "SSH client is built-in on macOS"),
            _ => throw new InvalidOperationException($"Unsupported platform: {kind}")
        };
    }

    /// <summary>
    /// Installs the SSH client.
    /// </summary>
    public static async Task InstallClientAsync(IPlatform platform)
    {
        var (cmd, args) = GetClientInstallCommand(platform.Kind, platform.PackageManager);
        await platform.RunCommandAsync(cmd, args);
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private static async Task<bool> CheckWindowsSshdInstalledAsync(IPlatform platform)
    {
        var result = await platform.TryRunCommandAsync("powershell",
            "-Command \"Get-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0 | Select-Object -ExpandProperty State\"");
        return result.ExitCode == 0 && result.StdOut.Trim().Equals("Installed", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> CheckLinuxSshdInstalledAsync(IPlatform platform)
    {
        var result = await platform.TryRunCommandAsync("which", "sshd");
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut);
    }

    private static async Task<bool> CheckWindowsSshdEnabledAsync(IPlatform platform)
    {
        var result = await platform.TryRunCommandAsync("sc", "qc sshd");
        return result.ExitCode == 0 && result.StdOut.Contains("AUTO_START");
    }

    private static async Task<bool> CheckMacOsSshdEnabledAsync(IPlatform platform)
    {
        var result = await platform.TryRunCommandAsync("systemsetup", "-getremotelogin");
        return result.ExitCode == 0 && result.StdOut.Contains("On", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> CheckLinuxSshdEnabledAsync(IPlatform platform)
    {
        var serviceName = GetSshServiceName(PlatformKind.Linux);
        var result = await platform.TryRunCommandAsync("systemctl", $"is-enabled {serviceName}");
        return result.ExitCode == 0 && result.StdOut.Trim().Equals("enabled", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLinuxServiceName()
    {
        if (File.Exists("/lib/systemd/system/ssh.service") || File.Exists("/etc/init.d/ssh"))
            return "ssh";
        return "sshd";
    }
}
