using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ChzzkDownloader.Services;
using Xunit.Abstractions;

namespace ChzzkDownloader.IntegrationTests;

public sealed class BrowserCompatibilityIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task GenericBrowserRequest_ResolvesSimulated403_AndDownloadsDecodableVideo()
    {
        // This is a controlled header-gating fixture, NOT a real Cloudflare challenge.
        // Use production argument builders without relaxing the app's public-HTTPS URL policy.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        var root = Path.Combine(Path.GetTempPath(), $"StreamNest-BrowserCompatibility-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var sample = Path.Combine(root, "source.mp4");
            var generated = await RunAsync(ToolLocator.FfmpegPath,
                ["-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=160x90:r=10", "-t", "2",
                 "-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags", "+faststart", sample], token);
            Assert.Equal(0, generated.ExitCode);
            await using var server = new BrowserHeaderVideoServer(await File.ReadAllBytesAsync(sample, token));
            Assert.False(WebVideoUrlService.TryParse(server.Url, out _));

            var legacyArgs = YtDlpService.BuildWebAnalyzeArguments(server.Url);
            legacyArgs.Remove("--ignore-errors");
            legacyArgs.RemoveRange(legacyArgs.IndexOf("--extractor-args"), 2);
            legacyArgs.RemoveRange(legacyArgs.IndexOf("--impersonate"), 2);
            var before = await RunAsync(ToolLocator.YtDlpPath, legacyArgs, token);
            Assert.NotEqual(0, before.ExitCode);
            Assert.Contains("Got HTTP Error 403 caused by Cloudflare anti-bot challenge", before.Error);
            Assert.Contains("generic:impersonate", before.Error);
            Assert.True(server.BlockedRequests > 0);

            var analysis = await RunAsync(ToolLocator.YtDlpPath,
                YtDlpService.BuildWebAnalyzeArguments(server.Url), token);
            Assert.True(analysis.ExitCode == 0, analysis.Error);
            using var document = JsonDocument.Parse(analysis.Output);
            var item = Assert.Single(YtDlpService.ParseWebVideos(document.RootElement));
            var selected = item.SelectFormat(Assert.Single(item.Video.Formats));
            var outputFolder = Path.Combine(root, "영상 결과");
            var download = await RunAsync(ToolLocator.YtDlpPath,
                YtDlpService.BuildDownloadArguments(server.Url, outputFolder, Path.Combine(root, "parts"), selected, null), token);
            Assert.True(download.ExitCode == 0, download.Error);
            var final = Assert.Single(download.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
                line => line.StartsWith("FINAL|", StringComparison.Ordinal))[6..].Trim();
            Assert.True(File.Exists(final), download.Output);
            Assert.True(new FileInfo(final).Length > 0);
            Assert.True(server.BrowserPageRequests >= 2, "Both analysis and download re-extraction must use browser requests.");
            Assert.True(server.MediaRequests > 0);
            var decoded = await RunAsync(ToolLocator.FfmpegPath,
                ["-v", "error", "-xerror", "-i", final, "-f", "null", "-"], token);
            Assert.True(decoded.ExitCode == 0, decoded.Error);
            output.WriteLine($"Before: HTTP 403. After: analysis + download + full decode passed. Bytes={new FileInfo(final).Length}; browser page requests={server.BrowserPageRequests}.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string executable, IEnumerable<string> arguments, CancellationToken token)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        return (process.ExitCode, await stdout, await stderr);
    }

    private sealed class BrowserHeaderVideoServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        private int _blocked, _browserPages, _media;
        public int BlockedRequests => Volatile.Read(ref _blocked);
        public int BrowserPageRequests => Volatile.Read(ref _browserPages);
        public int MediaRequests => Volatile.Read(ref _media);
        public string Url { get; }

        public BrowserHeaderVideoServer(byte[] media)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/video";
            _serve = ServeAsync(media, _stop.Token);
        }

        private async Task ServeAsync(byte[] media, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(token);
                await using var stream = client.GetStream();
                try
                {
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var firstLine = await reader.ReadLineAsync(token) ?? "";
                    var browserRequest = false;
                    while (await reader.ReadLineAsync(token) is { Length: > 0 } header)
                        browserRequest |= header.StartsWith("sec-ch-ua:", StringComparison.OrdinalIgnoreCase);
                    var isMedia = firstLine.Contains(" /sample.mp4 ", StringComparison.Ordinal);
                    var allowed = isMedia || browserRequest;
                    if (isMedia) Interlocked.Increment(ref _media);
                    else if (browserRequest) Interlocked.Increment(ref _browserPages);
                    else Interlocked.Increment(ref _blocked);
                    var body = isMedia ? media : Encoding.UTF8.GetBytes(allowed
                        ? "<html><title>Browser compatibility fixture</title><video controls><source src='/sample.mp4' type='video/mp4'></video></html>"
                        : "<html><title>Attention Required! | Cloudflare</title>Simulated challenge response for automated tests only.</html>");
                    var headers = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 {(allowed ? "200 OK" : "403 Forbidden")}\r\n" +
                        $"Content-Type: {(isMedia ? "video/mp4" : "text/html; charset=utf-8")}\r\n" +
                        (allowed ? "" : "cf-mitigated: challenge\r\n") +
                        $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers, token);
                    if (!firstLine.StartsWith("HEAD ", StringComparison.Ordinal))
                        await stream.WriteAsync(body, token);
                }
                catch (IOException) when (!token.IsCancellationRequested)
                {
                    // yt-dlp can close a media probe after reading enough bytes.
                }
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
