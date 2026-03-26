namespace SshEasyConfig.Platform;

public enum PlatformKind { Linux, MacOS, Windows, Wsl }

public enum PackageManager { Apt, Dnf, Yum, Brew, WinGet, None }

public enum FirewallType { Ufw, Firewalld, Iptables, WindowsFirewall, Pf, None }

public interface IPlatform
{
    PlatformKind Kind { get; }
    string SshDirectoryPath { get; }
    string SshdConfigPath { get; }
    string AuthorizedKeysFilename { get; }
    bool IsElevated { get; }
    PackageManager PackageManager { get; }
    FirewallType FirewallType { get; }
    Task SetFilePermissionsAsync(string path, SshFileKind kind);
    Task<bool> CheckFilePermissionsAsync(string path, SshFileKind kind);
    Task RestartSshServiceAsync();
    Task<bool> IsSshServiceRunningAsync();
    Task<string> RunCommandAsync(string command, string arguments);
    Task<(int ExitCode, string StdOut, string StdErr)> TryRunCommandAsync(string command, string arguments);
}

public enum SshFileKind { SshDirectory, PrivateKey, AuthorizedKeys, Config }
