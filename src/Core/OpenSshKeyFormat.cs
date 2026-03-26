using System.Buffers.Binary;
using System.Text;

namespace SshEasyConfig.Core;

public static class OpenSshKeyFormat
{
    private const string KeyType = "ssh-ed25519";
    private const int BlockSize = 8;

    public static string FormatPublicKey(byte[] rawPublicKey, string comment)
    {
        var wireBytes = EncodePublicKeyWire(rawPublicKey);
        var base64 = Convert.ToBase64String(wireBytes);
        return $"{KeyType} {base64} {comment}";
    }

    public static string FormatPrivateKey(byte[] rawPublicKey, byte[] rawPrivateKey, string comment)
    {
        using var ms = new MemoryStream();

        // Auth magic
        var magic = Encoding.ASCII.GetBytes("openssh-key-v1\0");
        ms.Write(magic);

        // Cipher: "none"
        WriteString(ms, "none");

        // KDF: "none"
        WriteString(ms, "none");

        // KDF options: empty
        WriteUInt32(ms, 0);

        // Number of keys: 1
        WriteUInt32(ms, 1);

        // Public key wire block
        var pubWire = EncodePublicKeyWire(rawPublicKey);
        WriteUInt32(ms, (uint)pubWire.Length);
        ms.Write(pubWire);

        // Private key section
        var privSection = BuildPrivateSection(rawPublicKey, rawPrivateKey, comment);
        WriteUInt32(ms, (uint)privSection.Length);
        ms.Write(privSection);

        var allBytes = ms.ToArray();
        var base64 = Convert.ToBase64String(allBytes);

        var sb = new StringBuilder();
        sb.AppendLine("-----BEGIN OPENSSH PRIVATE KEY-----");
        for (var i = 0; i < base64.Length; i += 70)
        {
            var len = Math.Min(70, base64.Length - i);
            sb.AppendLine(base64.Substring(i, len));
        }
        sb.Append("-----END OPENSSH PRIVATE KEY-----");
        // Add trailing newline
        sb.AppendLine();

        return sb.ToString();
    }

    private static byte[] EncodePublicKeyWire(byte[] rawPublicKey)
    {
        using var ms = new MemoryStream();
        WriteString(ms, KeyType);
        WriteBytes(ms, rawPublicKey);
        return ms.ToArray();
    }

    private static byte[] BuildPrivateSection(byte[] rawPublicKey, byte[] rawPrivateKey, string comment)
    {
        using var ms = new MemoryStream();

        // Random check integers (must match)
        var checkInt = (uint)Random.Shared.Next();
        WriteUInt32(ms, checkInt);
        WriteUInt32(ms, checkInt);

        // Key type
        WriteString(ms, KeyType);

        // Public key bytes
        WriteBytes(ms, rawPublicKey);

        // Expanded private key: 32-byte seed (private) + 32-byte public
        var expandedPrivate = new byte[64];
        Buffer.BlockCopy(rawPrivateKey, 0, expandedPrivate, 0, 32);
        Buffer.BlockCopy(rawPublicKey, 0, expandedPrivate, 32, 32);
        WriteBytes(ms, expandedPrivate);

        // Comment
        WriteString(ms, comment);

        // Padding: 1, 2, 3, ... up to block alignment
        var currentLen = (int)ms.Length;
        var paddingNeeded = BlockSize - (currentLen % BlockSize);
        if (paddingNeeded == BlockSize)
            paddingNeeded = 0;
        for (var i = 1; i <= paddingNeeded; i++)
            ms.WriteByte((byte)i);

        return ms.ToArray();
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, value);
        stream.Write(buf);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteBytes(Stream stream, byte[] data)
    {
        WriteUInt32(stream, (uint)data.Length);
        stream.Write(data);
    }
}
