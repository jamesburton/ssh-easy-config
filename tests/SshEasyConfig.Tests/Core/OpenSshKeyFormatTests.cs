using NSec.Cryptography;
using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class OpenSshKeyFormatTests
{
    private readonly byte[] _testPublicKey;
    private readonly byte[] _testPrivateKey;

    public OpenSshKeyFormatTests()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        _testPublicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        _testPrivateKey = key.Export(KeyBlobFormat.RawPrivateKey);
    }

    [Fact]
    public void FormatPublicKey_StartsWithKeyType()
    {
        var result = OpenSshKeyFormat.FormatPublicKey(_testPublicKey, "user@machine");
        Assert.StartsWith("ssh-ed25519 ", result);
    }

    [Fact]
    public void FormatPublicKey_EndsWithComment()
    {
        var result = OpenSshKeyFormat.FormatPublicKey(_testPublicKey, "user@machine");
        Assert.EndsWith(" user@machine", result);
    }

    [Fact]
    public void FormatPublicKey_Base64MiddleSection()
    {
        var result = OpenSshKeyFormat.FormatPublicKey(_testPublicKey, "user@machine");
        var parts = result.Split(' ');
        Assert.Equal(3, parts.Length);

        // Middle part should be valid base64
        var decoded = Convert.FromBase64String(parts[1]);
        Assert.True(decoded.Length > 0);
    }

    [Fact]
    public void FormatPrivateKey_HasCorrectPemBoundaries()
    {
        var result = OpenSshKeyFormat.FormatPrivateKey(_testPublicKey, _testPrivateKey, "user@machine");
        Assert.StartsWith("-----BEGIN OPENSSH PRIVATE KEY-----", result);
        Assert.Contains("-----END OPENSSH PRIVATE KEY-----", result);
    }

    [Fact]
    public void FormatPublicKey_RoundTrips_WireFormat()
    {
        var result = OpenSshKeyFormat.FormatPublicKey(_testPublicKey, "user@machine");
        var parts = result.Split(' ');
        var wireBytes = Convert.FromBase64String(parts[1]);

        using var ms = new MemoryStream(wireBytes);
        using var reader = new BinaryReader(ms);

        // Read key type string
        var keyTypeLength = ReadUInt32BigEndian(reader);
        var keyTypeBytes = reader.ReadBytes((int)keyTypeLength);
        var keyType = System.Text.Encoding.ASCII.GetString(keyTypeBytes);
        Assert.Equal("ssh-ed25519", keyType);

        // Read raw key
        var keyLength = ReadUInt32BigEndian(reader);
        Assert.Equal(32u, keyLength);
        var rawKey = reader.ReadBytes((int)keyLength);
        Assert.Equal(_testPublicKey, rawKey);
    }

    private static uint ReadUInt32BigEndian(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes);
    }
}
