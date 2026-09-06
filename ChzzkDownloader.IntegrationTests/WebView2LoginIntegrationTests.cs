using ChzzkDownloader.Services;

namespace ChzzkDownloader.IntegrationTests;

[Collection(WebView2IntegrationCollection.Name)]
public sealed class WebView2LoginIntegrationTests
{
    [EnvironmentFact(IntegrationTestSettings.RunAuthTestsVariable)]
    [Trait("Category", "AuthenticatedWebView2")]
    public async Task AppProfile_CanOpenYouTubeAndContainsAuthenticatedSession()
    {
        var cookies = await WebView2ProfileHarness.UseAsync(async coreWebView =>
        {
            await WebView2ProfileHarness.NavigateAsync(
                coreWebView,
                "https://www.youtube.com/?hl=ko&gl=KR",
                TimeSpan.FromSeconds(45));
            return await WebViewCookieCollector.CollectAsync(coreWebView, VideoSource.YouTube);
        });

        Assert.True(
            WebViewCookieCollector.HasAuthenticatedYouTubeSession(cookies),
            $"인증된 YouTube 세션 쿠키를 찾지 못했습니다. 앱의 로그인 창에서 먼저 로그인하세요. 수집된 쿠키 수: {cookies.Count}");
    }
}
