using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class YtDlpCompletedPathTests
{
    [Fact]
    public void ResolveCompletedPath_FindsVideoByIdWhenReportedKoreanPathIsCorrupted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"StreamNest-완료경로-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var partialPath = Path.Combine(root, "sample [fSZunyFnVXs].mp4.part");
            var actualPath = Path.Combine(root, "영상 제목 [fSZunyFnVXs].mp4");
            File.WriteAllBytes(partialPath, [1, 2, 3]);
            File.WriteAllBytes(actualPath, new byte[4096]);

            var resolved = YtDlpService.ResolveCompletedPath(
                Path.Combine(root, "����ٿ�ε�", "영상 제목 [fSZunyFnVXs].mp4"),
                root,
                "https://www.youtube.com/watch?v=fSZunyFnVXs");

            Assert.Equal(Path.GetFullPath(actualPath), resolved);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveCompletedPath_PrefersExistingReportedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"StreamNest-ReportedPath-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var reportedPath = Path.Combine(root, "custom-name.mp4");
            File.WriteAllBytes(reportedPath, new byte[1024]);

            var resolved = YtDlpService.ResolveCompletedPath(
                reportedPath,
                root,
                "https://www.youtube.com/watch?v=fSZunyFnVXs");

            Assert.Equal(Path.GetFullPath(reportedPath), resolved);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
