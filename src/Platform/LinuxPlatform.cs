using System.Diagnostics;

namespace SshEasyConfig.Platform;

public class LinuxPlatform : IPlatform
{
    public virtual PlatformKind Kind => PlatformKind.Linux;

    public virtual string SshDirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public virtual string SshdConfigPath => "/etc/ssh/sshd_config";

    public virtual string AuthorizedKeysFilename => "authorized_keys";

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
}
