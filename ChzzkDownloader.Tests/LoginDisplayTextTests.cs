using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class LoginDisplayTextTests
{
    [Fact]
    public void GetExternalLinkPrompt_UsesYouTubeScope()
    {
        var message = LoginDisplayText.GetExternalLinkPrompt(
            VideoSource.YouTube,
            "https://support.google.com");

        Assert.Contains("YouTube/Google", message);
        Assert.DoesNotContain("네이버 로그인 범위", message);
    }

    [Fact]
    public void GetExternalLinkPrompt_UsesChzzkScope()
    {
        var message = LoginDisplayText.GetExternalLinkPrompt(
            VideoSource.Chzzk,
            "https://help.naver.com");

        Assert.Contains("치지직/네이버", message);
    }

    [Fact]
    public void ClearPrompt_ExplainsThatSharedProfileClearsBothServices()
    {
        Assert.Contains("치지직·YouTube", LoginDisplayText.ClearAllSessionsPrompt);
        Assert.Contains("모두 삭제", LoginDisplayText.ClearAllSessionsPrompt);
    }
}
