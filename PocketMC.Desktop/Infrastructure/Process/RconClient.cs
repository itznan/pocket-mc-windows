using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Infrastructure.Process;

public class RconClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _password;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _authenticated;

    public RconClient(string host, int port, string password)
    {
        _host = host;
        _port = port;
        _password = password;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(_host, _port, ct);
        _stream = _client.GetStream();

        // Authenticate
        var result = await SendPacketAsync(3, _password, ct);
        if (result.Id == -1)
        {
            throw new UnauthorizedAccessException("RCON authentication failed.");
        }
        _authenticated = true;
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        if (!_authenticated) throw new InvalidOperationException("Not authenticated.");
        var result = await SendPacketAsync(2, command, ct);
        return result.Body;
    }

    private async Task<RconPacket> SendPacketAsync(int type, string body, CancellationToken ct)
    {
        if (_stream == null) throw new InvalidOperationException("Not connected.");

        int id = new Random().Next();
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        int length = 4 + 4 + bodyBytes.Length + 2;

        byte[] packet = new byte[length + 4];
        BitConverter.GetBytes(length).CopyTo(packet, 0);
        BitConverter.GetBytes(id).CopyTo(packet, 4);
        BitConverter.GetBytes(type).CopyTo(packet, 8);
        bodyBytes.CopyTo(packet, 12);
        packet[packet.Length - 2] = 0;
        packet[packet.Length - 1] = 0;

        await _stream.WriteAsync(packet, 0, packet.Length, ct);

        // Read response
        byte[] lenBuf = new byte[4];
        await ReadExactAsync(lenBuf, 4, ct);
        int respLen = BitConverter.ToInt32(lenBuf, 0);

        byte[] respData = new byte[respLen];
        await ReadExactAsync(respData, respLen, ct);

        int respId = BitConverter.ToInt32(respData, 0);
        int respType = BitConverter.ToInt32(respData, 4);
        string respBody = Encoding.UTF8.GetString(respData, 8, respLen - 10);

        return new RconPacket(respId, respType, respBody);
    }

    private async Task ReadExactAsync(byte[] buffer, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await _stream!.ReadAsync(buffer, totalRead, count - totalRead, ct);
            if (read == 0) throw new IOException("End of stream reached.");
            totalRead += read;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
    }

    private record RconPacket(int Id, int Type, string Body);
}
