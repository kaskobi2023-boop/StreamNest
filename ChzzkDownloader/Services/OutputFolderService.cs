namespace ChzzkDownloader.Services;

public static class OutputFolderService
{
    public static string ResolveSelectedFolder(string? selectedFolder)
    {
        if (string.IsNullOrWhiteSpace(selectedFolder))
            throw new ArgumentException("저장 폴더가 지정되지 않았습니다.", nameof(selectedFolder));

        return Path.GetFullPath(selectedFolder.Trim());
    }
}
