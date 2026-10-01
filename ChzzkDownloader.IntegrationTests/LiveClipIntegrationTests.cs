using System.Text.Json;
using ChzzkDownloader.Services;
using Xunit.Abstractions;
using static ChzzkDownloader.IntegrationTests.BrowserCompatibilityIntegrationTests;

namespace ChzzkDownloader.IntegrationTests;

// Explicit opt-in: public server availability must not gate offline regression tests.
public sealed class LiveClipIntegrationTests(ITestOutputHelper output)
{
    [EnvironmentFact("STREAMNEST_RUN_CLIPS", "STREAMNEST_SOOP_CATCH_URL")]
    public Task SoopCatch_AnalyzesAndDownloads() => CheckAsync(
        Environment.GetEnvironmentVariable("STREAMNEST_SOOP_CATCH_URL")!, "soop");

    [EnvironmentFact("STREAMNEST_RUN_CLIPS", "STREAMNEST_CHZZK_CLIP_URL")]
    public Task ChzzkClip_AnalyzesAndDownloads() => CheckAsync(
        Environment.GetEnvironmentVariable("STREAMNEST_CHZZK_CLIP_URL")!, "chzzk");

    private async Task CheckAsync(string url, string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        var root = Environment.GetEnvironmentVariable("STREAMNEST_CLIP_OUTPUT") ??
                   Path.Combine(Path.GetTempPath(), "StreamNest-live-clips-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.True(VideoUrlService.TryParse(url, out var parsed));
        var service = new YtDlpService(new CookieFileService(), Path.Combine(root, "parts", name));
        var video = await service.AnalyzeAsync(parsed.CanonicalUrl, null, token);
        Assert.True(video.DurationSeconds > 0);
        var format = video.Formats.First();
        Assert.True(format.Height > 0);
        var path = await service.DownloadAsync(parsed.CanonicalUrl, Path.Combine(root, name), format,
            null, null, null, token);
        Assert.NotNull(path);
        var probe = await RunAsync(ToolLocator.FfprobePath,
            ["-v", "error", "-show_entries", "format=duration,size:stream=codec_type,height,width", "-of", "json", path], token);
        Assert.Equal(0, probe.ExitCode);
        using var json = JsonDocument.Parse(probe.Output);
        var actualDuration = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(actualDuration, video.DurationSeconds!.Value - .5, video.DurationSeconds.Value + .5);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        Assert.Contains(streams, s => s.GetProperty("codec_type").GetString() == "audio");
        Assert.Contains(streams, s => s.TryGetProperty("height", out var h) && h.GetInt32() == format.Height);
        var decode = await RunAsync(ToolLocator.FfmpegPath,
            ["-v", "error", "-xerror", "-i", path, "-f", "null", "-"], token);
        Assert.True(decode.ExitCode == 0, decode.Error);
        await File.WriteAllTextAsync(Path.Combine(root, name + "-probe.json"), probe.Output, token);
        output.WriteLine($"{name}: {video.Formats.Count} qualities, selected {format.Height}p, {video.DurationSeconds}s, {new FileInfo(path).Length} bytes; video/audio/full decode passed.");
    }
}
