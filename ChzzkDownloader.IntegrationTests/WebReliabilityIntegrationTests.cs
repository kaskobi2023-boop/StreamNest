using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ChzzkDownloader.Models;
using ChzzkDownloader.Services;
using Xunit.Abstractions;
using static ChzzkDownloader.IntegrationTests.BrowserCompatibilityIntegrationTests;

namespace ChzzkDownloader.IntegrationTests;

public sealed class WebReliabilityIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CancelAfterRefreshReordersEntries_CleansTheActualRetryDirectory()
    {
        await WithFixture(async (server, service, root, token) =>
        {
            server.ReorderOnRefresh = true;
            var items = await service.AnalyzeWebAsync(server.Url, token);
            Assert.Equal(2, items.Count);
            var selected = items[1];
            Assert.Equal(2, selected.PlaylistIndex);
            var initial = selected.SelectFormat(selected.Video.Formats[0]);
            server.Expire(false);
            server.PauseRefreshedSegments = true;
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
            var target = Path.Combine(root, "cancelled-video");
            var downloading = service.DownloadAsync(server.Url, target, initial, null, null, null, cancelled.Token);
            await server.RefreshedSegmentStarted.Task.WaitAsync(token);
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloading);
            Assert.Equal(2, server.PageRequests);
            var directories = Directory.GetDirectories(Path.Combine(root, "parts"));
            Assert.Single(directories); // Original request and retry share one job.
            Assert.NotEmpty(Directory.GetFiles(directories[0], "*", SearchOption.AllDirectories));
            var cleanup = await service.DeletePartialFilesAsync(server.Url, target, initial);
            Assert.True(cleanup.Succeeded, cleanup.Error);
            Assert.Empty(Directory.GetDirectories(Path.Combine(root, "parts")));
            Assert.Empty(Directory.GetFiles(target, "*.mp4"));
        });
    }

    [Fact]
    public async Task BrowserTransport_ReachesMasterChildPlaylistsAndCachedDownloadFragments()
    {
        // Controlled browser-header gate, not a CAPTCHA or real CDN challenge.
        await WithFixture(async (server, service, root, token) =>
        {
            server.RequireBrowserForMedia = true;
            var oldArgs = YtDlpService.BuildWebAnalyzeArguments(server.Url);
            oldArgs.RemoveRange(oldArgs.IndexOf("--impersonate"), 2);
            oldArgs.Remove("--ignore-errors");
            var before = await RunAsync(ToolLocator.YtDlpPath, oldArgs, token);
            Assert.NotEqual(0, before.ExitCode);
            Assert.Contains("HTTP Error 403", before.Error);
            Assert.Contains("[StreamNest:HLS_ACCESS_DENIED]", before.Error);
            Assert.DoesNotContain("Unsupported URL", before.Error);
            var deniedBefore = server.BrowserMediaDenied;
            Assert.True(deniedBefore > 0);

            var item = Assert.Single(await service.AnalyzeWebAsync(server.Url, token));
            var pageRequests = server.PageRequests;
            var path = await service.DownloadAsync(server.Url, Path.Combine(root, "browser-video"),
                item.SelectFormat(item.Video.Formats[0]), null, null, null, token);
            Assert.True(File.Exists(path)); // Includes runtime full-decode verification.
            Assert.Equal(pageRequests, server.PageRequests); // Snapshot, no re-extraction.
            Assert.Equal(deniedBefore, server.BrowserMediaDenied);
            Assert.True(server.BrowserMasterRequests > 0);
            Assert.True(server.BrowserChildRequests > 0);
            Assert.True(server.BrowserSegmentRequests >= 4);
            output.WriteLine($"Old page-only transport: 403. Fixed transport: master={server.BrowserMasterRequests}, " +
                $"child={server.BrowserChildRequests}, segments={server.BrowserSegmentRequests}; full decode passed.");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermanentManifest403_IsReportedAsAccessDenied_NotUnsupportedUrl(bool includePagePath)
    {
        await WithFixture(async (server, service, root, token) =>
        {
            server.DenyManifests = true;
            var error = await Assert.ThrowsAsync<YtDlpException>(() =>
                service.AnalyzeWebAsync(server.Url, token, includePagePath));
            Assert.Contains("영상 주소는 찾았지만", error.Message);
            Assert.Contains("[StreamNest:HLS_ACCESS_DENIED]", error.Message);
            Assert.Contains("HTTP Error 403", error.Message);
            Assert.DoesNotContain("Unsupported URL", error.Message);
            Assert.DoesNotContain("token=", error.Message);
            Assert.Equal(1, server.PageRequests);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SavedStream_ReusesMetadata_AndRefreshesOnlyTheSameVideo(bool expire, bool replace)
    {
        await WithFixture(async (server, service, root, token) =>
        {
            var item = Assert.Single(await service.AnalyzeWebAsync(server.Url, token));
            Assert.Equal(1, server.PageRequests);
            if (expire) server.Expire(replace);
            var logs = new ConcurrentQueue<string>();
            var task = service.DownloadAsync(server.Url, Path.Combine(root, "영상"),
                item.SelectFormat(item.Video.Formats[0]), null, null, logs.Enqueue, token);
            if (replace)
            {
                var error = await Assert.ThrowsAsync<YtDlpException>(() => task);
                Assert.Contains("같은 영상을 확인하지 못했습니다", error.Message);
                Assert.Empty(Directory.GetFiles(Path.Combine(root, "영상"), "*.mp4"));
            }
            else
            {
                var path = await task;
                Assert.True(File.Exists(path));
                Assert.Contains(logs, log => log.StartsWith("파일 검증 통과"));
            }
            Assert.Equal(expire ? 2 : 1, server.PageRequests);
            Assert.DoesNotContain(logs, log => log.Contains("token=1") || log.Contains("token=2"));
            Assert.Empty(Directory.GetFiles(root, "request-*.json", SearchOption.AllDirectories));
            output.WriteLine($"expire={expire}, replace={replace}: pages={server.PageRequests}; reuse/identity/cleanup verified.");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrailerDoesNotHideMainVideo_IndividualQualitiesAreGrouped(bool includeMaster)
    {
        await WithFixture(async (server, service, root, token) =>
        {
            server.SeparateQualities = true;
            server.IncludeMaster = includeMaster;
            server.Trailer = true;
            server.BrokenCandidate = true;
            var items = await service.AnalyzeWebAsync(server.Url, token);
            Assert.Equal(2, items.Count);
            var main = Assert.Single(items, item => item.Video.Id.StartsWith("web-packed-"));
            Assert.Equal(new int?[] { 180, 90 }, main.Video.Formats.Select(format => format.Height));
            foreach (var quality in main.Video.Formats)
            {
                var path = await service.DownloadAsync(server.Url, Path.Combine(root, $"화질-{quality.Height}"),
                    main.SelectFormat(quality), null, null, null, token);
                Assert.True(File.Exists(path)); // Runtime verifier also checks exact height/full decode.
            }
            Assert.Equal(1, server.PageRequests);
            Assert.Equal(0, server.ScriptExecutions);
            output.WriteLine($"trailer + separate variants (master={includeMaster}): 2 videos, 2 main qualities, both downloaded and verified.");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptSegment_NeverReportsCompletion(bool corruptInsteadOfMissing)
    {
        await WithFixture(async (server, service, root, token) =>
        {
            var item = Assert.Single(await service.AnalyzeWebAsync(server.Url, token));
            server.Fault = corruptInsteadOfMissing ? "corrupt" : "missing";
            var events = new ConcurrentQueue<DownloadProgressInfo>();
            await Assert.ThrowsAsync<YtDlpException>(() => service.DownloadAsync(server.Url,
                Path.Combine(root, "영상"), item.SelectFormat(item.Video.Formats[0]), null,
                new ImmediateProgress(events.Enqueue), null, token));
            Assert.DoesNotContain(events, item => item.Message == "완료");
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "영상"), "*.mp4"));
            Assert.InRange(server.PageRequests, 1, 2);
            Assert.Empty(Directory.GetFiles(root, "request-*.json", SearchOption.AllDirectories));
            output.WriteLine($"fault={server.Fault}: failed, no completion event, no credential snapshot left.");
        });
    }

    [Fact]
    public async Task RuntimeVerifierRejectsTruncatedMedia_AndHonorsCancellation()
    {
        await WithFixture(async (server, service, root, token) =>
        {
            var source = Path.Combine(root, "hls", "high", "chunk000.bin");
            await Assert.ThrowsAsync<YtDlpException>(() => MediaVerificationService.VerifyAsync(source, 4, 180, true, token));
            using var stopped = new CancellationTokenSource();
            stopped.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                MediaVerificationService.VerifyAsync(source, null, null, false, stopped.Token));
        });
    }

    private static async Task WithFixture(Func<FixtureServer, YtDlpService, string, CancellationToken, Task> action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var root = Path.Combine(Path.GetTempPath(), "StreamNest-Reliability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var assets = await PackedHlsIntegrationTests.MakeAssetsAsync(root, timeout.Token);
            // Alias explicit quality paths; each alias retains its own segment directory.
            foreach (var pair in assets.ToArray())
                if (pair.Key.Contains("/high/") || pair.Key.Contains("/low/"))
                    assets[pair.Key.Replace("/high/", "/180p/").Replace("/low/", "/90p/")] = pair.Value;
            var preview = Path.Combine(root, "preview.mp4");
            var generated = await RunAsync(ToolLocator.FfmpegPath,
                ["-v", "error", "-i", Path.Combine(root, "hls", "low", "chunk000.bin"), "-c", "copy", preview], timeout.Token);
            Assert.Equal(0, generated.ExitCode);
            assets["/preview.mp4"] = await File.ReadAllBytesAsync(preview, timeout.Token);
            await using var server = new FixtureServer(assets);
            var service = new YtDlpService(new CookieFileService(Path.Combine(root, "cookies")), Path.Combine(root, "parts"),
                (url, _) => url == server.Url ? Task.FromResult(new Uri(url)) : throw new ArgumentException("Not this fixture"));
            await action(server, service, root, timeout.Token);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class ImmediateProgress(Action<DownloadProgressInfo> report) : IProgress<DownloadProgressInfo>
    {
        public void Report(DownloadProgressInfo value) => report(value);
    }

    private sealed class FixtureServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        private int _pageRequests, _minimumToken = 1, _executions;
        private int _browserMediaDenied, _browserMaster, _browserChild, _browserSegments;
        private bool _replace;
        public string Url { get; }
        public int PageRequests => Volatile.Read(ref _pageRequests);
        public int ScriptExecutions => Volatile.Read(ref _executions);
        public int BrowserMediaDenied => Volatile.Read(ref _browserMediaDenied);
        public int BrowserMasterRequests => Volatile.Read(ref _browserMaster);
        public int BrowserChildRequests => Volatile.Read(ref _browserChild);
        public int BrowserSegmentRequests => Volatile.Read(ref _browserSegments);
        public bool RequireBrowserForMedia { get; set; }
        public bool DenyManifests { get; set; }
        public bool ReorderOnRefresh { get; set; }
        public bool PauseRefreshedSegments { get; set; }
        public TaskCompletionSource RefreshedSegmentStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool SeparateQualities { get; set; }
        public bool IncludeMaster { get; set; }
        public bool Trailer { get; set; }
        public bool BrokenCandidate { get; set; }
        public string? Fault { get; set; }
        public void Expire(bool replace) { _minimumToken = PageRequests + 1; _replace = replace; }

        public FixtureServer(Dictionary<string, byte[]> assets)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/packed?video=fixture";
            _serve = ServeAsync(assets, _stop.Token);
        }

        private string Page(int issuedToken)
        {
            var prefix = _replace ? "/other/hls/" : "/hls/";
            var paths = SeparateQualities ? new List<string> { "90p/index.m3u8", "180p/index.m3u8" } : ["master.m3u8"];
            if (SeparateQualities && IncludeMaster) paths.Add("master.m3u8");
            var sources = string.Join(";", paths.Select((path, i) => $"var source{i}=\"{prefix}{path}?token={issuedToken}\"")) + ";fetch('/script-executed');";
            if (ReorderOnRefresh)
            {
                var other = $"var other=\"/other/hls/master.m3u8?token={issuedToken}\";";
                sources = issuedToken == 1 ? other + sources : sources + other;
            }
            // A Packer envelope with no symbol substitution exercises discovery;
            // the original fixture separately covers actual packed words.
            var script = "eval(function(p,a,c,k,e,d){return p;}(" + JsonSerializer.Serialize(sources) + ",36,0,\"\".split('|'),0,{}))";
            return "<html><title>Generated reliability demo</title>" +
                (Trailer ? "<video controls src='/preview.mp4'></video>" : "") +
                (BrokenCandidate ? "<video controls src='/broken.m3u8' type='application/x-mpegURL'></video>" : "") +
                "<script>" + script + "</script></html>";
        }

        private async Task ServeAsync(Dictionary<string, byte[]> assets, CancellationToken token)
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
                    var request = new Uri(new Uri(Url), first.Split(' ').ElementAtOrDefault(1) ?? "/");
                    var path = request.AbsolutePath;
                    var assetPath = path.StartsWith("/other/") ? path[6..] : path;
                    var issuedToken = System.Text.RegularExpressions.Regex.Match(request.Query, @"(?:[?&])token=(\d+)");
                    var number = issuedToken.Success ? int.Parse(issuedToken.Groups[1].Value) : 0;
                    if (PauseRefreshedSegments && number >= _minimumToken && _minimumToken > 1 && path.EndsWith(".bin"))
                    {
                        RefreshedSegmentStarted.TrySetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    }
                    var status = "200 OK";
                    var browserRequest = headers.ContainsKey("sec-ch-ua");
                    if (assetPath.StartsWith("/hls/"))
                    {
                        if (RequireBrowserForMedia && !browserRequest) Interlocked.Increment(ref _browserMediaDenied);
                        if (browserRequest)
                        {
                            if (assetPath.EndsWith("master.m3u8")) Interlocked.Increment(ref _browserMaster);
                            else if (assetPath.EndsWith(".m3u8")) Interlocked.Increment(ref _browserChild);
                            else if (assetPath.EndsWith(".bin")) Interlocked.Increment(ref _browserSegments);
                        }
                    }
                    byte[] body;
                    if (path == "/packed") body = Encoding.UTF8.GetBytes(Page(Interlocked.Increment(ref _pageRequests)));
                    else if (path == "/script-executed") { Interlocked.Increment(ref _executions); body = []; }
                    else if (assetPath.StartsWith("/hls/") && (number < _minimumToken ||
                             DenyManifests && assetPath.EndsWith(".m3u8") ||
                             RequireBrowserForMedia && !browserRequest ||
                             headers.GetValueOrDefault("Referer") != Url || headers.ContainsKey("Cookie")))
                    { status = "403 Forbidden"; body = Encoding.UTF8.GetBytes(status); }
                    else if (!assets.TryGetValue(assetPath, out body!) || Fault == "missing" && path.EndsWith("chunk001.bin"))
                    { status = "404 Not Found"; body = Encoding.UTF8.GetBytes(status); }
                    else if (Fault == "corrupt" && path.EndsWith("chunk001.bin"))
                        body = Encoding.UTF8.GetBytes("This is an HTML error body, not a video segment.");
                    else if (path.EndsWith(".m3u8"))
                    {
                        var manifest = Encoding.UTF8.GetString(body).Replace("low/", "90p/").Replace("high/", "180p/");
                        body = Encoding.UTF8.GetBytes(string.Join('\n', manifest.Split('\n').Select(line =>
                            string.IsNullOrWhiteSpace(line) || line.StartsWith('#') ? line : line.TrimEnd() + "?token=" + number)));
                    }
                    var contentType = path == "/packed" ? "text/html" : path.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" :
                        path.EndsWith(".mp4") ? "video/mp4" : "application/octet-stream";
                    var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response, token);
                    if (!first.StartsWith("HEAD ")) await stream.WriteAsync(body, token);
                }
                catch (IOException) when (!token.IsCancellationRequested) { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync(); _listener.Stop();
            try { await _serve; }
            catch (OperationCanceledException) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
            _stop.Dispose();
        }
    }
}
