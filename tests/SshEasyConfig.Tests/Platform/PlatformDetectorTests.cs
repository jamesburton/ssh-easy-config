using SshEasyConfig.Platform;

namespace SshEasyConfig.Tests.Platform;

public class PlatformDetectorTests
{
    [Fact]
    public void Detect_ReturnsNonNull()
    {
        var platform = PlatformDetector.Detect();
        Assert.NotNull(platform);
    }

    [Fact]
    public void Detect_ReturnsPlatformMatchingOs()
    {
        var platform = PlatformDetector.Detect();
        if (OperatingSystem.IsWindows())
            Assert.True(platform.Kind == PlatformKind.Windows || platform.Kind == PlatformKind.Wsl);
        else if (OperatingSystem.IsMacOS())
            Assert.Equal(PlatformKind.MacOS, platform.Kind);
        else if (OperatingSystem.IsLinux())
            Assert.True(platform.Kind == PlatformKind.Linux || platform.Kind == PlatformKind.Wsl);
    }
}
