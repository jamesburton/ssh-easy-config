namespace SshEasyConfig.Platform;

public class WslPlatform : LinuxPlatform
{
    public override PlatformKind Kind => PlatformKind.Wsl;

    public bool IsWsl2
    {
        get
        {
            try
            {
                if (File.Exists("/proc/version"))
                {
                    var version = File.ReadAllText("/proc/version");
                    return version.Contains("WSL2", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Ignore read errors
            }
            return false;
        }
    }

    public string? WindowsSshDirectoryPath
    {
        get
        {
            try
            {
                var userProfile = RunCommandAsync("cmd.exe", "/c echo %USERPROFILE%")
                    .GetAwaiter().GetResult().Trim();

                if (!string.IsNullOrEmpty(userProfile) && !userProfile.Contains("%USERPROFILE%"))
                {
                    var wslPath = ToWslPath(userProfile);
                    return Path.Combine(wslPath, ".ssh");
                }
            }
            catch
            {
                // cmd.exe may not be available
            }
            return null;
        }
    }

    /// <summary>
    /// Converts a WSL path to a Windows path.
    /// Example: /mnt/c/Users/james -> C:\Users\james
    /// </summary>
    public static string ToWindowsPath(string wslPath)
    {
        if (!wslPath.StartsWith("/mnt/", StringComparison.Ordinal) || wslPath.Length < 6)
            throw new ArgumentException($"Not a valid WSL mount path: {wslPath}", nameof(wslPath));

        var driveLetter = char.ToUpper(wslPath[5]);
        var rest = wslPath.Length > 6 ? wslPath[6..].Replace('/', '\\') : "\\";
        return $"{driveLetter}:{rest}";
    }

    /// <summary>
    /// Converts a Windows path to a WSL path.
    /// Example: C:\Users\james -> /mnt/c/Users/james
    /// </summary>
    public static string ToWslPath(string windowsPath)
    {
        if (windowsPath.Length < 2 || windowsPath[1] != ':')
            throw new ArgumentException($"Not a valid Windows path: {windowsPath}", nameof(windowsPath));

        var driveLetter = char.ToLower(windowsPath[0]);
        var rest = windowsPath.Length > 2 ? windowsPath[2..].Replace('\\', '/') : "/";
        return $"/mnt/{driveLetter}{rest}";
    }
}
