using System.Text.RegularExpressions;

namespace ChzzkDownloader.Services;

public enum VideoSource
{
    Chzzk,
    YouTube,
    Soop,
    RPlay
}

public sealed record VideoUrlInfo(VideoSource Source, string VideoId, string CanonicalUrl);

/// <summary>
/// Validates URLs before they reach yt-dlp. Keeping this allow-list separate from
/// the downloader prevents an arbitrary URL from being passed to the external tool.
/// </summary>
public static partial class VideoUrlService
{
    private static readonly HashSet<string> SupportedYouTubeHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "music.youtube.com",
        "www.youtube-nocookie.com",
        "youtu.be"
    };

    public static bool TryParse(string input, out VideoUrlInfo info)
    {
        info = null!;
        if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        if (ChzzkProbeService.TryGetVideoId(uri.AbsoluteUri, out var chzzkVideoId))
        {
            info = new VideoUrlInfo(
                VideoSource.Chzzk,
                chzzkVideoId,
                $"https://chzzk.naver.com/video/{chzzkVideoId}");
            return true;
        }

        if (IsSoopVodHost(uri.IdnHost))
        {
            var match = SoopVideoPathRegex().Match(uri.AbsolutePath);
            if (!match.Success) return false;
            var id = match.Groups[1].Value;
            info = new VideoUrlInfo(VideoSource.Soop, id, $"https://vod.sooplive.com/player/{id}");
            return true;
        }

        if (uri.IdnHost.Equals("chzzk.naver.com", StringComparison.OrdinalIgnoreCase))
        {
            var match = ChzzkClipPathRegex().Match(uri.AbsolutePath);
            if (!match.Success) return false;
            var id = match.Groups[1].Value;
            info = new VideoUrlInfo(VideoSource.Chzzk, id, $"https://chzzk.naver.com/clips/{id}");
            return true;
        }

        if (IsRPlayHost(uri.IdnHost))
        {
            var match = RPlayVideoPathRegex().Match(uri.AbsolutePath);
            if (!match.Success) return false;
            var id = match.Groups[1].Value;
            info = new VideoUrlInfo(VideoSource.RPlay, id, $"https://rplay.live/play/{id}");
            return true;
        }

        if (!TryGetYouTubeVideoId(uri, out var youtubeVideoId))
            return false;

        info = new VideoUrlInfo(
            VideoSource.YouTube,
            youtubeVideoId,
            $"https://www.youtube.com/watch?v={youtubeVideoId}");
        return true;
    }

    public static bool IsYouTubeHost(string host) =>
        !string.IsNullOrWhiteSpace(host) && SupportedYouTubeHosts.Contains(host);

    public static bool IsSoopVodHost(string host) =>
        host.Equals("vod.sooplive.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("vod.afreecatv.com", StringComparison.OrdinalIgnoreCase);

    public static bool IsRPlayHost(string host) =>
        host.Equals("rplay.live", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("www.rplay.live", StringComparison.OrdinalIgnoreCase);

    public static string GetDisplayName(VideoSource source) => source switch
    {
        VideoSource.Chzzk => "치지직",
        VideoSource.YouTube => "YouTube",
        VideoSource.Soop => "SOOP(숲)",
        VideoSource.RPlay => "웹 영상",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    // A Catch is the same individual VOD number; the suffix selects the website's
    // short-video UI. Canonicalize to the existing single-video extraction path.
    [GeneratedRegex("^/(?:player|PLAYER/STATION)/([0-9]+)(?:/catch)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex SoopVideoPathRegex();

    [GeneratedRegex("^/clips/([A-Za-z0-9_-]{6,80})/?$")]
    private static partial Regex ChzzkClipPathRegex();

    [GeneratedRegex("^/play/([a-zA-Z0-9_-]+)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex RPlayVideoPathRegex();

    private static bool TryGetYouTubeVideoId(Uri uri, out string videoId)
    {
        videoId = string.Empty;
        var host = uri.IdnHost;
        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var segment = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return TryNormalizeVideoId(segment, out videoId);
        }

        if (!IsYouTubeHost(host) || host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
            return false;

        var path = uri.AbsolutePath.Trim('/');
        if (path.Equals("watch", StringComparison.OrdinalIgnoreCase))
            return TryNormalizeVideoId(GetQueryValue(uri, "v"), out videoId);

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            (parts[0].Equals("shorts", StringComparison.OrdinalIgnoreCase) ||
             parts[0].Equals("embed", StringComparison.OrdinalIgnoreCase) ||
             parts[0].Equals("live", StringComparison.OrdinalIgnoreCase)))
            return TryNormalizeVideoId(parts[1], out videoId);

        return false;
    }

    private static string? GetQueryValue(Uri uri, string key)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 &&
                Uri.UnescapeDataString(parts[0]).Equals(key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(parts[1]);
        }

        return null;
    }

    private static bool TryNormalizeVideoId(string? candidate, out string videoId)
    {
        videoId = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var value = candidate.Trim();
        if (!YouTubeVideoIdRegex().IsMatch(value))
            return false;

        videoId = value;
        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{6,}$")]
    private static partial Regex YouTubeVideoIdRegex();
}
