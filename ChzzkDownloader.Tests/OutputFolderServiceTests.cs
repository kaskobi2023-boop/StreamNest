using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class OutputFolderServiceTests
{
    [Fact]
    public void ResolveSelectedFolder_UsesTheFolderShownInTheControl()
    {
        var selectedFolder = Path.Combine(Path.GetTempPath(), "StreamNest selected folder");

        var result = OutputFolderService.ResolveSelectedFolder($"  {selectedFolder}  ");

        Assert.Equal(Path.GetFullPath(selectedFolder), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveSelectedFolder_RejectsMissingSelection(string? selectedFolder)
    {
        Assert.Throws<ArgumentException>(() => OutputFolderService.ResolveSelectedFolder(selectedFolder));
    }
}
