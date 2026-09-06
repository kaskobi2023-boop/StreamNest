using ChzzkDownloader.Models;
using Microsoft.Web.WebView2.Core;

namespace ChzzkDownloader.Services;

public static class WebViewCookieCollector
{
    private static readonly string[] YouTubeOrigins =
    [
        "https://youtube.com/",
        "https://www.youtube.com/",
        "https://m.youtube.com/",
        "https://accounts.youtube.com/",
        "https://accounts.google.com/",
        "https://www.google.com/",
        "https://consent.google.com/"
    ];

    private static readonly string[] ChzzkOrigins =
    [
        "https://chzzk.naver.com/",
        "https://api.chzzk.naver.com/",
        "https://apis.naver.com/"
    ];

    private static readonly HashSet<string> YouTubeAuthenticationCookieNames = new(
        [
            "LOGIN_INFO",
            "SID",
            "HSID",
            "SSID",
            "APISID",
            "SAPISID",
            "__Secure-1PSID",
            "__Secure-3PSID"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> GetOrigins(VideoSource source) =>
        source == VideoSource.YouTube ? YouTubeOrigins : ChzzkOrigins;

    public static bool HasAuthenticatedYouTubeSession(IEnumerable<BrowserCookie> cookies) =>
        cookies.Any(cookie => YouTubeAuthenticationCookieNames.Contains(cookie.Name));

    public static async Task<IReadOnlyList<BrowserCookie>> CollectAsync(
        CoreWebView2 coreWebView,
        VideoSource source)
    {
        ArgumentNullException.ThrowIfNull(coreWebView);
        var collected = new Dictionary<string, BrowserCookie>(StringComparer.OrdinalIgnoreCase);

        foreach (var origin in GetOrigins(source))
        {
            var cookies = await coreWebView.CookieManager.GetCookiesAsync(origin);
            foreach (var cookie in cookies)
            {
                if (!cookie.IsSecure ||
                    string.IsNullOrWhiteSpace(cookie.Name) ||
                    string.IsNullOrWhiteSpace(cookie.Value) ||
                    !LoginSecurityPolicy.IsAllowedCookieDomain(cookie.Domain) ||
                    (!cookie.IsSession && cookie.Expires.ToUniversalTime() <= DateTime.UtcNow))
                    continue;

                var key = $"{cookie.Name}\n{cookie.Domain}\n{cookie.Path}";
                var expires = cookie.IsSession
                    ? 0
                    : new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds();
                collected[key] = new BrowserCookie(
                    cookie.Name,
                    cookie.Value,
                    cookie.Domain,
                    cookie.Path,
                    cookie.IsSecure,
                    cookie.IsHttpOnly,
                    expires);
            }
        }

        return collected.Values.ToList();
    }
}
