namespace ChzzkDownloader.Services;

public enum YtDlpErrorKind
{
    Authentication,
    Storage,
    Tool,
    Extractor,
    Network,
    Unavailable,
    Unknown,
    AccessDenied,
    FormatUnavailable
}

public static class YtDlpErrorClassifier
{
    private static readonly string[] AuthenticationMarkers =
        ["login", "log in", "sign in", "adult", "age-restricted", "members only", "members-only", "available to members", "members of this channel", "confirm you're not a bot", "confirm you're not a robot", "authentication", "로그인", "인증"];

    private static readonly string[] StorageMarkers =
        ["no space left", "disk full", "not enough space", "[errno 13]", "[winerror 5]", "저장 공간", "공간이 부족"];

    private static readonly string[] ToolMarkers =
        ["ffmpeg not found", "ffprobe not found", "unable to locate ffmpeg", "도구를 찾을 수 없습니다"];

    private static readonly string[] ExtractorMarkers =
        ["extractor error", "keyerror(", "please report this issue"];

    private static readonly string[] AccessDeniedMarkers =
        ["http error 403", "403 forbidden", "403: forbidden"];

    private static readonly string[] FormatUnavailableMarkers =
        ["requested format is not available", "no video formats found"];

    private static readonly string[] NetworkMarkers =
        ["timed out", "timeout", "connection", "network", "unable to download", "remote host", "연결 실패"];

    private static readonly string[] UnavailableMarkers =
        ["private video", "video unavailable", "removed", "deleted", "not available", "비공개", "삭제"];

    public static YtDlpErrorKind Classify(string message)
    {
        if (ContainsAny(message, AuthenticationMarkers))
            return YtDlpErrorKind.Authentication;
        if (ContainsAny(message, StorageMarkers))
            return YtDlpErrorKind.Storage;
        if (ContainsAny(message, ToolMarkers))
            return YtDlpErrorKind.Tool;
        if (ContainsAny(message, FormatUnavailableMarkers))
            return YtDlpErrorKind.FormatUnavailable;
        if (ContainsAny(message, AccessDeniedMarkers))
            return YtDlpErrorKind.AccessDenied;
        if (ContainsAny(message, UnavailableMarkers))
            return YtDlpErrorKind.Unavailable;
        if (ContainsAny(message, ExtractorMarkers))
            return YtDlpErrorKind.Extractor;
        if (ContainsAny(message, NetworkMarkers))
            return YtDlpErrorKind.Network;
        return YtDlpErrorKind.Unknown;
    }

    public static string ToUserMessage(YtDlpErrorKind kind, string operation) => kind switch
    {
        YtDlpErrorKind.AccessDenied => "서버가 영상 요청을 거부했습니다(HTTP 403). yt-dlp 버전을 확인하고 영상 정보를 다시 분석해주세요. 계속 실패하면 접근 권한과 상세 작업 기록을 확인해주세요.",
        YtDlpErrorKind.FormatUnavailable => "요청한 화질을 현재 사용할 수 없습니다. 영상 정보를 다시 분석한 뒤 다른 화질을 선택해주세요.",
        YtDlpErrorKind.Storage => "저장 공간이 부족하거나 저장 폴더에 쓸 수 없습니다. 환경 점검을 확인해주세요.",
        YtDlpErrorKind.Tool => "다운로드 도구를 실행하지 못했습니다. 환경 점검을 다시 실행해주세요.",
        YtDlpErrorKind.Extractor => "영상 스트림 구조를 해석하지 못했습니다. 상세 작업 기록의 추출기 오류를 확인해주세요.",
        YtDlpErrorKind.Network => $"네트워크 문제로 영상 {operation}에 실패했습니다. 잠시 후 다시 시도해주세요.",
        YtDlpErrorKind.Unavailable => "삭제·비공개되었거나 현재 계정으로 접근할 수 없는 영상입니다.",
        _ => $"영상 {operation}에 실패했습니다. 상세 작업 기록을 확인해주세요."
    };

    private static bool ContainsAny(string message, IEnumerable<string> markers) =>
        markers.Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
