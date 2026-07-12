using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PersonalServer.Tests;

/// <summary>
/// Minimal localhost HTTP/1.1 stub standing in for STARfolio's loopback API. Uses a raw
/// TcpListener rather than HttpListener so it needs no urlacl reservation / admin on Windows.
/// Routes by request path to a caller-supplied JSON body.
/// </summary>
internal sealed class StubLoopback : IDisposable
{
    readonly TcpListener _listener;
    readonly CancellationTokenSource _cts = new();
    readonly Func<string, string> _respond;

    public int Port { get; }

    public StubLoopback(Func<string, string> respondByPath)
    {
        _respond = respondByPath;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoopAsync();
    }

    async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { break; }
            _ = Task.Run(() => Handle(client));
        }
    }

    void Handle(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            var buffer = new byte[8192];
            var received = new List<byte>();
            var contentLength = -1;
            var headerEnd = -1;

            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) return;
                received.AddRange(buffer.AsSpan(0, read).ToArray());

                var text = Encoding.ASCII.GetString(received.ToArray());
                if (headerEnd < 0)
                {
                    headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (headerEnd >= 0)
                        contentLength = ParseContentLength(text[..headerEnd]);
                }
                if (headerEnd >= 0 && received.Count - (headerEnd + 4) >= contentLength)
                {
                    var path = text.Split("\r\n")[0].Split(' ')[1];
                    Write(stream, _respond(path));
                    return;
                }
            }
        }
    }

    static int ParseContentLength(string headers)
    {
        foreach (var line in headers.Split("\r\n"))
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(line["Content-Length:".Length..].Trim(), out var n) ? n : 0;
        return 0;
    }

    static void Write(NetworkStream stream, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        stream.Write(head);
        stream.Write(payload);
        stream.Flush();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}
