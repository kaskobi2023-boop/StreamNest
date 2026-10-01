using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class VideoUrlServiceTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ&list=PL123", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=42", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void TryParse_AcceptsSupportedYouTubeUrls(string url, string expectedId)
    {
        Assert.True(VideoUrlService.TryParse(url, out var info));
        Assert.Equal(VideoSource.YouTube, info.Source);
        Assert.Equal(expectedId, info.VideoId);
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PL123456")]
    [InlineData("https://www.youtube.com/watch")]
    [InlineData("https://www.youtube.com/watch?v=bad.id")]
    [InlineData("https://youtube.com.evil.example/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://evil.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/")]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://user:secret@www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://user:secret@chzzk.naver.com/video/12345678")]
    public void TryParse_RejectsUnsupportedOrUnsafeUrls(string url)
    {
        Assert.False(VideoUrlService.TryParse(url, out _));
    }

    [Fact]
    public void TryParse_StillRecognizesChzzkUrls()
    {
        Assert.True(VideoUrlService.TryParse(
            "https://chzzk.naver.com/video/12345678?from=share",
            out var info));

        Assert.Equal(VideoSource.Chzzk, info.Source);
        Assert.Equal("12345678", info.VideoId);
    }

    [Fact]
    public void TryParse_CanonicalizesYouTubeShareUrl()
    {
        Assert.True(VideoUrlService.TryParse(
            "https://youtu.be/dQw4w9WgXcQ?t=42&si=share-token#fragment",
            out var info));

        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", info.CanonicalUrl);
    }

    [Theory]
    [InlineData("https://chzzk.naver.com/clips/AbCdEf1234")]
    [InlineData("  https://chzzk.naver.com/clips/AbCdEf1234/?from=share#clip\u00a0")]
    public void ChzzkClipKeepsCaseAndUsesThePlatformTab(string url)
    {
        Assert.True(VideoUrlService.TryParse(url, out var info));
        Assert.Equal(VideoSource.Chzzk, info.Source);
        Assert.Equal("AbCdEf1234", info.VideoId);
        Assert.Equal("https://chzzk.naver.com/clips/AbCdEf1234", info.CanonicalUrl);
        Assert.False(ChzzkProbeService.TryGetVideoId(info.CanonicalUrl, out _));
        Assert.False(WebVideoUrlService.TryParse(url, out _));
    }

    [Theory]
    [InlineData("https://chzzk.naver.com/clips/")]
    [InlineData("https://chzzk.naver.com/clips/AbCdEf1234/extra")]
    [InlineData("https://chzzk.naver.com/clips/AbCdEf1234%2Fextra")]
    [InlineData("https://chzzk.naver.com.evil.test/clips/AbCdEf1234")]
    [InlineData("https://user:pass@chzzk.naver.com/clips/AbCdEf1234")]
    [InlineData("http://chzzk.naver.com/clips/AbCdEf1234")]
    [InlineData("https://chzzk.naver.com:444/clips/AbCdEf1234")]
    public void RejectsUnsafeClipUrls(string url) => Assert.False(VideoUrlService.TryParse(url, out _));
}
