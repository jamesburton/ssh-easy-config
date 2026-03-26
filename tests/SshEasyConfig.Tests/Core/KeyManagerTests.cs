using NSubstitute;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Tests.Core;

public class KeyManagerTests
{
    [Fact]
    public void GenerateKeyPair_ReturnsValidKeys()
    {
        var keyPair = KeyManager.GenerateKeyPair("test@host");

        Assert.StartsWith("ssh-ed25519 ", keyPair.PublicKeyOpenSsh);
        Assert.Contains("test@host", keyPair.PublicKeyOpenSsh);
        Assert.StartsWith("-----BEGIN OPENSSH PRIVATE KEY-----", keyPair.PrivateKeyPem);
        Assert.Contains("-----END OPENSSH PRIVATE KEY-----", keyPair.PrivateKeyPem);
        Assert.Equal(32, keyPair.RawPublicKey.Length);
    }

    [Fact]
    public void GenerateKeyPair_ProducesDifferentKeysEachCall()
    {
        var keyPair1 = KeyManager.GenerateKeyPair("test@host");
        var keyPair2 = KeyManager.GenerateKeyPair("test@host");

        Assert.NotEqual(keyPair1.RawPublicKey, keyPair2.RawPublicKey);
        Assert.NotEqual(keyPair1.PublicKeyOpenSsh, keyPair2.PublicKeyOpenSsh);
        Assert.NotEqual(keyPair1.PrivateKeyPem, keyPair2.PrivateKeyPem);
    }

    [Fact]
    public async Task SaveKeyPair_WritesFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ssh-test-{Guid.NewGuid()}");
        try
        {
            var platform = Substitute.For<IPlatform>();
            platform.SshDirectoryPath.Returns(tempDir);

            var keyPair = KeyManager.GenerateKeyPair("test@host");
            await KeyManager.SaveKeyPairAsync(platform, keyPair, "id_ed25519");

            var privateKeyPath = Path.Combine(tempDir, "id_ed25519");
            var publicKeyPath = Path.Combine(tempDir, "id_ed25519.pub");

            Assert.True(File.Exists(privateKeyPath));
            Assert.True(File.Exists(publicKeyPath));

            var privateContent = await File.ReadAllTextAsync(privateKeyPath);
            var publicContent = await File.ReadAllTextAsync(publicKeyPath);

            Assert.StartsWith("-----BEGIN OPENSSH PRIVATE KEY-----", privateContent);
            Assert.StartsWith("ssh-ed25519 ", publicContent);

            await platform.Received().SetFilePermissionsAsync(privateKeyPath, SshFileKind.PrivateKey);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
