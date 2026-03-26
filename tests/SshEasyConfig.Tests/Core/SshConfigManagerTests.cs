using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class SshConfigManagerTests
{
    [Fact]
    public void ParseHosts_EmptyContent_ReturnsEmpty()
    {
        var hosts = SshConfigManager.ParseHosts("");
        Assert.Empty(hosts);
    }

    [Fact]
    public void ParseHosts_SingleHost_ReturnsEntry()
    {
        var content = "Host myserver\n    HostName 192.168.1.100\n    User admin\n    Port 22\n    IdentityFile ~/.ssh/id_ed25519\n";
        var hosts = SshConfigManager.ParseHosts(content);
        Assert.Single(hosts);
        Assert.Equal("myserver", hosts[0].Alias);
        Assert.Equal("192.168.1.100", hosts[0].HostName);
        Assert.Equal("admin", hosts[0].User);
        Assert.Equal(22, hosts[0].Port);
    }

    [Fact]
    public void ParseHosts_MultipleHosts()
    {
        var content = "Host server1\n    HostName 10.0.0.1\n    User alice\n\nHost server2\n    HostName 10.0.0.2\n    User bob\n";
        var hosts = SshConfigManager.ParseHosts(content);
        Assert.Equal(2, hosts.Count);
        Assert.Equal("server1", hosts[0].Alias);
        Assert.Equal("server2", hosts[1].Alias);
    }

    [Fact]
    public void AddHost_ToEmptyConfig()
    {
        var entry = new SshHostEntry("myserver", "192.168.1.100", "admin", 22, "~/.ssh/id_ed25519");
        var result = SshConfigManager.AddHost("", entry);
        Assert.Contains("Host myserver", result);
        Assert.Contains("HostName 192.168.1.100", result);
        Assert.Contains("User admin", result);
        Assert.Contains("IdentityFile ~/.ssh/id_ed25519", result);
    }

    [Fact]
    public void AddHost_PreservesExistingContent()
    {
        var existing = "# Global settings\nHost *\n    ServerAliveInterval 60\n\nHost existing\n    HostName 10.0.0.1\n";
        var entry = new SshHostEntry("newhost", "10.0.0.2", "root", 2222, null);
        var result = SshConfigManager.AddHost(existing, entry);
        Assert.Contains("# Global settings", result);
        Assert.Contains("Host existing", result);
        Assert.Contains("Host newhost", result);
        Assert.Contains("Port 2222", result);
    }

    [Fact]
    public void RemoveHost_RemovesEntireBlock()
    {
        var content = "Host keep\n    HostName 10.0.0.1\n\nHost remove\n    HostName 10.0.0.2\n    User admin\n\nHost alsokeep\n    HostName 10.0.0.3\n";
        var result = SshConfigManager.RemoveHost(content, "remove");
        Assert.DoesNotContain("Host remove", result);
        Assert.DoesNotContain("10.0.0.2", result);
        Assert.Contains("Host keep", result);
        Assert.Contains("Host alsokeep", result);
    }

    [Fact]
    public void AddHost_DoesNotAddDefaultPort()
    {
        var entry = new SshHostEntry("myserver", "10.0.0.1", "admin", 22, null);
        var result = SshConfigManager.AddHost("", entry);
        Assert.DoesNotContain("Port", result);
    }

    [Fact]
    public void AddHost_IncludesNonDefaultPort()
    {
        var entry = new SshHostEntry("myserver", "10.0.0.1", "admin", 2222, null);
        var result = SshConfigManager.AddHost("", entry);
        Assert.Contains("Port 2222", result);
    }
}
