using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class ChzzkProbeServiceTests
{
    [Theory]
    [InlineData("https://chzzk.naver.com/video/13940682", "13940682")]
    [InlineData("https://chzzk.naver.com/video/14471813?from=share", "14471813")]
    public void TryGetVideoId_AcceptsExpectedUrls(string url, string expectedId)
    {
        Assert.True(ChzzkProbeService.TryGetVideoId(url, out var videoId));
        Assert.Equal(expectedId, videoId);
    }

    [Theory]
    [InlineData("https://example.com/video/13940682")]
    [InlineData("https://chzzk.naver.com.evil.example/video/13940682")]
    [InlineData("http://chzzk.naver.com/video/42/")]
    [InlineData("https://chzzk.naver.com:444/video/42/")]
    [InlineData("ftp://chzzk.naver.com/video/13940682")]
    [InlineData("https://chzzk.naver.com/clips/13940682")]
    [InlineData("not a url")]
    public void TryGetVideoId_RejectsUnexpectedUrls(string url)
    {
        Assert.False(ChzzkProbeService.TryGetVideoId(url, out _));
    }
}
