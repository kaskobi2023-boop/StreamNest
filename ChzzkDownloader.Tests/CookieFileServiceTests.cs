using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class CookieFileServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ChzzkDownloaderTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateAsync_ExportsOnlySecurePlaybackCookies()
    {
        var service = new CookieFileService(_temporaryDirectory);
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        BrowserCookie[] cookies =
        [
            new("NID_SES", "allowed-value", ".naver.com", "/", true, true, future),
            new("SID", "youtube-value", ".youtube.com", "/", true, true, future),
            new("HSID", "google-value", ".google.com", "/", true, true, future),
            new("NID_TEST", "wrong-domain", "nid.naver.com", "/", true, true, future),
            new("NID_TEST", "not-secure", ".naver.com", "/", false, false, future),
            new("NID_TEST", "expired", ".naver.com", "/", true, false, 1)
        ];

        var path = await service.CreateAsync(cookies);

        Assert.NotNull(path);
        var contents = await File.ReadAllTextAsync(path!);
        Assert.Contains("allowed-value", contents);
        Assert.Contains("youtube-value", contents);
        Assert.Contains("google-value", contents);
        Assert.DoesNotContain("wrong-domain", contents);
        Assert.DoesNotContain("not-secure", contents);
        Assert.DoesNotContain("expired", contents);
        service.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Constructor_RemovesCookieFileWithoutActiveOwner()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var orphanedPath = Path.Combine(_temporaryDirectory, "cookies-99999999-orphaned.txt");
        File.WriteAllText(orphanedPath, "sensitive");

        _ = new CookieFileService(_temporaryDirectory);

        Assert.False(File.Exists(orphanedPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
