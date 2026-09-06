using System.Security.Cryptography;
using System.Text;
using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class YtDlpServiceCleanupTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        $"ChzzkDownloader-CleanupTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task DeletePartialFilesAsync_RemovesOnlyTheSelectedJobDirectory()
    {
        var url = "https://chzzk.naver.com/video/12345678";
        var outputFolder = Path.Combine(_testRoot, "Output");
        var format = new VideoFormatOption { Selector = "bestvideo[height<=1080]+bestaudio/best" };
        var partialRoot = Path.Combine(_testRoot, "PartialDownloads");
        var partialDirectory = GetPartialDirectory(partialRoot, url, outputFolder, format);
        var unrelatedDirectory = Path.Combine(partialRoot, "unrelated");

        Directory.CreateDirectory(partialDirectory);
        Directory.CreateDirectory(unrelatedDirectory);
        File.WriteAllText(Path.Combine(partialDirectory, "video.f137.mp4.part-Frag1"), "partial");
        File.WriteAllText(Path.Combine(unrelatedDirectory, "keep.txt"), "keep");

        var service = CreateService(partialRoot);
        var result = await service.DeletePartialFilesAsync(url, outputFolder, format);

        Assert.True(result.Succeeded, result.Error);
        Assert.False(Directory.Exists(partialDirectory));
        Assert.True(File.Exists(Path.Combine(unrelatedDirectory, "keep.txt")));
    }

    [Fact]
    public async Task DeletePartialFilesAsync_RetriesUntilAFileLockIsReleased()
    {
        var url = "https://chzzk.naver.com/video/87654321";
        var outputFolder = Path.Combine(_testRoot, "LockedOutput");
        var format = new VideoFormatOption { Selector = "bestvideo+bestaudio/best" };
        var partialRoot = Path.Combine(_testRoot, "LockedPartialDownloads");
        var partialDirectory = GetPartialDirectory(partialRoot, url, outputFolder, format);
        var partialFile = Path.Combine(partialDirectory, "video.mp4.part");
        Directory.CreateDirectory(partialDirectory);
        await File.WriteAllTextAsync(partialFile, "partial");

        var service = CreateService(partialRoot);
        await using var lockStream = new FileStream(
            partialFile,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        var cleanupTask = service.DeletePartialFilesAsync(url, outputFolder, format);
        await Task.Delay(300);
        await lockStream.DisposeAsync();

        var result = await cleanupTask;

        Assert.True(result.Succeeded, result.Error);
        Assert.False(Directory.Exists(partialDirectory));
    }

    private YtDlpService CreateService(string partialRoot) => new(
        new CookieFileService(Path.Combine(_testRoot, "Cookies")),
        partialRoot);

    private static string GetPartialDirectory(
        string partialRoot,
        string url,
        string outputFolder,
        VideoFormatOption format)
    {
        var keySource = $"{url.Trim()}\n{Path.GetFullPath(outputFolder)}\n{format.Selector}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keySource)));
        return Path.Combine(partialRoot, hash[..24]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }
}
