using System.Diagnostics;
using System.Text.Json;
using ChzzkDownloader.Services;
using Xunit.Abstractions;

namespace ChzzkDownloader.IntegrationTests;

[Collection(WebView2IntegrationCollection.Name)]
public sealed class RPlaySessionIntegrationTests(ITestOutputHelper output)
{
    [EnvironmentFact("STREAMNEST_RUN_RPLAY_SESSION_TESTS")]
    [Trait("Category", "AuthenticatedRPlay")]
    public async Task ExistingBrowserAccount_AnalyzesRequestedVideo()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var cookies = await StaThreadRunner.RunAsync(() => RPlaySessionService.RestoreAsync(null, timeout.Token));
        Assert.True(cookies.Count >= 3, "현재 RPlay 브라우저 계정 세션을 읽지 못했습니다.");
        output.WriteLine("Current browser session restored; credential values omitted.");
        var url = Environment.GetEnvironmentVariable("STREAMNEST_RPLAY_TEST_URL");
        if (string.IsNullOrWhiteSpace(url)) return;
        var cookieFiles = new CookieFileService();
        var video = await new YtDlpService(cookieFiles).AnalyzeAsync(url, cookies, timeout.Token);
        Assert.NotEmpty(video.Formats);
        output.WriteLine($"Analysis succeeded: {video.Formats.Count} formats.");
        var sampleDirectory = Environment.GetEnvironmentVariable("STREAMNEST_RPLAY_SAMPLE_DIR");
        if (string.IsNullOrWhiteSpace(sampleDirectory)) return;
        Directory.CreateDirectory(sampleDirectory);
        var sample = Path.Combine(sampleDirectory, "rplay-session-sample.mp4");
        var cookiePath = await cookieFiles.CreateAsync(cookies, VideoSource.RPlay);
        try
        {
            var result = await RunAsync(ToolLocator.YtDlpPath,
            [
                "--no-config", "--no-playlist", "--no-cache-dir", "--no-progress", "--no-overwrites",
                "--plugin-dirs", ToolLocator.YtDlpPluginDirectory,
                "--ffmpeg-location", ToolLocator.ToolsDirectory,
                "--cookies", cookiePath!, "--format", video.Formats.Last().Selector,
                "--download-sections", "*0-5", "--force-keyframes-at-cuts",
                "--merge-output-format", "mp4", "--output", sample,
                "--socket-timeout", "15", "--retries", "1", url
            ], timeout.Token);
            Assert.True(result.ExitCode == 0, "Five-second sample transfer failed; raw credential-bearing process output intentionally omitted.");
            Assert.True(File.Exists(sample) && new FileInfo(sample).Length > 0);
            var probe = await RunAsync(ToolLocator.FfprobePath,
                ["-v", "error", "-show_entries", "format=duration:stream=codec_type", "-of", "json", sample], timeout.Token);
            Assert.Equal(0, probe.ExitCode);
            using var metadata = JsonDocument.Parse(probe.Output);
            var seconds = double.Parse(metadata.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(seconds, 4, 8);
            Assert.NotEmpty(metadata.RootElement.GetProperty("streams").EnumerateArray());
            var decode = await RunAsync(ToolLocator.FfmpegPath, ["-v", "error", "-xerror", "-i", sample, "-f", "null", "-"], timeout.Token);
            Assert.Equal(0, decode.ExitCode);
            output.WriteLine($"Sample verified: {seconds:F2} seconds, {new FileInfo(sample).Length} bytes; full decode passed.");
        }
        finally { cookieFiles.Delete(cookiePath); }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string file, string[] arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        await stderr; // Drain but never log output that may contain signed stream URLs.
        return (process.ExitCode, await stdout);
    }
}
