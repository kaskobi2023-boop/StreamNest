using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class WebViewCookieCollectorTests
{
    [Fact]
    public void GetOrigins_SeparatesYouTubeAndChzzkDomains()
    {
        var youtube = WebViewCookieCollector.GetOrigins(VideoSource.YouTube);
        var chzzk = WebViewCookieCollector.GetOrigins(VideoSource.Chzzk);

        Assert.Contains(youtube, origin => origin.Contains("youtube.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(youtube, origin => origin.Contains("google.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(youtube, origin => origin.Contains("naver.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(chzzk, origin => origin.Contains("naver.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(chzzk, origin => origin.Contains("youtube.com", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("SAPISID")]
    [InlineData("LOGIN_INFO")]
    [InlineData("__Secure-1PSID")]
    public void HasAuthenticatedYouTubeSession_RecognizesAuthenticationCookies(string name)
    {
        BrowserCookie[] cookies =
        [
            new(name, "value", ".youtube.com", "/", true, true, 0)
        ];

        Assert.True(WebViewCookieCollector.HasAuthenticatedYouTubeSession(cookies));
    }

    [Fact]
    public void HasAuthenticatedYouTubeSession_RejectsVisitorOnlyCookies()
    {
        BrowserCookie[] cookies =
        [
            new("VISITOR_INFO1_LIVE", "value", ".youtube.com", "/", true, true, 0),
            new("YSC", "value", ".youtube.com", "/", true, true, 0)
        ];

        Assert.False(WebViewCookieCollector.HasAuthenticatedYouTubeSession(cookies));
    }
}
