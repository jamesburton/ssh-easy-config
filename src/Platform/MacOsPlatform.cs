namespace SshEasyConfig.Platform;

public class MacOsPlatform : LinuxPlatform
{
    public override PlatformKind Kind => PlatformKind.MacOS;

    public override string SshdConfigPath => "/etc/ssh/sshd_config";

    public override PackageManager PackageManager
    {
        get
        {
            if (File.Exists("/opt/homebrew/bin/brew") || File.Exists("/usr/local/bin/brew"))
                return PackageManager.Brew;
            return PackageManager.None;
        }
    }

    public override FirewallType FirewallType => FirewallType.Pf;

    public override async Task<bool> CheckFilePermissionsAsync(string path, SshFileKind kind)
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
            // macOS uses -f %Lp instead of -c %a
            var result = await RunCommandAsync("stat", $"-f %Lp \"{path}\"");
            return result.Trim() == expectedMode;
        }
        catch
        {
            return false;
        }
    }

    public override async Task RestartSshServiceAsync()
    {
        // macOS uses launchctl for service management
        await RunCommandAsync("sudo", "launchctl stop com.openssh.sshd");
        await RunCommandAsync("sudo", "launchctl start com.openssh.sshd");
    }

    public override async Task<bool> IsSshServiceRunningAsync()
    {
        try
        {
            var result = await RunCommandAsync("launchctl", "list com.openssh.sshd");
            return !string.IsNullOrWhiteSpace(result);
        }
        catch
        {
            return false;
        }
    }
}
