using System.Globalization;
using ChzzkDownloader.Models;

namespace ChzzkDownloader.Services;

public sealed partial class YtDlpService
{
    internal static List<string> BuildWebAnalyzeArguments(string url, bool includePagePathInReferer = false)
    {
        List<string> arguments =
        [
            "--no-config", "--no-playlist", "--no-flat-playlist", "--playlist-end", WebVideoLimit.ToString(CultureInfo.InvariantCulture),
            "--plugin-dirs", ToolLocator.YtDlpPluginDirectory,
            "--encoding", "utf-8", "--skip-download", "--ignore-no-formats-error", "--ignore-errors", "--dump-single-json",
            "--socket-timeout", "20", "--retries", "1", "--extractor-retries", "1",
            "--js-runtimes", $"node:{ToolLocator.NodePath}", "--ffmpeg-location", ToolLocator.ToolsDirectory
        ];
        AddWebCompatibilityArguments(arguments, includePagePathInReferer);
        arguments.Add(url);
        return arguments;
    }

    internal static List<string> BuildDownloadArguments(
        string url, string outputFolder, string partialDirectory, VideoFormatOption format, string? cookiePath,
        string? infoJsonPath = null)
    {
        List<string> arguments =
        [
            "--no-config",
            "--no-playlist",
            "--plugin-dirs", ToolLocator.YtDlpPluginDirectory,
            "--encoding", "utf-8",
            "--continue",
            "--abort-on-unavailable-fragments",
            "--keep-fragments",
            "--concurrent-fragments", (format.IsGeneralWeb ? 16 : ConcurrentFragmentCount).ToString(CultureInfo.InvariantCulture),
            "--retries", "10",
            "--fragment-retries", "10",
            "--file-access-retries", "3",
            "--socket-timeout", "30",
            "--js-runtimes", $"node:{ToolLocator.NodePath}",
            "--ffmpeg-location", ToolLocator.ToolsDirectory,
            "--windows-filenames",
            "--paths", outputFolder,
            "--paths", $"temp:{partialDirectory}",
            "--output", "%(title).140B [%(id)s].%(ext)s",
            "--newline",
            "--progress",
            "--progress-delta", "0.25",
            "--progress-template", "download:PROGRESS|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s|%(progress.fragment_index)s|%(progress.fragment_count)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(info.format_id)s|%(info.vcodec)s|%(info.acodec)s",
            "--print", "after_move:FINAL|%(filepath)s",
            "--format", format.Selector
        ];
        if (format.IsAudioOnly)
        {
            arguments.AddRange(["--extract-audio", "--audio-format", "m4a", "--audio-quality", "0"]);
        }
        else
        {
            arguments.AddRange(["--merge-output-format", "mp4", "--remux-video", "mp4"]);
        }
        if (format.SoopPartIds.Count > 0)
        {
            arguments.AddRange(["--abort-on-error", "--extractor-args", $"soop:expected_ids={string.Join(',', format.SoopPartIds)}"]);
            if (format.SoopPartIds.Count > 1)
            {
                var printIndex = arguments.IndexOf("--print");
                arguments.RemoveRange(printIndex, 2);
                // yt-dlp's playlist print runs BEFORE FFmpegConcatPP. Use an
                // explicit staging filename and require it after successful exit.
                arguments.AddRange(["--concat-playlist", "always", "--output", "pl_video:SOOP [%(id)s].%(ext)s"]);
            }
        }
        AddWebSelectionArguments(arguments, format, cached: infoJsonPath is not null);
        if (format.IsGeneralWeb)
        {
            AddWebCompatibilityArguments(arguments, format.WebSnapshot?.IncludePagePathInReferer == true);
        }
        else
            AddCookies(arguments, cookiePath);
        if (infoJsonPath is not null)
            arguments.AddRange(["--load-info-json", infoJsonPath]);
        else
            arguments.Add(url);
        return arguments;
    }

    private static void AddWebCompatibilityArguments(List<string> arguments, bool includePagePathInReferer = false)
    {
        // generic:impersonate covers the entry page only. Keep the same browser
        // transport for manifests, child playlists and fragments, including when
        // downloading a cached snapshot. This helper is never used by platforms.
        arguments.AddRange(["--impersonate", "chrome"]);
        arguments.AddRange(["--extractor-args", "generic:impersonate;streamnest_packed=true" +
            (includePagePathInReferer ? ";streamnest_referer=page" : string.Empty)]);
    }
}
