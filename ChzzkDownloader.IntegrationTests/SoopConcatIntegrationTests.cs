using System.Text.Json;
using ChzzkDownloader.Services;
using static ChzzkDownloader.IntegrationTests.BrowserCompatibilityIntegrationTests;

namespace ChzzkDownloader.IntegrationTests;

public sealed class SoopConcatIntegrationTests
{
    [Fact]
    public async Task ValidVariableFrameTimestampsAreNotTreatedAsCorruption()
    {
        var root = Path.Combine(Path.GetTempPath(), "StreamNest-Vfr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var path = Path.Combine(root, "vfr.mp4");
            var generated = await RunAsync(ToolLocator.FfmpegPath,
                ["-v", "error", "-f", "lavfi", "-i", "testsrc2=size=256x144:rate=30", "-frames:v", "60",
                 "-vf", "settb=1/48000,setpts=N*1600+mod(N\\,3)*700", "-fps_mode:v", "passthrough",
                 "-enc_time_base:v", "1/48000", "-c:v", "libx264", "-preset", "ultrafast", "-video_track_timescale", "48000", path], timeout.Token);
            Assert.True(generated.ExitCode == 0, generated.Error);
            await MediaVerificationService.VerifyAsync(path, null, 144, false, timeout.Token);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WholeVodReportsOnlyConcatenatedFileAndMissingPartFails(bool missingPart)
    {
        var root = Path.Combine(Path.GetTempPath(), "StreamNest-SoopConcat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var token = timeout.Token;
        try
        {
            var sample = Path.Combine(root, "sample.mp4");
            var generated = await RunAsync(ToolLocator.FfmpegPath,
                ["-v", "error", "-f", "lavfi", "-i", "testsrc2=size=256x144:rate=10", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
                 "-t", "2", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", sample], token);
            Assert.True(generated.ExitCode == 0, generated.Error);
            object Part(string id, string path) => new
            {
                id, title = "구간 " + id, duration = 2, extractor = "soop", extractor_key = "AfreecaTV",
                formats = new[] { new { format_id = "http", url = new Uri(path).AbsoluteUri, height = 144, vcodec = "h264", acodec = "aac", ext = "mp4" } }
            };
            var fixture = JsonSerializer.Serialize(new
            {
                _type = "multi_video", id = "fixture123", title = "전체 숲 시험", duration = 4, extractor = "soop", extractor_key = "AfreecaTV",
                entries = new[] { Part("p1", sample), Part("p2", missingPart ? Path.Combine(root, "missing.mp4") : sample) }
            });
            using var json = JsonDocument.Parse(fixture);
            var format = Assert.Single(YtDlpService.ParseSoopVideo(json.RootElement).Formats);
            var infoPath = Path.Combine(root, "fixture.json");
            await File.WriteAllTextAsync(infoPath, fixture, token);
            var args = YtDlpService.BuildDownloadArguments("https://vod.sooplive.com/player/123", root, Path.Combine(root, "parts"), format, null);
            args.RemoveAt(args.Count - 1); // Only the synthetic test replaces network extraction.
            args.AddRange(["--enable-file-urls", "--no-clean-info-json", "--load-info-json", infoPath]);
            var result = await RunAsync(ToolLocator.YtDlpPath, args, token);
            if (missingPart)
            {
                Assert.NotEqual(0, result.ExitCode);
                Assert.False(File.Exists(Path.Combine(root, "SOOP [fixture123].mp4")));
                return;
            }
            Assert.True(result.ExitCode == 0, result.Error);
            var finalPath = Path.Combine(root, "SOOP [fixture123].mp4");
            Assert.True(File.Exists(finalPath), result.Output);
            Assert.Contains("fixture123", finalPath);
            await MediaVerificationService.VerifyAsync(finalPath, 4, 144, true, token);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
