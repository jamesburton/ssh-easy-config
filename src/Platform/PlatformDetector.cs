namespace SshEasyConfig.Platform;

public static class PlatformDetector
{
    public static IPlatform Detect()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsPlatform();

        if (OperatingSystem.IsMacOS())
            return new MacOsPlatform();

        // Linux — check for WSL
        if (IsWsl())
            return new WslPlatform();

        return new LinuxPlatform();
    }

    private static bool IsWsl()
    {
        // Check WSL_DISTRO_NAME environment variable
        var wslDistro = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");
        if (!string.IsNullOrEmpty(wslDistro))
            return true;

        // Check /proc/version for "microsoft" or "WSL"
        try
        {
            if (File.Exists("/proc/version"))
            {
                var version = File.ReadAllText("/proc/version");
                if (version.Contains("microsoft", StringComparison.OrdinalIgnoreCase) ||
                    version.Contains("WSL", StringComparison.Ordinal))
                    return true;
            }
        }
        catch
        {
            // Ignore read errors
        }

        return false;
    }
}
