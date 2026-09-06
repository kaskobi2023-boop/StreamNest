using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ChzzkDownloader.IntegrationTests;

internal sealed class LoopbackThumbnailServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _serveTask;

    public LoopbackThumbnailServer(byte[] body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Uri = new Uri($"http://127.0.0.1:{port}/thumbnail.png");
        _serveTask = ServeOnceAsync(body, statusCode, _cancellation.Token);
    }

    public Uri Uri { get; }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        _listener.Stop();
        try
        {
            await _serveTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException) when (_cancellation.IsCancellationRequested)
        {
        }
        _cancellation.Dispose();
    }

    private async Task ServeOnceAsync(byte[] body, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        var requestBuffer = new byte[4096];
        _ = await stream.ReadAsync(requestBuffer, cancellationToken);

        var reason = statusCode == HttpStatusCode.OK ? "OK" : "Not Found";
        var responseHeader = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(int)statusCode} {reason}\r\n" +
            "Content-Type: image/png\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(responseHeader, cancellationToken);
        if (body.Length > 0)
            await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
