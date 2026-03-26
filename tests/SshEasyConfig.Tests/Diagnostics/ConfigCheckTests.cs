using SshEasyConfig.Core;
using SshEasyConfig.Diagnostics;
using NSubstitute;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Tests.Diagnostics;

public class ConfigCheckTests
{
    [Fact]
    public void CheckSshdConfig_SecureConfig_AllPass()
    {
        var content = "PasswordAuthentication no\nPubkeyAuthentication yes\nPermitRootLogin no\n";
        var platform = Substitute.For<IPlatform>();
        var results = ConfigCheck.CheckSshdConfig(content, platform);
        Assert.All(results, r => Assert.Equal(CheckStatus.Pass, r.Status));
    }

    [Fact]
    public void CheckSshdConfig_InsecureConfig_HasWarnings()
    {
        var content = "PasswordAuthentication yes\nPermitRootLogin yes\n";
        var platform = Substitute.For<IPlatform>();
        var results = ConfigCheck.CheckSshdConfig(content, platform);
        Assert.Contains(results, r => r.Status == CheckStatus.Warn);
    }
}
