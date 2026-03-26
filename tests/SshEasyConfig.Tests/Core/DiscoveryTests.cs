using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class DiscoveryTests
{
    [Fact]
    public void ServiceName_IsCorrect()
    {
        Assert.Equal("_ssh-easy._tcp.local", Discovery.ServiceType);
    }

    [Fact]
    public void CreateServiceProfile_ContainsPort()
    {
        var profile = Discovery.CreateServiceProfile("myhost", 12345);
        Assert.Equal("myhost", profile.HostName);
        Assert.Equal(12345, profile.Port);
    }
}
