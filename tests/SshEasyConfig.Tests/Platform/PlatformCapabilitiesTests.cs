using SshEasyConfig.Platform;

namespace SshEasyConfig.Tests.Platform;

public class PlatformCapabilitiesTests
{
    [Fact]
    public void Platform_HasIsElevated()
    {
        var platform = PlatformDetector.Detect();
        _ = platform.IsElevated;
    }

    [Fact]
    public void Platform_HasPackageManager()
    {
        var platform = PlatformDetector.Detect();
        var pm = platform.PackageManager;
        Assert.True(Enum.IsDefined(pm));
    }

    [Fact]
    public void Platform_HasFirewallType()
    {
        var platform = PlatformDetector.Detect();
        var fw = platform.FirewallType;
        Assert.True(Enum.IsDefined(fw));
    }
}
