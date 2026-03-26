using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class KeyBundleTests
{
    [Fact]
    public void RoundTrips_ThroughJson()
    {
        var bundle = new KeyBundle(
            PublicKey: "ssh-ed25519 AAAA test@host",
            Hostname: "mypc",
            Username: "james",
            SuggestedAlias: "mypc-james");

        var json = bundle.ToJson();
        var parsed = KeyBundle.FromJson(json);

        Assert.Equal(bundle.PublicKey, parsed.PublicKey);
        Assert.Equal(bundle.Hostname, parsed.Hostname);
        Assert.Equal(bundle.Username, parsed.Username);
        Assert.Equal(bundle.SuggestedAlias, parsed.SuggestedAlias);
    }

    [Fact]
    public void ToClipboardText_ProducesBase64Block()
    {
        var bundle = new KeyBundle("ssh-ed25519 AAAA test", "host", "user", "alias");
        var text = bundle.ToClipboardText();
        Assert.StartsWith("--- BEGIN SSH-EASY-CONFIG ---", text);
        Assert.Contains("--- END SSH-EASY-CONFIG ---", text);
    }

    [Fact]
    public void FromClipboardText_RoundTrips()
    {
        var bundle = new KeyBundle("ssh-ed25519 AAAA test", "host", "user", "alias");
        var text = bundle.ToClipboardText();
        var parsed = KeyBundle.FromClipboardText(text);
        Assert.Equal(bundle.PublicKey, parsed.PublicKey);
        Assert.Equal(bundle.Hostname, parsed.Hostname);
    }

    [Fact]
    public void SaveToFile_And_LoadFromFile_RoundTrips()
    {
        var bundle = new KeyBundle("ssh-ed25519 AAAA test", "host", "user", "alias");
        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.sshec");
        try
        {
            bundle.SaveToFile(path);
            Assert.True(File.Exists(path));
            var loaded = KeyBundle.LoadFromFile(path);
            Assert.Equal(bundle.PublicKey, loaded.PublicKey);
            Assert.Equal(bundle.Hostname, loaded.Hostname);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
