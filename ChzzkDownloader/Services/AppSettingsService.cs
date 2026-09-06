using System.Text.Json;
using ChzzkDownloader.Models;

namespace ChzzkDownloader.Services;

public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public AppSettingsService()
    {
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChzzkLocalDownloader");
        Directory.CreateDirectory(appDirectory);
        _settingsPath = Path.Combine(appDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        var portableArchive = GetPortableArchiveFolder(AppContext.BaseDirectory);
        try
        {
            Directory.CreateDirectory(portableArchive);
            if (File.Exists(_settingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath));
                if (settings is not null && !string.IsNullOrWhiteSpace(settings.OutputFolder))
                {
                    var resolvedFolder = ResolveOutputFolder(settings.OutputFolder, AppContext.BaseDirectory);
                    if (!resolvedFolder.Equals(settings.OutputFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        settings.OutputFolder = resolvedFolder;
                        Save(settings);
                    }
                    return settings;
                }
            }
        }
        catch
        {
            // 손상된 설정은 기본값으로 복구한다.
        }

        return new AppSettings { OutputFolder = portableArchive };
    }

    internal static string ResolveOutputFolder(string? savedFolder, string appDirectory)
    {
        var portableArchive = GetPortableArchiveFolder(appDirectory);
        if (string.IsNullOrWhiteSpace(savedFolder))
            return portableArchive;

        var normalizedSavedFolder = Path.GetFullPath(savedFolder.Trim());
        return IsVersionedPackageArchive(normalizedSavedFolder)
            ? portableArchive
            : normalizedSavedFolder;
    }

    internal static string GetPortableArchiveFolder(string appDirectory) =>
        Path.GetFullPath(Path.Combine(appDirectory, "Archive"));

    private static bool IsVersionedPackageArchive(string folder)
    {
        var directory = new DirectoryInfo(folder);
        var packageDirectoryName = directory.Parent?.Name ?? string.Empty;
        return directory.Name.Equals("Archive", StringComparison.OrdinalIgnoreCase) &&
               packageDirectoryName.StartsWith("StreamNestDownloader-", StringComparison.OrdinalIgnoreCase) &&
               packageDirectoryName.Contains("-win-", StringComparison.OrdinalIgnoreCase);
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
