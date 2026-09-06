using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class YtDlpErrorClassifierTests
{
    [Theory]
    [InlineData("Sign in to confirm your age", YtDlpErrorKind.Authentication)]
    [InlineData("This video is available to members of this channel", YtDlpErrorKind.Authentication)]
    [InlineData("Sign in to confirm you're not a bot", YtDlpErrorKind.Authentication)]
    [InlineData("No space left on device", YtDlpErrorKind.Storage)]
    [InlineData("ffprobe not found", YtDlpErrorKind.Tool)]
    [InlineData("An extractor error has occurred. (caused by KeyError('sourceURL'))", YtDlpErrorKind.Extractor)]
    [InlineData("Connection timed out", YtDlpErrorKind.Network)]
    [InlineData("This video has been removed", YtDlpErrorKind.Unavailable)]
    [InlineData("unexpected extractor response", YtDlpErrorKind.Unknown)]
    [InlineData("ERROR: unable to download video data: HTTP Error 403: Forbidden", YtDlpErrorKind.AccessDenied)]
    [InlineData("HTTP Error 403: Forbidden. Sign in to confirm your age", YtDlpErrorKind.Authentication)]
    [InlineData("unable to download: Requested format is not available", YtDlpErrorKind.FormatUnavailable)]
    [InlineData("No video formats found", YtDlpErrorKind.FormatUnavailable)]
    [InlineData("Unable to download: This video has been removed", YtDlpErrorKind.Unavailable)]
    [InlineData("unable to download: [Errno 13] Permission denied", YtDlpErrorKind.Storage)]
    [InlineData("[WinError 5] Access is denied", YtDlpErrorKind.Storage)]
    public void Classify_ReturnsExpectedKind(string message, YtDlpErrorKind expected)
    {
        Assert.Equal(expected, YtDlpErrorClassifier.Classify(message));
    }

    [Fact]
    public void ForbiddenMessage_ProvidesRecoveryWithoutPromisingRetryWillWork()
    {
        var message = YtDlpErrorClassifier.ToUserMessage(YtDlpErrorKind.AccessDenied, "다운로드");
        Assert.Contains("403", message);
        Assert.Contains("yt-dlp", message);
        Assert.DoesNotContain("네트워크 문제", message);
    }
}
