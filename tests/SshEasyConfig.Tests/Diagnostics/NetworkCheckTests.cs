using SshEasyConfig.Diagnostics;

namespace SshEasyConfig.Tests.Diagnostics;

public class NetworkCheckTests
{
    [Fact]
    public async Task CheckDns_Localhost_Resolves()
    {
        var results = await NetworkCheck.CheckDnsAsync("localhost");
        Assert.Contains(results, r => r.CheckName == "DNS Resolution" && r.Status == CheckStatus.Pass);
    }

    [Fact]
    public async Task CheckDns_InvalidHost_Fails()
    {
        var results = await NetworkCheck.CheckDnsAsync("this-host-does-not-exist-12345.invalid");
        Assert.Contains(results, r => r.CheckName == "DNS Resolution" && r.Status == CheckStatus.Fail);
    }

    [Fact]
    public async Task CheckPort_ClosedPort_Fails()
    {
        var result = await NetworkCheck.CheckPortAsync("127.0.0.1", 1, TimeSpan.FromSeconds(1));
        Assert.Equal(CheckStatus.Fail, result.Status);
    }
}
