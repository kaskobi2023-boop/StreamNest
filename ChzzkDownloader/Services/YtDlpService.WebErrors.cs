namespace ChzzkDownloader.Services;

public sealed partial class YtDlpService
{
    internal static string DescribeWebAnalysisError(string error)
    {
        var detail = WebVideoUrlService.RedactQueries(CleanError(error, null));
        var summary = detail.Contains("[StreamNest:HLS_ACCESS_DENIED]", StringComparison.Ordinal)
            ? "영상 주소는 찾았지만 HLS 재생목록 서버가 접근을 거부했습니다(HTTP 401/403). 브라우저 호환 요청을 적용해도 거부된 상태이며, 주소 미지원 오류가 아닙니다."
            : detail.Contains("[StreamNest:HLS_FETCH_FAILED]", StringComparison.Ordinal)
                ? "영상 주소는 찾았지만 HLS 재생목록을 읽지 못했습니다. 아래 서버 응답을 확인해주세요."
                : "지원 가능한 공개 영상 스트림을 확인하지 못했습니다. 아래 오류를 확인해주세요.";
        return summary + (string.IsNullOrWhiteSpace(detail) ? string.Empty : "\n" + detail);
    }
}
