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
    [InlineData("https://user:secret@chzzk.naver.com/video/14471813")]
    public void TryParse_RejectsUnsupportedOrUnsafeUrls(string url)
    {
        Assert.False(VideoUrlService.TryParse(url, out _));
    }

    [Fact]
    public void TryParse_StillRecognizesChzzkUrls()
    {
        Assert.True(VideoUrlService.TryParse(
            "https://chzzk.naver.com/video/14471813?from=share",
            out var info));

        Assert.Equal(VideoSource.Chzzk, info.Source);
        Assert.Equal("14471813", info.VideoId);
    }

    [Fact]
    public void TryParse_CanonicalizesYouTubeShareUrl()
    {
        Assert.True(VideoUrlService.TryParse(
            "https://youtu.be/dQw4w9WgXcQ?t=42&si=share-token#fragment",
            out var info));

        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", info.CanonicalUrl);
    }
}
