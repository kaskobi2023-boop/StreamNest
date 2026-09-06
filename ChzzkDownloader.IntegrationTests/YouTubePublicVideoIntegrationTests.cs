using ChzzkDownloader.Services;

namespace ChzzkDownloader.IntegrationTests;

public sealed class YouTubePublicVideoIntegrationTests
{
    [EnvironmentFact(IntegrationTestSettings.RunNetworkTestsVariable)]
    [Trait("Category", "Network")]
    public async Task PublicVideo_AnalyzesWithoutCookies()
    {
        var url = Environment.GetEnvironmentVariable(IntegrationTestSettings.PublicUrlVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(url))
            url = "https://www.youtube.com/watch?v=jNQXAC9IVRw";
        Assert.True(VideoUrlService.TryParse(url, out var urlInfo));
        Assert.Equal(VideoSource.YouTube, urlInfo.Source);
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"StreamNest-PublicIntegration-{Guid.NewGuid():N}");
        try
        {
            var service = new YtDlpService(new CookieFileService(Path.Combine(temporaryRoot, "Cookies")));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var video = await service.AnalyzeAsync(urlInfo.CanonicalUrl, [], timeout.Token);

            Assert.Equal(urlInfo.VideoId, video.Id);
            Assert.NotEmpty(video.Formats);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }
}
