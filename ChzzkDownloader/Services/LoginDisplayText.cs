namespace ChzzkDownloader.Services;

public static class LoginDisplayText
{
    public static string GetLoginScopeName(VideoSource source) => source == VideoSource.YouTube
        ? "YouTube/Google"
        : "치지직/네이버";

    public static string GetExternalLinkPrompt(VideoSource source, string origin) =>
        $"이 주소는 {GetLoginScopeName(source)} 로그인 범위 밖입니다. 기본 브라우저에서 열까요?\n\n{origin}";

    public static string GetMissingCookieMessage(VideoSource source) => source == VideoSource.YouTube
        ? "YouTube 로그인 쿠키를 찾지 못했습니다. 먼저 YouTube/Google 계정에 로그인해주세요."
        : "로그인 쿠키를 찾지 못했습니다. 먼저 네이버에 로그인해주세요.";

    public const string ClearAllSessionsPrompt =
        "앱에 저장된 치지직·YouTube 로그인 세션을 모두 삭제할까요? 다시 로그인해야 합니다.";
}
