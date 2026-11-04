using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DeepSeekPet;

/// <summary>
/// 一个极简的本地 HTTP 服务，让外部 agent（比如 DSH 里的我）可以驱动这只桌面鱼。
/// 只用原始 TcpListener，避免 HttpListener 的 URL ACL 权限问题。
/// </summary>
internal sealed class AgentBridge : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<string, string, string, string> _handler;
    private readonly Action<string> _log;
    private readonly Thread _thread;
    private volatile bool _running = true;

    public int Port { get; }

    public AgentBridge(int port, Func<string, string, string, string> handler, Action<string> log)
    {
        _handler = handler;
        _log = log;

        TcpListener? listener = null;
        int chosen = port;
        for (int i = 0; i < 12 && listener is null; i++)
        {
            try
            {
                var candidate = new TcpListener(IPAddress.Loopback, chosen);
                candidate.Start();
                listener = candidate;
            }
            catch
            {
                chosen++;
            }
        }

        _listener = listener ?? throw new IOException("no free local port for pet bridge");
        Port = chosen;

        _thread = new Thread(Loop) { IsBackground = true, Name = "pet-bridge" };
        _thread.Start();
    }

    private void Loop()
    {
        while (_running)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch
            {
                break;
            }

            try
            {
                Handle(client);
            }
            catch (Exception ex)
            {
                _log("handler: " + ex.Message);
            }
            finally
            {
                try { client.Close(); } catch { }
            }
        }
    }

    private void Handle(TcpClient client)
    {
        client.ReceiveTimeout = 5000;
        using NetworkStream stream = client.GetStream();

        var head = new List<byte>(512);
        var one = new byte[1];
        while (head.Count < 65536)
        {
            int n = stream.Read(one, 0, 1);
            if (n == 0) return;
            head.Add(one[0]);
            int c = head.Count;
            if (c >= 4 && head[c - 1] == (byte)'\n' && head[c - 2] == (byte)'\r' && head[c - 3] == (byte)'\n' && head[c - 4] == (byte)'\r')
                break;
        }

        string headText = Encoding.UTF8.GetString(head.ToArray());
        string[] lines = headText.Split("\r\n");
        string[] requestLine = lines.Length > 0 ? lines[0].Split(' ') : Array.Empty<string>();
        string method = requestLine.Length > 0 ? requestLine[0] : "GET";
        string path = requestLine.Length > 1 ? requestLine[1] : "/";

        int contentLength = 0;
        foreach (string line in lines)
        {
            int idx = line.IndexOf(':');
            if (idx <= 0) continue;
            if (line[..idx].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                int.TryParse(line[(idx + 1)..].Trim(), out contentLength);
        }

        string body = string.Empty;
        if (contentLength > 0 && contentLength < 1_048_576)
        {
            var data = new byte[contentLength];
            int read = 0;
            while (read < contentLength)
            {
                int n = stream.Read(data, read, contentLength - read);
                if (n <= 0) break;
                read += n;
            }
            body = Encoding.UTF8.GetString(data, 0, read);
        }

        string response = _handler(method, path, body) ?? "{}";
        byte[] payload = Encoding.UTF8.GetBytes(response);
        string header =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Access-Control-Allow-Headers: *\r\n" +
            "Connection: close\r\n\r\n";

        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    public void Dispose()
    {
        _running = false;
        try { _listener.Stop(); } catch { }
    }
}
