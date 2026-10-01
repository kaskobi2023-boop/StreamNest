using System.Text.Json;
using System.Text.RegularExpressions;
using ChzzkDownloader.Models;

namespace ChzzkDownloader.Services;

public sealed partial class YtDlpService
{
    internal static VideoInfo ParseSoopVideo(JsonElement root)
    {
        var multipart = root.TryGetProperty("entries", out var entries);
        if (multipart && (ReadString(root, "_type") != "multi_video" || entries.ValueKind != JsonValueKind.Array))
            throw new YtDlpException("SOOP 모음 주소는 지원하지 않습니다. 개별 VOD·클립의 player 주소를 입력해주세요.");
        var parts = multipart ? entries.EnumerateArray().ToArray() : [root];
        if (parts.Length == 0 || parts.Any(part => part.ValueKind != JsonValueKind.Object))
            throw new YtDlpException("SOOP 영상의 전체 구간을 확인하지 못했습니다. 시청 권한과 영상 상태를 확인해주세요.");

        var rootId = ReadString(root, "id") ?? "";
        var ids = parts.Select(part => ReadString(part, "id") ?? "").ToArray();
        if (!Regex.IsMatch(rootId, "^[A-Za-z0-9_-]+$"))
            throw new YtDlpException("SOOP 영상 식별자가 올바르지 않습니다.");
        if (ids.Any(id => !Regex.IsMatch(id, "^[A-Za-z0-9_-]+$")) || ids.Distinct().Count() != ids.Length)
            throw new YtDlpException("SOOP 영상 구간 식별자가 올바르지 않습니다. 다시 분석해주세요.");
        if (parts.Any(part => ReadBoolean(part, "is_live") || ReadBoolean(part, "has_drm") ||
                              ReadString(part, "live_status") is "is_live" or "is_upcoming" || part.TryGetProperty("entries", out _)))
            throw new YtDlpException("SOOP 실시간 방송·DRM 영상은 지원하지 않습니다. 다시보기나 클립 주소를 입력해주세요.");

        // SOOP direct MP4 and some HLS renditions omit codec/height metadata.
        // Preserve the auto option; the common final verifier checks the file.
        var videos = parts.Select(part => ParseVideoInfo(part, allowUnknownWebVideo: true)).ToArray();
        if (videos.Any(video => video.Formats.Count == 0))
            throw new YtDlpException("SOOP 영상 중 다운로드할 수 없는 구간이 있습니다. 일부만 완료 처리하지 않았습니다.");
        var durations = parts.Select(part => ReadDouble(part, "duration")).ToArray();
        double? duration = durations.All(value => value is > 0 && double.IsFinite(value.Value))
            ? durations.Sum(value => value!.Value) : ReadDouble(root, "duration");
        var commonHeights = videos[0].Formats.Select(option => option.Height)
            .Where(height => videos.All(video => video.Formats.Any(option => option.Height == height))).ToArray();
        if (commonHeights.Length == 0)
            throw new YtDlpException("SOOP 전체 구간에 공통인 화질이 없습니다. 서로 다른 화질을 섞어 저장하지 않았습니다.");

        var options = commonHeights.Select(height =>
        {
            var matching = videos.Select(video => video.Formats.First(option => option.Height == height)).ToArray();
            long? bytes = matching.All(option => option.EstimatedBytes is > 0)
                ? matching.Sum(option => option.EstimatedBytes!.Value) : null;
            return new VideoFormatOption
            {
                Height = height,
                Fps = matching[0].Fps,
                Label = (height is > 0 ? $"{height}p" : "원본 화질 (자동)") +
                        (parts.Length > 1 ? $" · 전체 {parts.Length}개 구간" : "") +
                        (bytes is > 0 ? $" · 약 {FormatBytes(bytes.Value)}" : ""),
                EstimatedBytes = bytes,
                ExpectedDurationSeconds = duration,
                ExpectsAudio = matching.Any(option => option.ExpectsAudio),
                ExpectedVideoId = ReadString(root, "id"),
                Selector = height is > 0 ? $"bestvideo[height={height}]+bestaudio/best[height={height}]" : "bestvideo+bestaudio/best",
                SoopPartIds = ids
            };
        }).ToArray();
        return new VideoInfo
        {
            Id = ReadString(root, "id") ?? ids[0],
            Title = ReadString(root, "title") ?? videos[0].Title,
            ChannelName = ReadString(root, "uploader") ?? videos[0].ChannelName,
            ThumbnailUrl = ReadString(root, "thumbnail") ?? videos[0].ThumbnailUrl,
            DurationSeconds = duration is > 0 ? (long)duration.Value : null,
            PartCount = parts.Length,
            Formats = options
        };
    }
}
