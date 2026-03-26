namespace SshEasyConfig.Platform;

public enum PlatformKind { Linux, MacOS, Windows, Wsl }

public interface IPlatform
{
    PlatformKind Kind { get; }
    string SshDirectoryPath { get; }
    string SshdConfigPath { get; }
    string AuthorizedKeysFilename { get; }
    Task SetFilePermissionsAsync(string path, SshFileKind kind);
    Task<bool> CheckFilePermissionsAsync(string path, SshFileKind kind);
    Task RestartSshServiceAsync();
    Task<bool> IsSshServiceRunningAsync();
    Task<string> RunCommandAsync(string command, string arguments);
}

public enum SshFileKind { SshDirectory, PrivateKey, AuthorizedKeys, Config }
