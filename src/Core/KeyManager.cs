using System.Security.Cryptography;
using NSec.Cryptography;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public record SshKeyPair(string PublicKeyOpenSsh, string PrivateKeyPem, byte[] RawPublicKey);

public static class KeyManager
{
    private static readonly SignatureAlgorithm Ed25519Algorithm = SignatureAlgorithm.Ed25519;

    public static SshKeyPair GenerateKeyPair(string comment)
    {
        using var key = Key.Create(Ed25519Algorithm,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

        var rawPublicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var rawPrivateKey = key.Export(KeyBlobFormat.RawPrivateKey);

        try
        {
            var publicKeyOpenSsh = OpenSshKeyFormat.FormatPublicKey(rawPublicKey, comment);
            var privateKeyPem = OpenSshKeyFormat.FormatPrivateKey(rawPublicKey, rawPrivateKey, comment);

            return new SshKeyPair(publicKeyOpenSsh, privateKeyPem, rawPublicKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawPrivateKey);
        }
    }

    public static async Task SaveKeyPairAsync(IPlatform platform, SshKeyPair keyPair, string keyName)
    {
        var sshDir = platform.SshDirectoryPath;

        if (!Directory.Exists(sshDir))
            Directory.CreateDirectory(sshDir);

        var privateKeyPath = Path.Combine(sshDir, keyName);
        var publicKeyPath = Path.Combine(sshDir, $"{keyName}.pub");

        await File.WriteAllTextAsync(privateKeyPath, keyPair.PrivateKeyPem);
        await File.WriteAllTextAsync(publicKeyPath, keyPair.PublicKeyOpenSsh);

        await platform.SetFilePermissionsAsync(privateKeyPath, SshFileKind.PrivateKey);
    }

    public static bool KeyExists(IPlatform platform, string keyName)
    {
        var privateKeyPath = Path.Combine(platform.SshDirectoryPath, keyName);
        return File.Exists(privateKeyPath);
    }

    public static async Task<string> ReadPublicKeyAsync(IPlatform platform, string keyName)
    {
        var publicKeyPath = Path.Combine(platform.SshDirectoryPath, $"{keyName}.pub");
        return await File.ReadAllTextAsync(publicKeyPath);
    }
}
