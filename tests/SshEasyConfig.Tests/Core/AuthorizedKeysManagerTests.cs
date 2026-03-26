using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class AuthorizedKeysManagerTests
{
    [Fact]
    public void AddKey_ToEmpty_AddsLine()
    {
        var result = AuthorizedKeysManager.AddKey("", "ssh-ed25519 AAAA test@host");
        Assert.Equal("ssh-ed25519 AAAA test@host\n", result);
    }

    [Fact]
    public void AddKey_PreservesExistingEntries()
    {
        var content = "ssh-rsa BBBB existing@host\n";
        var result = AuthorizedKeysManager.AddKey(content, "ssh-ed25519 AAAA new@host");
        Assert.Contains("ssh-rsa BBBB existing@host", result);
        Assert.Contains("ssh-ed25519 AAAA new@host", result);
    }

    [Fact]
    public void AddKey_DeduplicatesSameKey()
    {
        var content = "ssh-ed25519 AAAA test@host\n";
        var result = AuthorizedKeysManager.AddKey(content, "ssh-ed25519 AAAA test@host");
        var occurrences = result.Split('\n').Count(l => l.Trim() == "ssh-ed25519 AAAA test@host");
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void AddKey_DeduplicatesByKeyData_IgnoringComment()
    {
        var content = "ssh-ed25519 AAAA oldcomment\n";
        var result = AuthorizedKeysManager.AddKey(content, "ssh-ed25519 AAAA newcomment");
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Contains("oldcomment", lines[0]);
    }

    [Fact]
    public void AddKey_PreservesComments()
    {
        var content = "# My important comment\nssh-rsa BBBB existing\n";
        var result = AuthorizedKeysManager.AddKey(content, "ssh-ed25519 AAAA new");
        Assert.Contains("# My important comment", result);
    }

    [Fact]
    public void RemoveKey_RemovesMatchingEntry()
    {
        var content = "ssh-ed25519 AAAA test@host\nssh-rsa BBBB other@host\n";
        var result = AuthorizedKeysManager.RemoveKey(content, "ssh-ed25519 AAAA");
        Assert.DoesNotContain("ssh-ed25519 AAAA", result);
        Assert.Contains("ssh-rsa BBBB other@host", result);
    }

    [Fact]
    public void RemoveKey_PreservesOtherEntries()
    {
        var content = "# comment\nssh-ed25519 AAAA remove\nssh-rsa BBBB keep\n";
        var result = AuthorizedKeysManager.RemoveKey(content, "ssh-ed25519 AAAA");
        Assert.Contains("# comment", result);
        Assert.Contains("ssh-rsa BBBB keep", result);
    }
}
