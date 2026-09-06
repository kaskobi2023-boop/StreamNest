using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class ThumbnailServiceTests
{
    [Theory]
    [InlineData("https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg")]
    [InlineData("http://127.0.0.1:54321/thumbnail.png")]
    public void TryCreateAllowedUri_AcceptsHttpsAndLoopbackHttp(string value)
    {
        Assert.True(ThumbnailService.TryCreateAllowedUri(value, out _));
    }

    [Theory]
    [InlineData("http://example.com/thumbnail.png")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("https://example.com:444/thumbnail.png")]
    [InlineData("not a url")]
    public void TryCreateAllowedUri_RejectsUnsafeUris(string value)
    {
        Assert.False(ThumbnailService.TryCreateAllowedUri(value, out _));
    }
}
