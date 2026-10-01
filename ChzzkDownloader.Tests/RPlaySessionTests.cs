using System.Text;
using System.Text.Json;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class RPlaySessionTests
{
    public static string Token(object payload) => Encode(new { alg = "HS256", typ = "JWT" }) + "." + Encode(payload) + ".test_signature";
    private static string Encode(object value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Snapshot(string token, string oid = "0123456789abcdef01234567", string loginType = "google") =>
        JsonSerializer.Serialize(new { token, userOid = oid, loginType });

    [Fact]
    public async Task CurrentAccountExportsExactlyOneTokenAndMatchingContext()
    {
        var token = Token(new { exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() });
        var cookies = RPlaySessionService.ParseSnapshot(Snapshot(token));
        Assert.Equal(3, cookies.Count);
        Assert.Single(cookies, PlaybackCookiePolicy.IsRPlayAuthenticationCookie);
        Assert.All(cookies, c => Assert.Equal("api.rplay.live", c.Domain));
        Assert.True(PlaybackCookiePolicy.HasRPlayLoginSession(cookies));
        var folder = Path.Combine(Path.GetTempPath(), "RPlaySessionTests-" + Guid.NewGuid());
        var service = new CookieFileService(folder);
        var path = await service.CreateAsync(cookies, VideoSource.RPlay);
        try
        {
            var text = await File.ReadAllTextAsync(path!);
            Assert.Contains(RPlaySessionService.RequestorCookie, text);
            Assert.Contains(RPlaySessionService.LoginTypeCookie, text);
            Assert.DoesNotContain("\tTRUE\t/", text); // No wildcard domain export.
            Assert.Null(await service.CreateAsync(cookies, VideoSource.YouTube));
        }
        finally { service.Delete(path); Directory.Delete(folder); }
    }

    [Fact]
    public void ExpiredTokenIsRejectedEvenWhenCookieItselfIsASessionCookie()
    {
        var token = Token(new { exp = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds() });
        Assert.Empty(RPlaySessionService.ParseSnapshot(Snapshot(token)));
        Assert.False(PlaybackCookiePolicy.HasRPlayLoginSession([new("_AUTHORIZATION_", token, "api.rplay.live", "/", true, true, 0)]));
    }

    [Fact]
    public void LegacyTokenWithoutExpiryIsAcceptedOnlyFromACompleteAccountSnapshot()
    {
        var token = Token(new { eml = "synthetic@example.invalid", dat = "2026-09-13" });
        Assert.Equal(3, RPlaySessionService.ParseSnapshot(Snapshot(token)).Count);
        Assert.Empty(RPlaySessionService.ParseSnapshot(Snapshot(token, loginType: "")));
        Assert.Empty(RPlaySessionService.ParseSnapshot(Snapshot(token, oid: "")));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"token\":123}")]
    public void IncompleteSnapshotsAreRejected(string json) => Assert.Empty(RPlaySessionService.ParseSnapshot(json));

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...")]
    public void MalformedTokensAreRejected(string token) => Assert.Empty(RPlaySessionService.ParseSnapshot(Snapshot(token)));

    [Theory]
    [InlineData("https://rplay.live/home", true)]
    [InlineData("https://www.rplay.live/", true)]
    [InlineData("https://accounts.google.com/", false)]
    [InlineData("https://other.rplay.live/", false)]
    [InlineData("http://rplay.live/", false)]
    [InlineData("https://rplay.live:8443/", false)]
    public void SessionIsReadOnlyOnTheWebsiteOrigin(string url, bool allowed) => Assert.Equal(allowed, RPlaySessionService.IsSessionPage(url));
}
