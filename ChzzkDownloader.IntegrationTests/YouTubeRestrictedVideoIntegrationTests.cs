using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.IntegrationTests;

[Collection(WebView2IntegrationCollection.Name)]
public sealed class YouTubeRestrictedVideoIntegrationTests
{
    [EnvironmentFact(
        IntegrationTestSettings.RunAuthTestsVariable,
        IntegrationTestSettings.MembershipUrlVariable)]
    [Trait("Category", "RestrictedYouTube")]
    public Task MembershipVideo_AnalyzesWithAppWebViewSession() =>
        AnalyzeRestrictedVideoAsync(IntegrationTestSettings.MembershipUrlVariable);

    [EnvironmentFact(
        IntegrationTestSettings.RunAuthTestsVariable,
        IntegrationTestSettings.AgeRestrictedUrlVariable)]
    [Trait("Category", "RestrictedYouTube")]
    public Task AgeRestrictedVideo_AnalyzesWithAppWebViewSession() =>
        AnalyzeRestrictedVideoAsync(IntegrationTestSettings.AgeRestrictedUrlVariable);

    private static async Task AnalyzeRestrictedVideoAsync(string urlVariable)
    {
        var url = IntegrationTestSettings.RequireUrl(urlVariable);
        Assert.True(VideoUrlService.TryParse(url, out var urlInfo));
        Assert.Equal(VideoSource.YouTube, urlInfo.Source);

        var cookies = await WebView2ProfileHarness.UseAsync(coreWebView =>
            WebViewCookieCollector.CollectAsync(coreWebView, VideoSource.YouTube));
        Assert.True(
            WebViewCookieCollector.HasAuthenticatedYouTubeSession(cookies),
            "앱 WebView2 프로필에 인증된 YouTube 세션이 없습니다.");

        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"StreamNest-RestrictedIntegration-{Guid.NewGuid():N}");
        try
        {
            var service = new YtDlpService(new CookieFileService(Path.Combine(temporaryRoot, "Cookies")));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            VideoInfo video = await service.AnalyzeAsync(url, cookies, timeout.Token);

            Assert.Equal(urlInfo.VideoId, video.Id);
            Assert.False(string.IsNullOrWhiteSpace(video.Title));
            Assert.NotEmpty(video.Formats);
            Assert.Contains(video.Formats, format => format.Height > 0);

            var selectedFormat = video.Formats.First(format => format.Height > 0);
            await YtDlpSimulationHarness.SimulateAsync(
                urlInfo.CanonicalUrl,
                selectedFormat.Selector,
                cookies,
                timeout.Token);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }
}
