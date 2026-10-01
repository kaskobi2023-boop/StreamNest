using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChzzkDownloader.Services;
using Xunit.Abstractions;
using static ChzzkDownloader.IntegrationTests.BrowserCompatibilityIntegrationTests;

namespace ChzzkDownloader.IntegrationTests;

[Collection(WebView2IntegrationCollection.Name)]
public sealed class BrowserCaptureIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DynamicPlayer_AutomaticFallback_CapturesExtensionlessAesHls_AndDownloadsBothQualities()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        var root = Path.Combine(Path.GetTempPath(), "StreamNest-BrowserCapture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var assets = await PackedHlsIntegrationTests.MakeAssetsAsync(root, token);
            var key = RandomNumberGenerator.GetBytes(16);
            var iv = new byte[16];
            foreach (var name in assets.Keys.ToArray())
            {
                if (name.EndsWith(".bin"))
                {
                    using var aes = Aes.Create();
                    aes.Key = key;
                    assets[name] = aes.EncryptCbc(assets[name], iv);
                }
                else if (name.EndsWith("index.m3u8"))
                    assets[name] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(assets[name]).Replace("#EXTM3U",
                        "#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"../key\",IV=0x00000000000000000000000000000000"));
            }
            assets["/hls/key"] = key;
            assets["/hls/manifest"] = assets["/hls/master.m3u8"];
            assets.Remove("/hls/master.m3u8");
            await using var server = new PlayerServer(assets);
            Task<Uri> Validate(string url, CancellationToken _) => new Uri(url).Authority == new Uri(server.Url).Authority
                ? Task.FromResult(new Uri(url)) : throw new ArgumentException("Only this fixture server is allowed");
            var service = new YtDlpService(new CookieFileService(Path.Combine(root, "cookies")), Path.Combine(root, "parts"), Validate);
            await Assert.ThrowsAsync<YtDlpException>(() => service.AnalyzeWebAsync(server.Url, token));
            Assert.Equal(0, server.PlayerStarts);
            service.BrowserCapture = (url, ct) => StaThreadRunner.RunAsync(() => new WebBrowserCaptureService(Validate).CaptureAsync(url, ct));
            var item = Assert.Single(await service.AnalyzeWebAsync(server.Url, token));
            Assert.True(item.Snapshot!.BrowserCaptured);
            Assert.Equal(4, item.Video.DurationSeconds);
            Assert.Equal(new int?[] { 180, 90 }, item.Video.Formats.Select(f => f.Height));
            Assert.True(server.PlayerStarts > 0);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "parts", "BrowserAnalysis")));
            foreach (var format in item.Video.Formats)
            {
                var path = await service.DownloadAsync(server.Url, Path.Combine(root, "output"), item.SelectFormat(format),
                    null, null, null, token);
                Assert.NotNull(path);
                var probe = await RunAsync(ToolLocator.FfprobePath,
                    ["-v", "error", "-show_entries", "stream=height,codec_type", "-of", "json", path], token);
                Assert.Equal(0, probe.ExitCode);
                using var json = JsonDocument.Parse(probe.Output);
                var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                Assert.Contains(streams, s => s.GetProperty("codec_type").GetString() == "audio");
                Assert.Contains(streams, s => s.TryGetProperty("height", out var h) && h.GetInt32() == format.Height);
                var decode = await RunAsync(ToolLocator.FfmpegPath, ["-v", "error", "-xerror", "-i", path, "-f", "null", "-"], token);
                Assert.True(decode.ExitCode == 0, decode.Error);
                output.WriteLine($"Dynamic browser capture → AES-128 HLS → {format.Height}p MP4: {new FileInfo(path).Length} bytes, audio + decode passed.");
            }
            Assert.True(server.KeyRequests >= 2);
            Assert.Equal(0, server.CookieRequests);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task BrowserCapture_CancellationStopsWaitingForPlayer()
    {
        await using var server = new PlayerServer(new Dictionary<string, byte[]>());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StaThreadRunner.RunAsync(() =>
            new WebBrowserCaptureService((url, _) => Task.FromResult(new Uri(url))).CaptureAsync(server.Url, timeout.Token)));
    }

    private sealed class PlayerServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        private readonly List<Task> _clients = [];
        public string Url { get; }
        public int PlayerStarts, KeyRequests, CookieRequests;
        internal PlayerServer(Dictionary<string, byte[]> assets)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/watch";
            assets["/watch"] = Encoding.UTF8.GetBytes("""
                <html><title>Generated dynamic video</title><body>
                <button aria-label="Play" onclick="fetch('/start').then(r=>r.json()).then(x=>fetch(x.url))">Play</button>
                </body></html>
                """);
            assets["/start"] = Encoding.UTF8.GetBytes("{\"url\":\"/hls/manifest?token=fixture-secret\"}");
            _serve = ServeAsync(assets);
        }
        private async Task ServeAsync(Dictionary<string, byte[]> assets)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _clients.Add(HandleAsync(client, assets));
                }
            }
            catch (OperationCanceledException) { }
        }
        private async Task HandleAsync(TcpClient client, Dictionary<string, byte[]> assets)
        {
            using (client)
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var line = await reader.ReadLineAsync(_stop.Token);
                if (line is null) return;
                var path = new Uri(new Uri(Url), line.Split(' ')[1]).AbsolutePath;
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } header)
                    if (header.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref CookieRequests);
                if (path == "/start") Interlocked.Increment(ref PlayerStarts);
                if (path == "/hls/key") Interlocked.Increment(ref KeyRequests);
                var found = assets.TryGetValue(path, out var bytes);
                bytes ??= [];
                var type = path == "/watch" ? "text/html" : path == "/start" ? "application/json" : "text/plain";
                var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, _stop.Token);
                await stream.WriteAsync(bytes, _stop.Token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException) { }
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await _serve;
            await Task.WhenAll(_clients);
            _stop.Dispose();
        }
    }
}
