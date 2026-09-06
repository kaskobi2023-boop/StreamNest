using ChzzkDownloader.Services;
using ChzzkDownloader.Models;

namespace ChzzkDownloader.Tests;

public sealed class YtDlpDownloadOptionsTests
{
    [Fact]
    public void ConcurrentFragmentCount_IsThirtyTwo()
    {
        Assert.Equal(32, YtDlpService.ConcurrentFragmentCount);
    }

    [Fact]
    public void WebAnalysis_UsesGenericBrowserCompatibilityWithoutPlatformCookies()
    {
        const string url = "https://example.com/video?token=keep-me";
        var args = YtDlpService.BuildWebAnalyzeArguments(url);
        Assert.Equal("generic:impersonate;streamnest_packed=true", Option(args, "--extractor-args"));
        Assert.Equal(ToolLocator.YtDlpPluginDirectory, Option(args, "--plugin-dirs"));
        Assert.Equal(1, args.Count(arg => arg == "--extractor-args"));
        Assert.Equal("20", Option(args, "--playlist-end"));
        Assert.Equal("chrome", Option(args, "--impersonate"));
        Assert.DoesNotContain("--cookies", args);
        Assert.DoesNotContain("--cookies-from-browser", args);
        Assert.Equal(url, args[^1]);
    }

    [Fact]
    public void WebDownload_ReusesCompatibilityAndSelectedEntryWithoutLeakingCookies()
    {
        const string url = "https://example.com/page?token=keep-me";
        var format = new VideoFormatOption { IsGeneralWeb = true, WebPlaylistIndex = 2, ExpectedVideoId = "entry-2", Selector = "mp4" };
        var args = YtDlpService.BuildDownloadArguments(url, @"C:\영상 폴더", @"C:\임시 폴더", format, @"C:\private-cookies.txt");
        Assert.Equal("generic:impersonate;streamnest_packed=true", Option(args, "--extractor-args"));
        Assert.Equal(1, args.Count(arg => arg == "--extractor-args"));
        Assert.Equal("2", Option(args, "--playlist-items"));
        Assert.Equal("id = 'entry-2' & !is_live & !has_drm", Option(args, "--match-filter"));
        Assert.Equal("4", Option(args, "--concurrent-fragments"));
        Assert.Equal("mp4", Option(args, "--format"));
        Assert.Contains(@"C:\영상 폴더", args);
        Assert.Equal("chrome", Option(args, "--impersonate"));
        Assert.DoesNotContain("--cookies", args);
        Assert.DoesNotContain(@"C:\private-cookies.txt", args);
        Assert.Equal(url, args[^1]);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=sample")]
    [InlineData("https://chzzk.naver.com/video/123")]
    public void PlatformDownload_KeepsCookiesAndFragmentSettingsWithoutGenericOverride(string url)
    {
        var args = YtDlpService.BuildDownloadArguments(url, "out", "parts", new VideoFormatOption(), "cookies.txt");
        Assert.DoesNotContain("--extractor-args", args);
        Assert.DoesNotContain("--impersonate", args);
        Assert.DoesNotContain("--playlist-items", args);
        Assert.Equal("cookies.txt", Option(args, "--cookies"));
        Assert.Equal("32", Option(args, "--concurrent-fragments"));
        Assert.Contains("--abort-on-unavailable-fragments", args);
        Assert.Contains("--keep-fragments", args);
        Assert.Equal("10", Option(args, "--retries"));
        Assert.Equal(url, args[^1]);
    }

    private static string Option(List<string> args, string name)
    {
        var index = args.IndexOf(name);
        Assert.True(index >= 0 && index + 1 < args.Count, $"Missing option: {name}");
        return args[index + 1];
    }
}
