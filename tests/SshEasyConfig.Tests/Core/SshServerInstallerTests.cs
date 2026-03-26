using NSubstitute;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Tests.Core;

public class SshServerInstallerTests
{
    [Fact]
    public async Task IsSshdInstalled_WhenRunning_ReturnsTrue()
    {
        var platform = Substitute.For<IPlatform>();
        platform.IsSshServiceRunningAsync().Returns(true);

        var result = await SshServerInstaller.IsSshdInstalledAsync(platform);

        Assert.True(result);
    }

    [Fact]
    public async Task IsSshdInstalled_WhenSshdConfigMissing_ReturnsFalse()
    {
        var platform = Substitute.For<IPlatform>();
        platform.IsSshServiceRunningAsync().Returns(false);
        platform.SshdConfigPath.Returns("/nonexistent/path/sshd_config");
        platform.Kind.Returns(PlatformKind.Linux);
        platform.TryRunCommandAsync("which", "sshd")
            .Returns((1, "", "not found"));

        var result = await SshServerInstaller.IsSshdInstalledAsync(platform);

        Assert.False(result);
    }

    [Fact]
    public void GetSshServiceName_Windows_ReturnsSshd()
    {
        var name = SshServerInstaller.GetSshServiceName(PlatformKind.Windows);
        Assert.Equal("sshd", name);
    }

    [Fact]
    public void GetSshServiceName_MacOS_ReturnsComOpensshSshd()
    {
        var name = SshServerInstaller.GetSshServiceName(PlatformKind.MacOS);
        Assert.Equal("com.openssh.sshd", name);
    }

    [Fact]
    public void GetInstallCommand_Windows_UsesPowershellWithAddWindowsCapability()
    {
        var (command, arguments) = SshServerInstaller.GetInstallCommand(PlatformKind.Windows, PackageManager.None);

        Assert.Equal("powershell", command);
        Assert.Contains("Add-WindowsCapability", arguments);
    }

    [Fact]
    public void GetInstallCommand_LinuxApt_UsesSudoApt()
    {
        var (command, arguments) = SshServerInstaller.GetInstallCommand(PlatformKind.Linux, PackageManager.Apt);

        Assert.Equal("sudo", command);
        Assert.Contains("apt", arguments);
        Assert.Contains("openssh-server", arguments);
    }

    [Fact]
    public void GetInstallCommand_LinuxDnf_UsesSudoDnf()
    {
        var (command, arguments) = SshServerInstaller.GetInstallCommand(PlatformKind.Linux, PackageManager.Dnf);

        Assert.Equal("sudo", command);
        Assert.Contains("dnf", arguments);
        Assert.Contains("openssh-server", arguments);
    }

    [Fact]
    public async Task InstallAsync_CallsRunCommandWithCorrectArgs()
    {
        var platform = Substitute.For<IPlatform>();
        platform.Kind.Returns(PlatformKind.Linux);
        platform.PackageManager.Returns(PackageManager.Apt);
        platform.RunCommandAsync("sudo", Arg.Any<string>()).Returns("ok");

        await SshServerInstaller.InstallAsync(platform);

        await platform.Received(1).RunCommandAsync("sudo", Arg.Is<string>(s => s.Contains("apt")));
    }

    [Fact]
    public async Task StartAsync_Windows_UsesScStart()
    {
        var platform = Substitute.For<IPlatform>();
        platform.Kind.Returns(PlatformKind.Windows);
        platform.RunCommandAsync("sc", "start sshd").Returns("ok");

        await SshServerInstaller.StartAsync(platform);

        await platform.Received(1).RunCommandAsync("sc", "start sshd");
    }

    [Fact]
    public async Task EnableAsync_Windows_UsesScConfig()
    {
        var platform = Substitute.For<IPlatform>();
        platform.Kind.Returns(PlatformKind.Windows);
        platform.RunCommandAsync("sc", "config sshd start= auto").Returns("ok");

        await SshServerInstaller.EnableAsync(platform);

        await platform.Received(1).RunCommandAsync("sc", "config sshd start= auto");
    }
}
