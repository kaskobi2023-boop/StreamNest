using ChzzkDownloader.Services;
using Xunit.Abstractions;

namespace ChzzkDownloader.IntegrationTests;

[Collection(WebView2IntegrationCollection.Name)]
public sealed class SoopSessionIntegrationTests(ITestOutputHelper output)
{
    [EnvironmentFact("STREAMNEST_RUN_SOOP_SESSION_TEST", "STREAMNEST_SOOP_TEST_URL")]
    [Trait("Category", "AuthenticatedWebView2")]
    public async Task ExistingAppSoopSessionCanAnalyzeUserSelectedVod()
    {
        var url = Environment.GetEnvironmentVariable("STREAMNEST_SOOP_TEST_URL")!;
        Assert.True(VideoUrlService.TryParse(url, out var parsed) && parsed.Source == VideoSource.Soop);
        // Read only through WebView2's supported cookie API. No navigation,
        // login automation, cookie modification, or cookie-value logging.
        var cookies = await WebView2ProfileHarness.UseAsync(core => WebViewCookieCollector.CollectAsync(core, VideoSource.Soop));
        Assert.True(PlaybackCookiePolicy.HasSoopLoginTickets(cookies), "SOOP login tickets are unavailable in the app profile.");
        output.WriteLine($"SOOP session tickets collected: {cookies.Count}; values not logged.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var service = new YtDlpService(new CookieFileService());
        var video = await service.AnalyzeAsync(parsed!.CanonicalUrl, cookies, timeout.Token);
        Assert.NotEmpty(video.Formats);
        output.WriteLine($"Authenticated VOD analysis succeeded; parts={video.PartCount}; format options={video.Formats.Count}.");
        foreach (var format in video.Formats)
            output.WriteLine($"Quality: height={format.Height?.ToString() ?? "unknown"}, fps={format.Fps?.ToString() ?? "unknown"}, estimatedBytes={format.EstimatedBytes?.ToString() ?? "unknown"}");
    }
}
