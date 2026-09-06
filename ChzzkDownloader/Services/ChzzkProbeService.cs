using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChzzkDownloader.Models;

namespace ChzzkDownloader.Services;

public sealed partial class ChzzkProbeService
{
    private readonly HttpClient _httpClient;

    public ChzzkProbeService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/138 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://chzzk.naver.com/");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
    }

    public static bool TryGetVideoId(string input, out string videoId)
    {
        videoId = string.Empty;
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.Equals(uri.IdnHost, "chzzk.naver.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var match = VideoPathRegex().Match(uri.AbsolutePath);
        if (!match.Success)
            return false;

        videoId = match.Groups[1].Value;
        return true;
    }

    public async Task<ProbeResult> ProbeAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!TryGetVideoId(url, out var videoId))
        {
            return new ProbeResult
            {
                IsValidUrl = false,
                Error = "치지직 다시보기 주소를 확인해주세요."
            };
        }

        try
        {
            using var response = await GetApiResponseAsync(videoId, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ProbeResult
                {
                    IsValidUrl = true,
                    VideoId = videoId,
                    Error = "삭제되었거나 존재하지 않는 영상입니다."
                };
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("content", out var content) ||
                content.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return new ProbeResult
                {
                    IsValidUrl = true,
                    VideoId = videoId,
                    Error = "영상 정보를 가져오지 못했습니다."
                };
            }

            var isAdult = GetBoolean(content, "adult");
            var adultStatus = GetString(content, "userAdultStatus");
            var playbackVideoId = GetString(content, "videoId");
            var inKey = GetString(content, "inKey");
            var rewindJson = GetString(content, "liveRewindPlaybackJson");
            var needsAdultLogin = isAdult && string.Equals(adultStatus, "NOT_LOGIN_USER", StringComparison.OrdinalIgnoreCase);
            var playbackMissing = string.IsNullOrWhiteSpace(playbackVideoId) &&
                                  string.IsNullOrWhiteSpace(inKey) &&
                                  string.IsNullOrWhiteSpace(rewindJson);
            var requiresLogin = needsAdultLogin || (isAdult && playbackMissing);

            return new ProbeResult
            {
                IsValidUrl = true,
                VideoId = videoId,
                Title = GetString(content, "videoTitle") ?? GetString(content, "title"),
                ChannelName = GetNestedString(content, "channel", "channelName"),
                DurationSeconds = GetLong(content, "duration"),
                ThumbnailUrl = GetString(content, "thumbnailImageUrl"),
                IsAdult = isAdult,
                RequiresLogin = requiresLogin,
                LoginReason = requiresLogin
                    ? "이 영상은 네이버 로그인 및 성인 인증이 필요합니다."
                    : null
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ProbeResult
            {
                IsValidUrl = true,
                VideoId = videoId,
                Error = $"치지직 연결 실패: {exception.Message}"
            };
        }
    }

    private async Task<HttpResponseMessage> GetApiResponseAsync(string videoId, CancellationToken cancellationToken)
    {
        var v3 = await _httpClient.GetAsync(
            $"https://api.chzzk.naver.com/service/v3/videos/{videoId}", cancellationToken);
        if (v3.StatusCode != HttpStatusCode.NotFound)
            return v3;

        v3.Dispose();
        return await _httpClient.GetAsync(
            $"https://api.chzzk.naver.com/service/v2/videos/{videoId}", cancellationToken);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? GetNestedString(JsonElement element, string objectName, string propertyName) =>
        element.TryGetProperty(objectName, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? GetString(nested, propertyName)
            : null;

    private static bool GetBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False && property.GetBoolean();

    private static long? GetLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
            return null;
        if (property.TryGetInt64(out var value))
            return value;
        return property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out value)
            ? value
            : null;
    }

    [GeneratedRegex(@"^/video/(\d+)(?:/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex VideoPathRegex();
}
