using System.Text.Json;

namespace ChzzkDownloader.Services;

public sealed partial class YtDlpService
{
    internal static string DescribeWebMetadataError(JsonElement root, string error)
    {
        var reasons = new HashSet<string>();
        Inspect(root);
        if (reasons.Count == 0) return DescribeWebAnalysisError(error);
        var detail = string.IsNullOrWhiteSpace(error) ? string.Empty : CleanError(error, null, redactQueries: true);
        return string.Join("\n", reasons) + (string.IsNullOrWhiteSpace(detail) ? string.Empty : "\n" + detail);

        void Inspect(JsonElement entry)
        {
            if (entry.ValueKind != JsonValueKind.Object) return;
            if (ReadBoolean(entry, "has_drm"))
            {
                reasons.Add("DRM 보호가 표시된 미디어입니다. 이 앱에서는 다운로드를 지원하지 않습니다.");
                return;
            }
            if (ReadBoolean(entry, "is_live") || ReadString(entry, "live_status") is "is_live" or "is_upcoming")
            {
                reasons.Add("진행 중이거나 예정된 라이브입니다. 일반 영상 탭은 다시보기 파일만 지원합니다.");
                return;
            }
            if (entry.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
            {
                // Match ParseWebVideos' inspection limit; never traverse arbitrary nested playlists.
                foreach (var child in entries.EnumerateArray().Take(WebVideoLimit))
                    if (child.ValueKind == JsonValueKind.Object && !child.TryGetProperty("entries", out _)) Inspect(child);
                return;
            }
            var formats = entry.TryGetProperty("formats", out var array) && array.ValueKind == JsonValueKind.Array
                ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToArray()
                : new[] { entry };
            if (formats.Any(item => ReadBoolean(item, "has_drm")))
                reasons.Add("DRM 보호가 표시된 형식이 있습니다. 보호된 형식은 선택 목록에서 제외합니다.");
            if (formats.Any(item => !ReadBoolean(item, "has_drm") &&
                string.Equals(ReadString(item, "vcodec"), "none", StringComparison.OrdinalIgnoreCase) &&
                ReadString(item, "acodec") is { Length: > 0 } codec &&
                !string.Equals(codec, "none", StringComparison.OrdinalIgnoreCase)) &&
                !formats.Any(item => !ReadBoolean(item, "has_drm") && (HasVideo(item) || IsUnknownWebVideo(item))))
                reasons.Add("오디오 전용 스트림이 확인되었습니다. 현재 일반 영상 탭은 오디오 전용 저장을 지원하지 않습니다. 파일 손상 판정은 아닙니다.");
        }
    }

    internal static string DescribeWebAnalysisError(string error)
    {
        var detail = CleanError(error, null, redactQueries: true);
        var summary = detail.Contains("[StreamNest:HLS_ACCESS_DENIED]", StringComparison.Ordinal)
            ? "영상 주소는 찾았지만 HLS 재생목록 서버가 접근을 거부했습니다(HTTP 401/403). 브라우저 호환 요청을 적용해도 거부된 상태이며, 주소 미지원 오류가 아닙니다."
            : detail.Contains("[StreamNest:HLS_FETCH_FAILED]", StringComparison.Ordinal)
                ? "영상 주소는 찾았지만 HLS 재생목록을 읽지 못했습니다. 아래 서버 응답을 확인해주세요."
                : detail.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase)
                    ? "현재 추출기가 이 주소의 미디어 구조를 지원하지 않습니다. 파일 손상이나 로그인 실패로 확인된 것은 아닙니다."
                    : "지원 가능한 공개 영상 스트림을 확인하지 못했습니다. 아래 오류를 확인해주세요.";
        return summary + (string.IsNullOrWhiteSpace(detail) ? string.Empty : "\n" + detail);
    }
}
