using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SshEasyConfig.Core;

public class NetworkExchange
{
    private const int MaxFrameSize = 1 * 1024 * 1024; // 1MB
    private readonly int _port;

    public NetworkExchange(int port = 0)
    {
        _port = port;
    }

    public int Port => _port;

    public async Task<KeyBundle?> ListenAndExchangeAsync(
        KeyBundle localBundle,
        string pairingCode,
        Action<string> onStatus,
        CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        try
        {
            var actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            onStatus($"Listening on port {actualPort}");

            using var client = await listener.AcceptTcpClientAsync(ct);
            onStatus("Client connected");

            await using var stream = client.GetStream();

            // Step 1: Send salt
            var salt = Guid.NewGuid().ToString();
            await WriteFrameAsync(stream, Encoding.UTF8.GetBytes(salt), ct);

            // Step 2: Derive key
            var key = PairingProtocol.DeriveKey(pairingCode, salt);

            // Step 3: Encrypt and send local bundle
            var localJson = Encoding.UTF8.GetBytes(localBundle.ToJson());
            var (ciphertext, nonce) = PairingProtocol.Encrypt(key, localJson);
            await WriteFrameAsync(stream, nonce, ct);
            await WriteFrameAsync(stream, ciphertext, ct);

            // Step 4: Receive remote bundle
            var remoteNonce = await ReadFrameAsync(stream, ct);
            var remoteCiphertext = await ReadFrameAsync(stream, ct);

            try
            {
                var remotePlaintext = PairingProtocol.Decrypt(key, remoteCiphertext, remoteNonce);
                var remoteBundle = KeyBundle.FromJson(Encoding.UTF8.GetString(remotePlaintext));
                onStatus("Exchange complete");
                return remoteBundle;
            }
            catch
            {
                onStatus("Pairing code mismatch");
                return null;
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    public async Task<KeyBundle?> ConnectAndExchangeAsync(
        string host,
        int port,
        KeyBundle localBundle,
        string pairingCode,
        CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct);

        await using var stream = client.GetStream();

        // Step 1: Receive salt
        var saltBytes = await ReadFrameAsync(stream, ct);
        var salt = Encoding.UTF8.GetString(saltBytes);

        // Step 2: Derive key
        var key = PairingProtocol.DeriveKey(pairingCode, salt);

        // Step 3: Receive remote bundle
        var remoteNonce = await ReadFrameAsync(stream, ct);
        var remoteCiphertext = await ReadFrameAsync(stream, ct);

        // Step 4: Encrypt and send local bundle
        var localJson = Encoding.UTF8.GetBytes(localBundle.ToJson());
        var (ciphertext, nonce) = PairingProtocol.Encrypt(key, localJson);
        await WriteFrameAsync(stream, nonce, ct);
        await WriteFrameAsync(stream, ciphertext, ct);

        try
        {
            var remotePlaintext = PairingProtocol.Decrypt(key, remoteCiphertext, remoteNonce);
            var remoteBundle = KeyBundle.FromJson(Encoding.UTF8.GetString(remotePlaintext));
            return remoteBundle;
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteFrameAsync(NetworkStream stream, byte[] data, CancellationToken ct)
    {
        var lengthPrefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, data.Length);
        await stream.WriteAsync(lengthPrefix, ct);
        await stream.WriteAsync(data, ct);
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        var lengthPrefix = new byte[4];
        await stream.ReadExactlyAsync(lengthPrefix, ct);
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthPrefix);

        if (length < 0 || length > MaxFrameSize)
            throw new InvalidOperationException($"Frame size {length} exceeds maximum of {MaxFrameSize} bytes.");

        var data = new byte[length];
        await stream.ReadExactlyAsync(data, ct);
        return data;
    }
}
