namespace ChzzkDownloader.Services;

public static class ToolLocator
{
    public static string ToolsDirectory => Path.Combine(AppContext.BaseDirectory, "Tools");
    public static string YtDlpPath => Path.Combine(ToolsDirectory, "yt-dlp.exe");
    public static string FfmpegPath => Path.Combine(ToolsDirectory, "ffmpeg.exe");
    public static string FfprobePath => Path.Combine(ToolsDirectory, "ffprobe.exe");
    public static string NodePath => Path.Combine(ToolsDirectory, "node.exe");
    public static string YtDlpPluginDirectory => Path.Combine(ToolsDirectory, "yt-dlp-plugins", "streamnest");
    public static string ChzzkExtractorPluginPath => Path.Combine(
        YtDlpPluginDirectory,
        "yt_dlp_plugins",
        "extractor",
        "streamnest_chzzk.py");
    public static string PartialDownloadsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChzzkDownloader",
        "PartialDownloads");

    public static string PackedWebExtractorPluginPath => Path.Combine(
        YtDlpPluginDirectory, "yt_dlp_plugins", "extractor", "streamnest_packed.py");

    public static void EnsureWebPluginExists()
    {
        if (!File.Exists(PackedWebExtractorPluginPath))
            throw new FileNotFoundException("StreamNest 일반 웹 영상 플러그인을 찾을 수 없습니다.", PackedWebExtractorPluginPath);
    }

    public static void EnsureToolsExist()
    {
        if (!File.Exists(YtDlpPath))
            throw new FileNotFoundException("yt-dlp.exe를 찾을 수 없습니다.", YtDlpPath);
        if (!File.Exists(FfmpegPath))
            throw new FileNotFoundException("ffmpeg.exe를 찾을 수 없습니다.", FfmpegPath);
        if (!File.Exists(FfprobePath))
            throw new FileNotFoundException("ffprobe.exe를 찾을 수 없습니다.", FfprobePath);
        if (!File.Exists(NodePath))
            throw new FileNotFoundException("YouTube JavaScript challenge 런타임(node.exe)을 찾을 수 없습니다.", NodePath);
        if (!File.Exists(ChzzkExtractorPluginPath))
            throw new FileNotFoundException("StreamNest 치지직 호환성 플러그인을 찾을 수 없습니다.", ChzzkExtractorPluginPath);
    }
}
