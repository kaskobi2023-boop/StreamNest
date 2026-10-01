using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class ChzzkProbeServiceTests
{
    [Theory]
    [InlineData("https://chzzk.naver.com/video/87654321", "87654321")]
    [InlineData("https://chzzk.naver.com/video/12345678?from=share", "12345678")]
    public void TryGetVideoId_AcceptsExpectedUrls(string url, string expectedId)
    {
        Assert.True(ChzzkProbeService.TryGetVideoId(url, out var videoId));
        Assert.Equal(expectedId, videoId);
    }

    [Theory]
    [InlineData("https://example.com/video/87654321")]
    [InlineData("https://chzzk.naver.com.evil.example/video/87654321")]
    [InlineData("http://chzzk.naver.com/video/42/")]
    [InlineData("https://chzzk.naver.com:444/video/42/")]
    [InlineData("ftp://chzzk.naver.com/video/87654321")]
    [InlineData("https://chzzk.naver.com/clips/87654321")]
    [InlineData("not a url")]
    public void TryGetVideoId_RejectsUnexpectedUrls(string url)
    {
        Assert.False(ChzzkProbeService.TryGetVideoId(url, out _));
    }
}
