namespace ChzzkDownloader.Services;

public static class LoginSecurityPolicy
{
    private static readonly HashSet<string> GoogleAuthenticationHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts.google.com",
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
        "consent.google.com",
        "google.com",
        "www.google.com",
        "myaccount.google.com",
        "g.co"
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
        "googleusercontent.com"
    };

    public static string CreateCanonicalVideoUrl(string input)
    {
        if (!VideoUrlService.TryParse(input, out var videoUrl))
            throw new ArgumentException("올바른 HTTPS 치지직 또는 YouTube 영상 주소가 아닙니다.", nameof(input));

        return videoUrl.CanonicalUrl;
    }

    public static bool IsAllowedTopLevelUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               uri.IsDefaultPort &&
               AllowedTopLevelHosts.Contains(uri.IdnHost);
    }

    public static bool IsGoogleAuthenticationUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               uri.IsDefaultPort &&
               GoogleAuthenticationHosts.Contains(uri.IdnHost);
    }

    public static bool TryGetExternalHttpsUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            parsed.IsDefaultPort)
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

    public static bool IsAllowedCookieDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().TrimStart('.').TrimEnd('.');
        return AllowedCookieDomains.Contains(normalized);
    }
}
