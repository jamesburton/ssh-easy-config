using NSubstitute;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Tests.Core;

public class FirewallManagerTests
{
    [Fact]
    public void GetOpenPortCommand_WindowsFirewall_UsesNetshWithOpenSSH()
    {
        var (command, arguments) = FirewallManager.GetOpenPortCommand(FirewallType.WindowsFirewall, 22);

        Assert.Equal("netsh", command);
        Assert.Contains("22", arguments);
        Assert.Contains("OpenSSH", arguments);
    }

    [Fact]
    public void GetOpenPortCommand_Ufw_UsesSudoUfwWith22()
    {
        var (command, arguments) = FirewallManager.GetOpenPortCommand(FirewallType.Ufw, 22);

        Assert.Equal("sudo", command);
        Assert.Contains("ufw", arguments);
        Assert.Contains("22", arguments);
    }

    [Fact]
    public void GetOpenPortCommand_Firewalld_UsesSudoFirewallCmd()
    {
        var (command, arguments) = FirewallManager.GetOpenPortCommand(FirewallType.Firewalld, 22);

        Assert.Equal("sudo", command);
        Assert.Contains("firewall-cmd", arguments);
    }

    [Fact]
    public void GetOpenPortCommand_None_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FirewallManager.GetOpenPortCommand(FirewallType.None, 22));
    }

    [Fact]
    public async Task IsPort22Open_WindowsFirewall_WithMatchingRule_ReturnsTrue()
    {
        var platform = Substitute.For<IPlatform>();
        platform.FirewallType.Returns(FirewallType.WindowsFirewall);
        platform.TryRunCommandAsync("netsh", Arg.Any<string>())
            .Returns((0,
                "Rule Name: OpenSSH-Server\nDirection: In\nLocalPort: 22\nAction: Allow\n",
                ""));

        var result = await FirewallManager.IsPort22OpenAsync(platform);

        Assert.True(result);
    }

    [Fact]
    public async Task IsPort22Open_WindowsFirewall_NoMatchingRule_ReturnsFalse()
    {
        var platform = Substitute.For<IPlatform>();
        platform.FirewallType.Returns(FirewallType.WindowsFirewall);
        platform.TryRunCommandAsync("netsh", Arg.Any<string>())
            .Returns((0,
                "Rule Name: SomeOtherRule\nDirection: In\nLocalPort: 80\nAction: Allow\n",
                ""));

        var result = await FirewallManager.IsPort22OpenAsync(platform);

        Assert.False(result);
    }

    [Fact]
    public async Task IsPort22Open_Pf_ReturnsTrue()
    {
        var platform = Substitute.For<IPlatform>();
        platform.FirewallType.Returns(FirewallType.Pf);

        var result = await FirewallManager.IsPort22OpenAsync(platform);

        Assert.True(result);
    }

    [Fact]
    public async Task IsPort22Open_None_ReturnsTrue()
    {
        var platform = Substitute.For<IPlatform>();
        platform.FirewallType.Returns(FirewallType.None);

        var result = await FirewallManager.IsPort22OpenAsync(platform);

        Assert.True(result);
    }

    [Fact]
    public void GetOpenPortCommand_Iptables_UsesSudoIptables()
    {
        var (command, arguments) = FirewallManager.GetOpenPortCommand(FirewallType.Iptables, 22);

        Assert.Equal("sudo", command);
        Assert.Contains("iptables", arguments);
        Assert.Contains("22", arguments);
    }
}
