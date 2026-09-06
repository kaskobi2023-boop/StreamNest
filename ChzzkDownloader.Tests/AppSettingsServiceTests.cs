using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public void ResolveOutputFolder_MigratesArchiveFromOlderVersionPackage()
    {
        var desktop = Path.Combine(Path.GetTempPath(), "StreamNest packages");
        var savedFolder = Path.Combine(desktop, "StreamNestDownloader-0.4.9-win-x64", "Archive");
        var currentAppDirectory = Path.Combine(desktop, "StreamNestDownloader-0.5.4-win-x64");

        var result = AppSettingsService.ResolveOutputFolder(savedFolder, currentAppDirectory);

        Assert.Equal(Path.Combine(currentAppDirectory, "Archive"), result);
    }

    [Fact]
    public void ResolveOutputFolder_PreservesUserSelectedCustomFolder()
    {
        var customFolder = Path.Combine(Path.GetTempPath(), "My video library");
        var currentAppDirectory = Path.Combine(Path.GetTempPath(), "StreamNestDownloader-0.5.4-win-x64");

        var result = AppSettingsService.ResolveOutputFolder(customFolder, currentAppDirectory);

        Assert.Equal(Path.GetFullPath(customFolder), result);
    }

    [Fact]
    public void ResolveOutputFolder_UsesCurrentPackageArchiveWhenSettingIsMissing()
    {
        var currentAppDirectory = Path.Combine(Path.GetTempPath(), "StreamNestDownloader-0.5.4-win-x64");

        var result = AppSettingsService.ResolveOutputFolder(null, currentAppDirectory);

        Assert.Equal(Path.Combine(currentAppDirectory, "Archive"), result);
    }
}
