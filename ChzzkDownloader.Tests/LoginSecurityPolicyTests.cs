using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class LoginSecurityPolicyTests
{
    [Theory]
    [InlineData("https://chzzk.naver.com/video/1")]
    [InlineData("https://nid.naver.com/nidlogin.login")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://accounts.google.com/ServiceLogin")]
    [InlineData("https://accounts.google.co.kr/ServiceLogin")]
    [InlineData("https://consent.google.com/")]
    public void IsAllowedTopLevelUri_AllowsRequiredLoginHosts(string value)
    {
        Assert.True(LoginSecurityPolicy.IsAllowedTopLevelUri(value));
    }

    [Theory]
    [InlineData("http://nid.naver.com/nidlogin.login")]
    [InlineData("https://nid.naver.com:444/nidlogin.login")]
    [InlineData("https://help.naver.com/")]
    [InlineData("https://naver.example/")]
    [InlineData("https://evil.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com.evil.example/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://accounts.google.co.kr.evil.example/")]
    [InlineData("https://user:pass@accounts.google.co.kr/")]
    [InlineData("http://accounts.google.co.kr/")]
    [InlineData("https://accounts.google.co.kr:8443/")]
    [InlineData("https://google.co.kr.attacker.com/")]
    [InlineData("file:///C:/Windows/win.ini")]
    public void IsAllowedTopLevelUri_RejectsUntrustedNavigation(string value)
    {
        Assert.False(LoginSecurityPolicy.IsAllowedTopLevelUri(value));
    }

    [Theory]
    [InlineData("https://accounts.google.com/signin/v2/challenge/selection")]
    [InlineData("https://accounts.google.co.kr/signin/v2/challenge/selection")]
    [InlineData("https://accounts.youtube.com/accounts/SetSID")]
    [InlineData("https://consent.google.com/m")]
    [InlineData("https://myaccount.google.com/security")]
    public void IsGoogleAuthenticationUri_RecognizesOnlyGoogleLoginFlow(string value)
    {
        Assert.True(LoginSecurityPolicy.IsGoogleAuthenticationUri(value));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://accounts.google.com.evil.example/signin")]
    [InlineData("https://accounts.google.co.kr.evil.example/signin")]
    [InlineData("https://google.co.kr.attacker.com/signin")]
    [InlineData("http://accounts.google.com/signin")]
    [InlineData("http://accounts.google.co.kr/signin")]
    [InlineData("https://accounts.google.com:8443/signin")]
    [InlineData("https://accounts.google.co.kr:8443/signin")]
    [InlineData("https://user:pass@accounts.google.com/signin")]
    [InlineData("https://user:pass@accounts.google.co.kr/signin")]
    [InlineData("https://google.com/search")]
    [InlineData("https://google.co.kr/search")]
    [InlineData("https://www.google.com/")]
    [InlineData("https://www.google.co.kr/")]
    [InlineData("https://g.co/verify")]
    public void IsGoogleAuthenticationUri_RejectsVideoAndUntrustedPages(string value)
    {
        Assert.False(LoginSecurityPolicy.IsGoogleAuthenticationUri(value));
    }

    [Theory]
    [InlineData(".naver.com")]
    [InlineData("chzzk.naver.com")]
    [InlineData("api.chzzk.naver.com")]
    [InlineData("apis.naver.com")]
    [InlineData(".youtube.com")]
    [InlineData("youtube.com")]
    [InlineData(".google.com")]
    [InlineData("accounts.google.com")]
    public void IsAllowedCookieDomain_AllowsPlaybackAndLoginDomains(string value)
    {
        Assert.True(LoginSecurityPolicy.IsAllowedCookieDomain(value));
    }

    [Theory]
    [InlineData("nid.naver.com")]
    [InlineData("blog.naver.com")]
    [InlineData("naver.example")]
    [InlineData("youtube.example")]
    [InlineData("evil.youtube.com")]
    public void IsAllowedCookieDomain_RejectsUnneededDomains(string value)
    {
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain(value));
    }

    [Fact]
    public void CreateCanonicalVideoUrl_RemovesQueryAndNormalizesSchemeAndHost()
    {
        var result = LoginSecurityPolicy.CreateCanonicalVideoUrl(
            "https://CHZZK.NAVER.COM/video/87654321?from=share");

        Assert.Equal("https://chzzk.naver.com/video/87654321", result);
    }

    [Fact]
    public void CreateCanonicalVideoUrl_RemovesUnneededYouTubeQueryValues()
    {
        var result = LoginSecurityPolicy.CreateCanonicalVideoUrl(
            "https://WWW.YouTube.com/watch?v=dQw4w9WgXcQ&list=PL123");

        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", result);
    }

    [Theory]
    // YouTube helper frames
    [InlineData("https://apis.google.com/js/api.js", VideoSource.YouTube, true)]
    [InlineData("https://ssl.gstatic.com/accounts/static", VideoSource.YouTube, true)]
    [InlineData("https://www.gstatic.com/recaptcha/api.js", VideoSource.YouTube, true)]
    [InlineData("https://recaptcha.net/recaptcha/api.js", VideoSource.YouTube, true)]
    [InlineData("https://www.recaptcha.net/recaptcha/api.js", VideoSource.YouTube, true)]
    [InlineData("https://accounts.google.com/o/oauth2/auth", VideoSource.YouTube, true)]
    [InlineData("https://accounts.google.co.kr/o/oauth2/auth", VideoSource.YouTube, true)]
    [InlineData("https://consent.google.com/m", VideoSource.YouTube, true)]
    [InlineData("https://myaccount.google.com/signinoptions", VideoSource.YouTube, true)]
    // RPlay helper frames
    [InlineData("https://apis.google.com/js/api.js", VideoSource.RPlay, true)]
    [InlineData("https://ssl.gstatic.com/accounts/static", VideoSource.RPlay, true)]
    [InlineData("https://www.gstatic.com/recaptcha/api.js", VideoSource.RPlay, true)]
    [InlineData("https://recaptcha.net/recaptcha/api.js", VideoSource.RPlay, true)]
    [InlineData("https://www.recaptcha.net/recaptcha/api.js", VideoSource.RPlay, true)]
    [InlineData("https://accounts.google.com/o/oauth2/auth", VideoSource.RPlay, true)]
    [InlineData("https://accounts.google.co.kr/o/oauth2/auth", VideoSource.RPlay, true)]
    [InlineData("https://consent.google.com/m", VideoSource.RPlay, true)]
    [InlineData("https://myaccount.google.com/signinoptions", VideoSource.RPlay, true)]
    // Top-level origin inside frame
    [InlineData("https://rplay.live/embed", VideoSource.RPlay, true)]
    [InlineData("https://chzzk.naver.com/live/1", VideoSource.Chzzk, true)]
    [InlineData("https://vod.sooplive.com/player/1", VideoSource.Soop, true)]
    // Chzzk and Soop reject Google helper frames
    [InlineData("https://apis.google.com/js/api.js", VideoSource.Chzzk, false)]
    [InlineData("https://apis.google.com/js/api.js", VideoSource.Soop, false)]
    [InlineData("https://ssl.gstatic.com/accounts/static", VideoSource.Chzzk, false)]
    [InlineData("https://recaptcha.net/recaptcha/api.js", VideoSource.Soop, false)]
    // Attack and invalid inputs
    [InlineData("https://apis.google.com.attacker.com/js/api.js", VideoSource.RPlay, false)]
    [InlineData("https://ssl.gstatic.com.evil.com/accounts", VideoSource.YouTube, false)]
    [InlineData("https://recaptcha.net.phishing.org/test", VideoSource.RPlay, false)]
    [InlineData("https://evil.attacker.com/iframe", VideoSource.RPlay, false)]
    [InlineData("http://apis.google.com/js/api.js", VideoSource.RPlay, false)]
    [InlineData("https://apis.google.com:8443/js/api.js", VideoSource.RPlay, false)]
    [InlineData("https://ssl.gstatic.com:8080/static", VideoSource.YouTube, false)]
    [InlineData("https://user:pass@apis.google.com/js/api.js", VideoSource.RPlay, false)]
    [InlineData("https://accounts.google.co.kr.attacker.com/oauth", VideoSource.RPlay, false)]
    [InlineData("https://accounts.google.co.kr.attacker.com/oauth", VideoSource.YouTube, false)]
    [InlineData("https://user:pass@accounts.google.co.kr/oauth", VideoSource.RPlay, false)]
    [InlineData("http://accounts.google.co.kr/oauth", VideoSource.RPlay, false)]
    [InlineData("https://accounts.google.co.kr:8443/oauth", VideoSource.RPlay, false)]
    [InlineData("https://google.co.kr.attacker.com/oauth", VideoSource.RPlay, false)]
    public void IsAllowedFrameUri_EnforcesRestrictedFrameHosts(string url, VideoSource source, bool expected)
    {
        Assert.Equal(expected, LoginSecurityPolicy.IsAllowedFrameUri(url, source));
    }

    [Theory]
    [InlineData("https://accounts.google.co.kr/signin", true)]
    [InlineData("https://accounts.google.com/signin", true)]
    [InlineData("https://user:pass@accounts.google.co.kr/signin", false)]
    [InlineData("http://accounts.google.co.kr/signin", false)]
    [InlineData("https://accounts.google.co.kr:8080/signin", false)]
    [InlineData("not-a-url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryGetExternalHttpsUri_ValidatesSchemePortAndUserInfo(string? input, bool expected)
    {
        var result = LoginSecurityPolicy.TryGetExternalHttpsUri(input, out var uri);
        Assert.Equal(expected, result);
        if (expected)
        {
            Assert.NotNull(uri);
            Assert.Empty(uri.UserInfo);
            Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
            Assert.True(uri.IsDefaultPort);
        }
    }

    [Theory]
    [InlineData("https://user:password@accounts.google.co.kr/signin?auth=xyz#token", "https://accounts.google.co.kr")]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth?client_id=123", "https://accounts.google.com")]
    [InlineData("https://accounts.google.com:8443/test", "https://accounts.google.com:8443")]
    [InlineData("http://google.com/test", "http://google.com")]
    [InlineData("javascript:alert(1)", "javascript:<redacted>")]
    [InlineData("data:text/html,test", "data:<redacted>")]
    [InlineData("not-a-valid-uri", "<invalid>")]
    [InlineData(null, "<invalid>")]
    [InlineData("", "<invalid>")]
    public void GetDiagnosticOrigin_RedactsCredentialsAndSanitizesOutput(string? input, string expected)
    {
        var result = LoginSecurityPolicy.GetDiagnosticOrigin(input);
        Assert.Equal(expected, result);
    }
}
