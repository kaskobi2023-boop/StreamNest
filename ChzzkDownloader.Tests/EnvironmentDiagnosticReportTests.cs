using ChzzkDownloader.Models;

namespace ChzzkDownloader.Tests;

public sealed class EnvironmentDiagnosticReportTests
{
    [Fact]
    public void CanDownload_IsFalseOnlyForBlockingErrors()
    {
        var report = new EnvironmentDiagnosticReport
        {
            Items =
            [
                new DiagnosticItem("yt-dlp", DiagnosticState.Ready, "ok", true),
                new DiagnosticItem("WebView2", DiagnosticState.Error, "missing")
            ]
        };

        Assert.True(report.CanDownload);
        Assert.False(report.CanUseLogin);
        Assert.Equal("도구 정상 · 일부 경고", report.StatusText);
    }

    [Fact]
    public void CanDownload_IsFalseForMissingRequiredTool()
    {
        var report = new EnvironmentDiagnosticReport
        {
            Items =
            [
                new DiagnosticItem("FFmpeg", DiagnosticState.Error, "missing", true),
                new DiagnosticItem("WebView2", DiagnosticState.Ready, "ok")
            ]
        };

        Assert.False(report.CanDownload);
        Assert.True(report.CanUseLogin);
        Assert.Equal("환경 확인 필요", report.StatusText);
    }
}
