using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class SoopSessionTests
{
    private static BrowserCookie Cookie(string name, string domain = ".sooplive.com", bool secure = false, double expires = 0) =>
        new(name, "fixture-value", domain, "/", secure, true, expires);

    [Theory]
    [InlineData("AuthTicket")]
    [InlineData("UserTicket")]
    [InlineData("BbsTicket")]
    [InlineData("BbsSaveTicket")]
    [InlineData("RDB")]
    [InlineData("isBbs")]
    public void RequiredSoopTicketsDoNotNeedSecureAttribute(string name)
    {
        var cookie = Cookie(name);
        Assert.True(PlaybackCookiePolicy.CanCollect(cookie, VideoSource.Soop));
        Assert.True(PlaybackCookiePolicy.CanExport(cookie));
        Assert.False(PlaybackCookiePolicy.CanCollect(cookie, VideoSource.YouTube));
        Assert.False(PlaybackCookiePolicy.CanCollect(cookie, VideoSource.Chzzk));
    }

    [Fact]
    public void RejectsAnalyticsSpoofedDomainsExpiredTicketsAndOtherNonSecureCookies()
    {
        Assert.False(PlaybackCookiePolicy.CanExport(Cookie("_au3rd", secure: true)));
        Assert.False(PlaybackCookiePolicy.CanExport(Cookie("AuthTicket", "sooplive.com.evil.test")));
        Assert.False(PlaybackCookiePolicy.CanExport(Cookie("AuthTicket", expires: 1)));
        Assert.False(PlaybackCookiePolicy.CanExport(Cookie("NID_SES", ".naver.com")));
        Assert.False(PlaybackCookiePolicy.CanExport(Cookie("SID", ".youtube.com")));
        Assert.True(PlaybackCookiePolicy.CanExport(Cookie("SID", ".youtube.com", true)));
    }

    [Fact]
    public void SessionConfirmationRequiresLiveAuthenticationTicketsNotJustAnyCookie()
    {
        Assert.True(PlaybackCookiePolicy.HasSoopLoginTickets([Cookie("AuthTicket"), Cookie("UserTicket")]));
        Assert.False(PlaybackCookiePolicy.HasSoopLoginTickets([Cookie("_au3rd", secure: true)]));
        Assert.False(PlaybackCookiePolicy.HasSoopLoginTickets([Cookie("AuthTicket")]));
        Assert.False(PlaybackCookiePolicy.HasSoopLoginTickets([Cookie("AuthTicket", expires: 1), Cookie("UserTicket")]));
    }

    [Fact]
    public async Task TemporaryTicketExportIsHttpsOnlyAndDoesNotMutateOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(), "StreamNest-SoopCookie-" + Guid.NewGuid().ToString("N"));
        var service = new CookieFileService(root);
        var ticket = Cookie("AuthTicket");
        string? path = null;
        try
        {
            path = await service.CreateAsync([ticket, Cookie("UserTicket"), Cookie("_au3rd", secure: true)]);
            Assert.NotNull(path);
            var lines = (await File.ReadAllLinesAsync(path!)).Where(line => line.Contains('\t')).ToArray();
            Assert.Equal(2, lines.Length);
            Assert.All(lines, line => Assert.Equal("TRUE", line.Split('\t')[3]));
            Assert.False(ticket.IsSecure);
            Assert.DoesNotContain(lines, line => line.Contains("_au3rd"));
        }
        finally
        {
            service.Delete(path);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
