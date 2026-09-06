using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class SessionCookieStoreTests
{
    [Fact]
    public void Clear_RemovesChzzkAndYouTubeCookiesTogether()
    {
        var store = new SessionCookieStore();
        var cookie = new BrowserCookie("SID", "secret", ".youtube.com", "/", true, true, 0);
        store.Set(VideoSource.YouTube, [cookie]);
        store.Set(VideoSource.Chzzk, [cookie with { Domain = ".naver.com" }]);

        store.Clear();

        Assert.False(store.HasAny);
        Assert.Empty(store.Get(VideoSource.YouTube));
        Assert.Empty(store.Get(VideoSource.Chzzk));
    }

    [Fact]
    public void Get_KeepsServiceCookiesSeparated()
    {
        var store = new SessionCookieStore();
        var youtube = new BrowserCookie("SID", "youtube", ".youtube.com", "/", true, true, 0);
        var chzzk = new BrowserCookie("NID_SES", "chzzk", ".naver.com", "/", true, true, 0);
        store.Set(VideoSource.YouTube, [youtube]);
        store.Set(VideoSource.Chzzk, [chzzk]);

        Assert.Equal("youtube", Assert.Single(store.Get(VideoSource.YouTube)).Value);
        Assert.Equal("chzzk", Assert.Single(store.Get(VideoSource.Chzzk)).Value);
    }
}
