using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SshEasyConfig.Core;

public static class PairingProtocol
{
    private static readonly byte[] Info = Encoding.UTF8.GetBytes("ssh-easy-config-pairing-v1");

    public static string GeneratePairingCode()
    {
        return RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
    }

    public static byte[] DeriveKey(string pairingCode, string salt)
    {
        var ikm = Encoding.UTF8.GetBytes(pairingCode);
        var saltBytes = Encoding.UTF8.GetBytes(salt);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, saltBytes, Info);
    }

    public static (byte[] CiphertextWithTag, byte[] Nonce) Encrypt(byte[] key, byte[] plaintext)
    {
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var result = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, result, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, ciphertext.Length, tag.Length);

        return (result, nonce);
    }

    public static byte[] Decrypt(byte[] key, byte[] ciphertextWithTag, byte[] nonce)
    {
        if (ciphertextWithTag.Length < 16)
            throw new CryptographicException("Ciphertext too short.");

        var ciphertextLength = ciphertextWithTag.Length - 16;
        var ciphertext = ciphertextWithTag.AsSpan(0, ciphertextLength);
        var tag = ciphertextWithTag.AsSpan(ciphertextLength, 16);

        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    public static string ComputeFingerprint(string publicKeyLine)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(publicKeyLine));
        var first8 = hash.AsSpan(0, 8);
        var hex = Convert.ToHexString(first8).ToLowerInvariant();
        return $"SHA256:{hex[0..4]}:{hex[4..8]}:{hex[8..12]}:{hex[12..16]}";
    }
}
