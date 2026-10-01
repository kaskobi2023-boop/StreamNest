namespace ChzzkDownloader.Services;

public static class LoginSecurityPolicy
{
    private static readonly HashSet<string> GoogleAuthenticationHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts.google.com",
        "accounts.google.co.kr",
        "accounts.youtube.com",
        "consent.google.com",
        "myaccount.google.com"
    };

    private static readonly HashSet<string> AllowedTopLevelHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "chzzk.naver.com",
        "nid.naver.com",
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "music.youtube.com",
        "www.youtube-nocookie.com",
        "youtu.be",
        "accounts.youtube.com",
        "accounts.google.com",
        "accounts.google.co.kr",
        "consent.google.com",
        "google.com",
        "www.google.com",
        "google.co.kr",
        "www.google.co.kr",
        "myaccount.google.com",
        "g.co",
        "vod.sooplive.com",
        "login.sooplive.com",
        "member.sooplive.com",
        "www.sooplive.com",
        "auth.sooplive.com",
        "rplay.live",
        "www.rplay.live",
        "api.rplay.live",
        "auth.rplay.live"
    };

    private static readonly HashSet<string> AllowedCookieDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "naver.com",
        "chzzk.naver.com",
        "api.chzzk.naver.com",
        "apis.naver.com",
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "music.youtube.com",
        "www.youtube-nocookie.com",
        "google.com",
        "accounts.google.com",
        "googleusercontent.com",
        "sooplive.com",
        "vod.sooplive.com",
        "login.sooplive.com",
        "member.sooplive.com",
        "api.m.sooplive.com",
        "live.sooplive.com",
        "rplay.live",
        "www.rplay.live",
        "api.rplay.live"
    };

    private static readonly HashSet<string> ChzzkTopLevelHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "chzzk.naver.com",
        "nid.naver.com"
    };

    private static readonly HashSet<string> YouTubeTopLevelHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "music.youtube.com",
        "www.youtube-nocookie.com",
        "youtu.be",
        "accounts.youtube.com",
        "accounts.google.com",
        "accounts.google.co.kr",
        "consent.google.com",
        "google.com",
        "www.google.com",
        "google.co.kr",
        "www.google.co.kr",
        "myaccount.google.com",
        "g.co"
    };

    private static readonly HashSet<string> SoopTopLevelHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "vod.sooplive.com",
        "login.sooplive.com",
        "member.sooplive.com",
        "www.sooplive.com",
        "auth.sooplive.com"
    };

    private static readonly HashSet<string> RPlayTopLevelHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "rplay.live",
        "www.rplay.live",
        "api.rplay.live",
        "auth.rplay.live",
        "accounts.google.com",
        "accounts.google.co.kr",
        "consent.google.com",
        "google.com",
        "www.google.com",
        "google.co.kr",
        "www.google.co.kr",
        "myaccount.google.com",
        "g.co"
    };

    private static readonly HashSet<string> GoogleAuthenticationFrameHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts.google.com",
        "accounts.google.co.kr",
        "apis.google.com",
        "ssl.gstatic.com",
        "www.gstatic.com",
        "recaptcha.net",
        "www.recaptcha.net",
        "consent.google.com",
        "myaccount.google.com"
    };

    public static string CreateCanonicalVideoUrl(string input)
    {
        if (!VideoUrlService.TryParse(input, out var videoUrl))
            throw new ArgumentException("올바른 HTTPS 치지직·YouTube·SOOP·RPlay 영상 주소가 아닙니다.", nameof(input));

        return videoUrl.CanonicalUrl;
    }

    public static bool IsAllowedTopLevelUri(string? value, VideoSource? source = null)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        return source switch
        {
            VideoSource.Chzzk => ChzzkTopLevelHosts.Contains(uri.IdnHost),
            VideoSource.YouTube => YouTubeTopLevelHosts.Contains(uri.IdnHost),
            VideoSource.Soop => SoopTopLevelHosts.Contains(uri.IdnHost),
            VideoSource.RPlay => RPlayTopLevelHosts.Contains(uri.IdnHost),
            _ => AllowedTopLevelHosts.Contains(uri.IdnHost)
        };
    }

    public static bool IsAllowedFrameUri(string? value, VideoSource? source = null)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        if (IsAllowedTopLevelUri(value, source))
            return true;

        if (source is VideoSource.YouTube or VideoSource.RPlay or null)
        {
            return GoogleAuthenticationFrameHosts.Contains(uri.IdnHost);
        }

        return false;
    }

    public static bool IsGoogleAuthenticationUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               uri.IsDefaultPort &&
               string.IsNullOrEmpty(uri.UserInfo) &&
               GoogleAuthenticationHosts.Contains(uri.IdnHost);
    }

    public static bool TryGetExternalHttpsUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            parsed.IsDefaultPort &&
            string.IsNullOrEmpty(parsed.UserInfo))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    public static string GetDisplayOrigin(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : "주소 확인 불가";
    }

    public static string GetDiagnosticOrigin(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return "<invalid>";

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return $"{uri.Scheme}:<redacted>";

        return new UriBuilder(uri.Scheme, uri.IdnHost, uri.Port)
            .Uri.GetLeftPart(UriPartial.Authority);
    }

    public static bool IsAllowedCookieDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().TrimStart('.').TrimEnd('.');
        return AllowedCookieDomains.Contains(normalized);
    }

    public static bool IsAllowedCookieDomain(string? value, VideoSource source)
    {
        if (!IsAllowedCookieDomain(value)) return false;
        var domain = value!.Trim().TrimStart('.').TrimEnd('.');
        return source switch
        {
            VideoSource.Soop => domain.Equals("sooplive.com", StringComparison.OrdinalIgnoreCase) || domain.EndsWith(".sooplive.com", StringComparison.OrdinalIgnoreCase),
            VideoSource.Chzzk => domain.Equals("naver.com", StringComparison.OrdinalIgnoreCase) || domain.EndsWith(".naver.com", StringComparison.OrdinalIgnoreCase),
            VideoSource.YouTube => domain.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || domain.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
                                  domain.Equals("google.com", StringComparison.OrdinalIgnoreCase) || domain.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase) ||
                                  domain.Equals("googleusercontent.com", StringComparison.OrdinalIgnoreCase) || domain.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase),
            VideoSource.RPlay => domain.Equals("rplay.live", StringComparison.OrdinalIgnoreCase) || domain.EndsWith(".rplay.live", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
