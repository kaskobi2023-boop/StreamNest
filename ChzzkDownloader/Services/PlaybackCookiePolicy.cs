using ChzzkDownloader.Models;

namespace ChzzkDownloader.Services;

/// <summary>One policy for browser collection and temporary engine export.</summary>
public static class PlaybackCookiePolicy
{
    private static readonly HashSet<string> SoopTicketNames = new(StringComparer.Ordinal)
    {
        "AuthTicket", "UserTicket", "BbsTicket", "BbsSaveTicket", "RDB", "isBbs"
    };

    public static bool IsSoopTicket(BrowserCookie cookie) =>
        LoginSecurityPolicy.IsAllowedCookieDomain(cookie.Domain, VideoSource.Soop) &&
        SoopTicketNames.Contains(cookie.Name);

    private static readonly HashSet<string> RPlayTrackingCookieNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "guest_device_id",
        "fam8_xuid",
        "exo_click_id",
        "x_click_id",
        "gclid",
        "rdt_cid",
        "arrow_adspot_id",
        "advision_suid"
    };

    private static readonly HashSet<string> RPlayAuthCookieNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_AUTHORIZATION_",
        "Authorization",
        "token",
        "access_token"
    };

    public static bool IsRPlayAuthenticationCookie(BrowserCookie cookie) =>
        LoginSecurityPolicy.IsAllowedCookieDomain(cookie.Domain, VideoSource.RPlay) &&
        !string.IsNullOrWhiteSpace(cookie.Value) &&
        RPlayAuthCookieNames.Contains(cookie.Name);

    public static bool CanExport(BrowserCookie cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie.Name) || string.IsNullOrWhiteSpace(cookie.Value) ||
            !LoginSecurityPolicy.IsAllowedCookieDomain(cookie.Domain) ||
            cookie.Expires > 0 && cookie.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
        // SOOP currently issues login tickets without the Secure attribute.
        // Permit only these named SOOP cookies; analytics cookies are excluded.
        if (LoginSecurityPolicy.IsAllowedCookieDomain(cookie.Domain, VideoSource.Soop)) return IsSoopTicket(cookie);
        if (LoginSecurityPolicy.IsAllowedCookieDomain(cookie.Domain, VideoSource.RPlay))
            return IsRPlayAuthenticationCookie(cookie) || RPlaySessionService.IsContextCookie(cookie);
        return cookie.IsSecure;
    }

    public static bool CanCollect(BrowserCookie cookie, VideoSource source) =>
        LoginSecurityPolicy.IsAllowedCookieDomain(cookie.Domain, source) && CanExport(cookie);

    public static bool HasSoopLoginTickets(IEnumerable<BrowserCookie> cookies)
    {
        var names = cookies.Where(cookie => CanCollect(cookie, VideoSource.Soop)).Select(cookie => cookie.Name).ToHashSet(StringComparer.Ordinal);
        return names.Contains("AuthTicket") && names.Contains("UserTicket");
    }

    public static bool HasRPlayLoginSession(IEnumerable<BrowserCookie> cookies) =>
        cookies.Any(cookie => CanCollect(cookie, VideoSource.RPlay) && IsRPlayAuthenticationCookie(cookie) &&
                              RPlaySessionService.TryGetTokenExpiry(cookie.Value, out _));
}
