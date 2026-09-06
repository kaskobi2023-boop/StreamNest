using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.IntegrationTests;

[Collection(WebView2IntegrationCollection.Name)]
public sealed class YouTubeDownloadIntegrationTests
{
    [EnvironmentFact(
        IntegrationTestSettings.RunDownloadTestsVariable,
        IntegrationTestSettings.RunNetworkTestsVariable)]
    [Trait("Category", "ActualDownload")]
    public async Task PublicVideo_AppDownloadMethodRecognizesFileInKoreanFolder()
    {
        const string url = "https://www.youtube.com/watch?v=jNQXAC9IVRw";
        Assert.True(VideoUrlService.TryParse(url, out var urlInfo));
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"StreamNest-한글저장폴더-{Guid.NewGuid():N}");
        var outputFolder = Path.Combine(temporaryRoot, "영상다운로드");
        try
        {
            var service = new YtDlpService(
                new CookieFileService(Path.Combine(temporaryRoot, "Cookies")),
                Path.Combine(temporaryRoot, "Partials"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            VideoInfo video = await service.AnalyzeAsync(urlInfo.CanonicalUrl, [], timeout.Token);
            var selectedFormat = video.Formats
                .Where(format => format.Height > 0)
                .OrderBy(format => format.Height)
                .First();
            var progressRecorder = new ProgressRecorder();

            var completedPath = await service.DownloadAsync(
                urlInfo.CanonicalUrl,
                outputFolder,
                selectedFormat,
                [],
                progress: progressRecorder,
                log: null,
                timeout.Token);

            Assert.False(string.IsNullOrWhiteSpace(completedPath));
            Assert.True(File.Exists(completedPath));
            Assert.Contains($"[{urlInfo.VideoId}]", Path.GetFileName(completedPath));
            var bytes = new FileInfo(completedPath).Length;
            Assert.True(bytes > 32 * 1024, $"Downloaded file was too small: {bytes} bytes");
            var progressValues = progressRecorder.Values;
            Assert.NotEmpty(progressValues);
            Assert.Equal(100d, progressValues[^1], 6);
            Assert.True(
                progressValues.Zip(progressValues.Skip(1), (previous, current) => current >= previous)
                    .All(isMonotonic => isMonotonic),
                $"Progress moved backward: {string.Join(", ", progressValues.Select(value => value.ToString("0.0")))}");
            Console.WriteLine($"PROGRESS_OK|{string.Join(" => ", progressRecorder.Messages)}");
            Console.WriteLine($"APP_DOWNLOAD_OK|path={completedPath}|bytes={bytes}");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [EnvironmentFact(
        IntegrationTestSettings.RunDownloadTestsVariable,
        IntegrationTestSettings.MembershipUrlVariable)]
    [Trait("Category", "ActualDownload")]
    public async Task MembershipVideo_DownloadsAndProducesPlayableSample()
    {
        var url = IntegrationTestSettings.RequireUrl(IntegrationTestSettings.MembershipUrlVariable);
        Assert.True(VideoUrlService.TryParse(url, out var urlInfo));
        Assert.Equal(VideoSource.YouTube, urlInfo.Source);

        var cookies = await WebView2ProfileHarness.UseAsync(coreWebView =>
            WebViewCookieCollector.CollectAsync(coreWebView, VideoSource.YouTube));
        Assert.True(
            WebViewCookieCollector.HasAuthenticatedYouTubeSession(cookies),
            "앱 WebView2 프로필에 인증된 YouTube 세션이 없습니다.");

        var service = new YtDlpService(new CookieFileService());
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        VideoInfo video = await service.AnalyzeAsync(urlInfo.CanonicalUrl, cookies, timeout.Token);
        var selectedFormat = video.Formats
            .Where(format => format.Height > 0)
            .OrderBy(format => format.Height)
            .First();

        var sample = await YtDlpSimulationHarness.DownloadSampleAsync(
            urlInfo.CanonicalUrl,
            selectedFormat.Selector,
            cookies,
            TimeSpan.FromSeconds(6),
            timeout.Token);

        Assert.True(sample.Bytes > 32 * 1024, $"Downloaded sample was too small: {sample.Bytes} bytes");
        Assert.InRange(sample.DurationSeconds, 0.5, 20);
        Console.WriteLine(
            $"DOWNLOAD_SAMPLE_OK|id={video.Id}|height={selectedFormat.Height}|bytes={sample.Bytes}|duration={sample.DurationSeconds:0.###}");
    }

    private sealed class ProgressRecorder : IProgress<DownloadProgressInfo>
    {
        private readonly object _sync = new();
        private readonly List<double> _values = [];
        private readonly List<string> _messages = [];

        public IReadOnlyList<double> Values
        {
            get
            {
                lock (_sync)
                    return _values.ToArray();
            }
        }

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_sync)
                    return _messages.ToArray();
            }
        }

        public void Report(DownloadProgressInfo value)
        {
            if (!value.Percent.HasValue)
                return;
            lock (_sync)
            {
                _values.Add(value.Percent.Value);
                _messages.Add(value.Message);
            }
        }
    }
}
