using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class PairingProtocolTests
{
    [Fact]
    public void GeneratePairingCode_Returns6Digits()
    {
        var code = PairingProtocol.GeneratePairingCode();
        Assert.Equal(6, code.Length);
        Assert.True(int.TryParse(code, out var n));
        Assert.InRange(n, 100000, 999999);
    }

    [Fact]
    public void GeneratePairingCode_ProducesDifferentCodes()
    {
        var codes = Enumerable.Range(0, 10).Select(_ => PairingProtocol.GeneratePairingCode()).ToHashSet();
        Assert.True(codes.Count > 1);
    }

    [Fact]
    public void DeriveKey_SameInputs_ProduceSameKey()
    {
        var key1 = PairingProtocol.DeriveKey("123456", "salt123");
        var key2 = PairingProtocol.DeriveKey("123456", "salt123");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKey_DifferentCodes_ProduceDifferentKeys()
    {
        var key1 = PairingProtocol.DeriveKey("123456", "salt123");
        var key2 = PairingProtocol.DeriveKey("654321", "salt123");
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveKey_Returns32Bytes()
    {
        var key = PairingProtocol.DeriveKey("123456", "salt");
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        var key = PairingProtocol.DeriveKey("123456", "salt");
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Hello, SSH!");
        var (ciphertext, nonce) = PairingProtocol.Encrypt(key, plaintext);
        var decrypted = PairingProtocol.Decrypt(key, ciphertext, nonce);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Decrypt_WrongKey_Throws()
    {
        var key1 = PairingProtocol.DeriveKey("123456", "salt");
        var key2 = PairingProtocol.DeriveKey("000000", "salt");
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Secret");
        var (ciphertext, nonce) = PairingProtocol.Encrypt(key1, plaintext);
        Assert.ThrowsAny<Exception>(() => PairingProtocol.Decrypt(key2, ciphertext, nonce));
    }

    [Fact]
    public void ComputeFingerprint_DeterministicForSameKey()
    {
        var fp1 = PairingProtocol.ComputeFingerprint("ssh-ed25519 AAAA test");
        var fp2 = PairingProtocol.ComputeFingerprint("ssh-ed25519 AAAA test");
        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void ComputeFingerprint_DifferentForDifferentKeys()
    {
        var fp1 = PairingProtocol.ComputeFingerprint("ssh-ed25519 AAAA test1");
        var fp2 = PairingProtocol.ComputeFingerprint("ssh-ed25519 BBBB test2");
        Assert.NotEqual(fp1, fp2);
    }
}
