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
    [InlineData("file:///C:/Windows/win.ini")]
    public void IsAllowedTopLevelUri_RejectsUntrustedNavigation(string value)
    {
        Assert.False(LoginSecurityPolicy.IsAllowedTopLevelUri(value));
    }

    [Theory]
    [InlineData("https://accounts.google.com/signin/v2/challenge/selection")]
    [InlineData("https://accounts.youtube.com/accounts/SetSID")]
    [InlineData("https://consent.google.com/m")]
    public void IsGoogleAuthenticationUri_RecognizesOnlyGoogleLoginFlow(string value)
    {
        Assert.True(LoginSecurityPolicy.IsGoogleAuthenticationUri(value));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://accounts.google.com.evil.example/signin")]
    [InlineData("http://accounts.google.com/signin")]
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
            "https://CHZZK.NAVER.COM/video/13940682?from=share");

        Assert.Equal("https://chzzk.naver.com/video/13940682", result);
    }

    [Fact]
    public void CreateCanonicalVideoUrl_RemovesUnneededYouTubeQueryValues()
    {
        var result = LoginSecurityPolicy.CreateCanonicalVideoUrl(
            "https://WWW.YouTube.com/watch?v=dQw4w9WgXcQ&list=PL123");

        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", result);
    }
}
