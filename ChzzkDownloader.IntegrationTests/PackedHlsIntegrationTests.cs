using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChzzkDownloader.Models;
using ChzzkDownloader.Services;
using Xunit.Abstractions;
using static ChzzkDownloader.IntegrationTests.BrowserCompatibilityIntegrationTests;

namespace ChzzkDownloader.IntegrationTests;

public sealed class PackedHlsIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PackedPage_AnalyzesAndDownloadsBothHlsQualities_WithoutExecutingScript()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        var root = Path.Combine(Path.GetTempPath(), $"StreamNest-PackedHls-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var assets = await MakeAssetsAsync(root, token);
            await using var server = new PackedVideoServer(assets);
            var cookieService = new CookieFileService(Path.Combine(root, "cookies"));
            // The production entry-point still refuses local/private addresses.
            await Assert.ThrowsAsync<ArgumentException>(() => new YtDlpService(cookieService)
                .AnalyzeWebAsync(server.Url, token));
            var service = new YtDlpService(cookieService, Path.Combine(root, "parts"), (url, _) =>
                url == server.Url ? Task.FromResult(new Uri(url)) : throw new ArgumentException("Not this test server"));

            var beforeArgs = YtDlpService.BuildWebAnalyzeArguments(server.Url);
            beforeArgs.Remove("--ignore-errors");
            beforeArgs[beforeArgs.IndexOf("--extractor-args") + 1] = "generic:impersonate;streamnest_packed=false";
            var before = await RunAsync(ToolLocator.YtDlpPath, beforeArgs, token);
            Assert.NotEqual(0, before.ExitCode);
            Assert.Contains("Unsupported URL", before.Error);

            using var http = new HttpClient();
            using var withoutHeaders = await http.GetAsync(new Uri(new Uri(server.Url), "/hls/master.m3u8"), token);
            Assert.Equal(HttpStatusCode.Forbidden, withoutHeaders.StatusCode);
            Assert.Equal(1, server.RejectedMediaRequests);

            var item = Assert.Single(await service.AnalyzeWebAsync(server.Url, token));
            Assert.Equal(new int?[] { 180, 90 }, item.Video.Formats.Select(format => format.Height));
            Assert.Equal(4, item.Video.DurationSeconds);
            Assert.Contains("-packed-", item.Video.Id);
            Assert.Equal(0, server.ScriptExecutions);

            foreach (var format in item.Video.Formats)
            {
                var logs = new ConcurrentQueue<string>();
                var path = await service.DownloadAsync(server.Url, Path.Combine(root, $"영상-{format.Height}"),
                    item.SelectFormat(format),
                    [new BrowserCookie("platform-session", "fixture-only", "127.0.0.1", "/", true, true, 0)],
                    null, logs.Enqueue, token);
                Assert.NotNull(path);
                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
                // --print after_move makes yt-dlp quiet; identify the fallback by
                // its stable video ID and verify the resulting file, not chatter.
                Assert.Contains(item.Video.Id, Path.GetFileName(path));
                var probe = await RunAsync(ToolLocator.FfprobePath,
                    ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=height", "-of", "json", path], token);
                Assert.True(probe.ExitCode == 0, probe.Error);
                using var json = JsonDocument.Parse(probe.Output);
                Assert.Equal(format.Height, json.RootElement.GetProperty("streams")[0].GetProperty("height").GetInt32());
                var decode = await RunAsync(ToolLocator.FfmpegPath,
                    ["-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"], token);
                Assert.True(decode.ExitCode == 0, decode.Error);
                output.WriteLine($"Packed HLS {format.Height}p: {new FileInfo(path).Length} bytes, full decode passed.");
            }
            Assert.Equal(0, server.ScriptExecutions);
            Assert.Equal(0, server.CookieRequests);
            Assert.Equal(1, server.RejectedMediaRequests); // Only the deliberate negative control failed.
            Assert.True(server.SegmentRequests >= 8);
            output.WriteLine("Baseline Unsupported URL reproduced; parser fallback, header propagation, both qualities, .bin MPEG-TS segments and no-script-execution checks passed.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static async Task<Dictionary<string, byte[]>> MakeAssetsAsync(string root, CancellationToken token)
    {
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, size) in new[] { ("low", "160x90"), ("high", "320x180") })
        {
            var directory = Path.Combine(root, "hls", name);
            Directory.CreateDirectory(directory);
            var generated = await RunAsync(ToolLocator.FfmpegPath,
                ["-v", "error", "-f", "lavfi", "-i", $"testsrc2=size={size}:rate=10",
                 "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "4",
                 "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-g", "10", "-sc_threshold", "0",
                 "-c:a", "aac", "-b:a", "32k", "-f", "hls", "-hls_time", "1", "-hls_playlist_type", "vod",
                 "-hls_segment_filename", Path.Combine(directory, "chunk%03d.bin"), Path.Combine(directory, "index.m3u8")], token);
            Assert.True(generated.ExitCode == 0, generated.Error);
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var bytes = await File.ReadAllBytesAsync(file, token);
                if (file.EndsWith(".bin", StringComparison.Ordinal)) Assert.Equal(0x47, bytes[0]);
                assets[$"/hls/{name}/{Path.GetFileName(file)}"] = bytes;
            }
        }
        assets["/hls/master.m3u8"] = Encoding.UTF8.GetBytes("""
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=150000,RESOLUTION=160x90,CODECS="avc1.42c01e,mp4a.40.2"
            low/index.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=400000,RESOLUTION=320x180,CODECS="avc1.42c01e,mp4a.40.2"
            high/index.m3u8

            """);
        return assets;
    }

    private sealed class PackedVideoServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        private int _rejected, _executed, _segments, _cookies;
        public string Url { get; }
        public int RejectedMediaRequests => Volatile.Read(ref _rejected);
        public int ScriptExecutions => Volatile.Read(ref _executed);
        public int SegmentRequests => Volatile.Read(ref _segments);
        public int CookieRequests => Volatile.Read(ref _cookies);

        public PackedVideoServer(Dictionary<string, byte[]> assets)
        {
            _listener.Start();
            var origin = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            Url = origin + "/packed?fixture=1";
            var source = "globalThis.fixtureSource=\"/hls/master.m3u8\";fetch(\"/script-executed\");";
            var symbols = Regex.Matches(source, @"\b\w+\b").Select(match => match.Value).Distinct().ToList();
            Assert.True(symbols.Count < 36);
            const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
            var packed = Regex.Replace(source, @"\b\w+\b", match => alphabet[symbols.IndexOf(match.Value)].ToString());
            var script = "eval(function(p,a,c,k,e,d){e=function(c){return c.toString(a)};while(c--)if(k[c])p=p.replace(new RegExp('\\\\b'+e(c)+'\\\\b','g'),k[c]);return p;}"
                + "(" + JsonSerializer.Serialize(packed) + ",36," + symbols.Count + "," + JsonSerializer.Serialize(string.Join('|', symbols)) + ".split('|'),0,{}))";
            assets["/packed"] = Encoding.UTF8.GetBytes($"<html><title>StreamNest generated HLS demo</title><body><h1>Generated test video</h1><script>{script}</script></body></html>");
            _serve = ServeAsync(assets, origin, _stop.Token);
        }

        private async Task ServeAsync(Dictionary<string, byte[]> assets, string origin, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(token);
                await using var stream = client.GetStream();
                try
                {
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var first = await reader.ReadLineAsync(token) ?? "";
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    while (await reader.ReadLineAsync(token) is { Length: > 0 } line)
                    {
                        var colon = line.IndexOf(':');
                        if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim();
                    }
                    var path = first.Split(' ').ElementAtOrDefault(1)?.Split('?')[0] ?? "";
                    if (headers.ContainsKey("Cookie")) Interlocked.Increment(ref _cookies);
                    if (path == "/script-executed") Interlocked.Increment(ref _executed);
                    var exists = assets.TryGetValue(path, out var body);
                    var allowed = !path.StartsWith("/hls/", StringComparison.Ordinal) ||
                        headers.GetValueOrDefault("Referer") == Url && headers.GetValueOrDefault("Origin") == origin;
                    if (!allowed) Interlocked.Increment(ref _rejected);
                    if (allowed && path.EndsWith(".bin", StringComparison.Ordinal)) Interlocked.Increment(ref _segments);
                    var status = !allowed ? "403 Forbidden" : exists ? "200 OK" : "404 Not Found";
                    body = allowed && exists ? body! : Encoding.UTF8.GetBytes(status);
                    var contentType = path.EndsWith(".m3u8", StringComparison.Ordinal) ? "application/vnd.apple.mpegurl" :
                        path.EndsWith(".bin", StringComparison.Ordinal) ? "application/octet-stream" : "text/html; charset=utf-8";
                    var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response, token);
                    if (!first.StartsWith("HEAD ", StringComparison.Ordinal)) await stream.WriteAsync(body, token);
                }
                catch (IOException) when (!token.IsCancellationRequested) { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try { await _serve; }
            catch (OperationCanceledException) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
            _stop.Dispose();
        }
    }
}
