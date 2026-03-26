using SshEasyConfig.Platform;

namespace SshEasyConfig.Diagnostics;

public static class WslCheck
{
    public static List<DiagnosticResult> Check(IPlatform platform)
    {
        var results = new List<DiagnosticResult>();

        if (platform is not WslPlatform wsl)
        {
            results.Add(new DiagnosticResult(
                "WSL Check",
                CheckStatus.Skip,
                "Not running in WSL"));
            return results;
        }

        // Check WSL version
        if (wsl.IsWsl2)
        {
            results.Add(new DiagnosticResult(
                "WSL Version",
                CheckStatus.Pass,
                "Running in WSL2"));

            results.Add(new DiagnosticResult(
                "WSL2 Networking",
                CheckStatus.Warn,
                "WSL2 uses a virtual network adapter. SSH connections from Windows to WSL2 may need port forwarding.",
                "Use 'netsh interface portproxy add' on Windows or connect via localhost if using mirrored networking"));
        }
        else
        {
            results.Add(new DiagnosticResult(
                "WSL Version",
                CheckStatus.Pass,
                "Running in WSL1 (shared network stack with Windows)"));
        }

        // Check Windows .ssh directory
        var winSshDir = wsl.WindowsSshDirectoryPath;
        if (winSshDir != null)
        {
            if (Directory.Exists(winSshDir))
            {
                results.Add(new DiagnosticResult(
                    "Windows .ssh Directory",
                    CheckStatus.Pass,
                    $"Windows .ssh directory found at {winSshDir}"));

                // Check if keys exist in Windows side
                var winKey = Path.Combine(winSshDir, "id_ed25519.pub");
                if (File.Exists(winKey))
                {
                    results.Add(new DiagnosticResult(
                        "Windows SSH Key",
                        CheckStatus.Pass,
                        "Ed25519 public key found in Windows .ssh directory"));
                }
                else
                {
                    results.Add(new DiagnosticResult(
                        "Windows SSH Key",
                        CheckStatus.Warn,
                        "No Ed25519 key found in Windows .ssh directory",
                        "Consider generating keys on the Windows side too, or copying from WSL"));
                }
            }
            else
            {
                results.Add(new DiagnosticResult(
                    "Windows .ssh Directory",
                    CheckStatus.Warn,
                    "Windows .ssh directory not found",
                    "Create it from Windows: mkdir %USERPROFILE%\\.ssh"));
            }
        }

        return results;
    }
}
