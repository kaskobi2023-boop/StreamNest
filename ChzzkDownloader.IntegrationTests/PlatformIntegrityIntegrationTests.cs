using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChzzkDownloader.Models;
using ChzzkDownloader.Services;
using static ChzzkDownloader.IntegrationTests.BrowserCompatibilityIntegrationTests;

namespace ChzzkDownloader.IntegrationTests;

public sealed class PlatformIntegrityIntegrationTests
{
    [Fact]
    public async Task HigherQualityNeverReusesOrOverwritesAnExistingLowerQuality()
    {
        await WithFixture(async (server, service, root, token) =>
        {
            var output = Path.Combine(root, "downloads");
            var low = await service.DownloadAsync(server.Url, output, Quality(90), null, null, null, token);
            var oldHash = SHA256.HashData(await File.ReadAllBytesAsync(low!, token));
            var high = await service.DownloadAsync(server.Url, output, Quality(180), null, null, null, token);
            Assert.NotEqual(low, high);
            Assert.Contains("[90p]", low);
            Assert.Contains("[180p]", high);
            Assert.Equal(oldHash, SHA256.HashData(await File.ReadAllBytesAsync(low!, token)));
            Assert.Equal(90, await Height(low!, token));
            Assert.Equal(180, await Height(high!, token));
            Assert.Equal(2, Directory.GetFiles(output, "*.mp4").Length);
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public async Task IncompletePlatformMediaNeverReachesFinalFolderOrCompletion(string fault)
    {
        await WithFixture(async (server, service, root, token) =>
        {
            server.Fault = fault;
            var output = Path.Combine(root, "downloads");
            var events = new ConcurrentQueue<DownloadProgressInfo>();
            await Assert.ThrowsAsync<YtDlpException>(() => service.DownloadAsync(server.Url, output,
                Quality(180), null, new Recorder(events.Enqueue), null, token));
            Assert.DoesNotContain(events, e => e.Message == "완료");
            Assert.Empty(Directory.GetFiles(output, "*.mp4"));
            Assert.True(server.FaultRequests > 0);
        });
    }

    [Theory]
    [InlineData(180, 4)]
    [InlineData(90, 9)]
    public async Task PlatformCompletionChecksAnalyzedHeightAndDuration(int expectedHeight, double expectedDuration)
    {
        await WithFixture(async (server, service, root, token) =>
        {
            // Model a changed rendition after analysis: engine returns 90p/4s,
            // but the original selection expects a different height or length.
            var option = new VideoFormatOption { Height = expectedHeight, ExpectedDurationSeconds = expectedDuration,
                ExpectsAudio = true, Selector = Quality(90).Selector };
            await Assert.ThrowsAsync<YtDlpException>(() => service.DownloadAsync(server.Url,
                Path.Combine(root, "downloads"), option, null, null, null, token));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "downloads"), "*.mp4"));
        });
    }

    private static VideoFormatOption Quality(int height) => new() { Height = height, ExpectedDurationSeconds = 4,
        ExpectsAudio = true, Selector = $"bestvideo[height<={height}]+bestaudio/best[height<={height}]/best" };

    private static async Task<int> Height(string path, CancellationToken token)
    {
        var probe = await RunAsync(ToolLocator.FfprobePath,
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=height", "-of", "json", path], token);
        Assert.Equal(0, probe.ExitCode);
        using var json = JsonDocument.Parse(probe.Output);
        return json.RootElement.GetProperty("streams")[0].GetProperty("height").GetInt32();
    }

    private static async Task WithFixture(Func<FixtureServer, YtDlpService, string, CancellationToken, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "StreamNest-PlatformIntegrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            var assets = await PackedHlsIntegrationTests.MakeAssetsAsync(root, timeout.Token);
            await using var server = new FixtureServer(assets);
            // Exercise the common platform download branch on generated media,
            // not platform extraction or login. No production URL policy changes.
            var service = new YtDlpService(new CookieFileService(Path.Combine(root, "cookies")), Path.Combine(root, "parts"));
            await action(server, service, root, timeout.Token);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Recorder(Action<DownloadProgressInfo> report) : IProgress<DownloadProgressInfo>
    {
        public void Report(DownloadProgressInfo value) => report(value);
    }

    private sealed class FixtureServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;
        private int _faultRequests;
        public string Url { get; }
        public string? Fault { get; set; }
        public int FaultRequests => Volatile.Read(ref _faultRequests);

        public FixtureServer(Dictionary<string, byte[]> assets)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/hls/master.m3u8";
            _serving = Serve(assets, _stop.Token);
        }

        private async Task Serve(Dictionary<string, byte[]> assets, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(token);
                await using var stream = client.GetStream();
                try
                {
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var first = await reader.ReadLineAsync(token) ?? "";
                    while (await reader.ReadLineAsync(token) is { Length: > 0 }) { }
                    var path = first.Split(' ').ElementAtOrDefault(1)?.Split('?')[0] ?? "";
                    var exists = assets.TryGetValue(path, out var body);
                    if (Fault is not null && path.EndsWith("chunk001.bin"))
                    {
                        Interlocked.Increment(ref _faultRequests);
                        if (Fault == "missing") exists = false;
                        else body = Encoding.UTF8.GetBytes("<html>Upstream error page instead of a media fragment.</html>");
                    }
                    if (!exists) body = Encoding.ASCII.GetBytes("not found");
                    var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(exists ? "200 OK" : "404 Not Found")}\r\nContent-Length: {body!.Length}\r\nContent-Type: {(path.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : "application/octet-stream")}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, token);
                    if (!first.StartsWith("HEAD ")) await stream.WriteAsync(body, token);
                }
                catch (IOException) when (!token.IsCancellationRequested) { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync(); _listener.Stop();
            try { await _serving; }
            catch (OperationCanceledException) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
            _stop.Dispose();
        }
    }
}
