using SshEasyConfig.Platform;

namespace SshEasyConfig.Diagnostics;

public static class AuthCheck
{
    public static Task<DiagnosticResult> CheckEd25519KeyExistsAsync(IPlatform platform)
    {
        var keyPath = Path.Combine(platform.SshDirectoryPath, "id_ed25519");

        if (File.Exists(keyPath))
        {
            return Task.FromResult(new DiagnosticResult(
                "Ed25519 Key",
                CheckStatus.Pass,
                $"Ed25519 key found at {keyPath}"));
        }

        return Task.FromResult(new DiagnosticResult(
            "Ed25519 Key",
            CheckStatus.Fail,
            $"No Ed25519 key found at {keyPath}",
            "Generate one with: ssh-easy-config keygen",
            AutoFixAvailable: true));
    }
}
