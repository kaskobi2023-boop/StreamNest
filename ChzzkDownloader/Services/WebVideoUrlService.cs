using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace ChzzkDownloader.Services;

/// <summary>Input validation for the separate, cookie-free public web mode.</summary>
public static partial class WebVideoUrlService
{
    public static bool TryParse(string? input, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps || !parsed.IsDefaultPort ||
            !string.IsNullOrEmpty(parsed.UserInfo))
            return false;
        var host = parsed.IdnHost.TrimEnd('.');
        if (VideoUrlService.IsYouTubeHost(host) || host.Equals("chzzk.naver.com", StringComparison.OrdinalIgnoreCase))
            return false;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            if (!IsPublicAddress(address)) return false;
        }
        else if (Uri.CheckHostName(host) != UriHostNameType.Dns || !host.Contains('.') ||
                 new[] { "localhost", "local", "internal", "lan", "home", "test", "invalid" }
                     .Any(suffix => host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase)))
            return false;
        // Keep query parameters: signed media URLs may need them. Fragments are not sent to servers.
        uri = new UriBuilder(parsed) { Fragment = string.Empty }.Uri;
        return true;
    }

    public static async Task<Uri> ValidateAsync(string input, CancellationToken token)
    {
        if (!TryParse(input, out var uri))
            throw new ArgumentException("일반 영상 탭에는 공개 HTTPS 웹페이지·MP4·HLS 주소를 입력해주세요. 치지직·YouTube는 전용 탭을 사용해주세요.");
        var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, token);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new ArgumentException("로컬·사설 네트워크 주소는 일반 영상 탭에서 사용할 수 없습니다.");
        return uri;
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !(bytes[0] is 0 or 10 or 127 || bytes[0] >= 224 ||
                     bytes[0] == 169 && bytes[1] == 254 ||
                     bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                     bytes[0] == 192 && (bytes[1] is 0 or 168 || bytes[1] == 88 && bytes[2] == 99) ||
                     bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
                     bytes[0] == 198 && (bytes[1] is 18 or 19 || bytes[1] == 51 && bytes[2] == 100) ||
                     bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xe0) == 0x20 &&
               !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
    }

    public static string RedactQueries(string text) => UrlRegex().Replace(text, match =>
        Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Path) + (uri.Query.Length > 0 ? "?[주소 매개변수 숨김]" : string.Empty)
            : "[주소]");

    [GeneratedRegex("https?://[^\\s\"'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
