using System.Globalization;
using ChzzkDownloader.Models;

namespace ChzzkDownloader.Services;

public static class YtDlpOutputParser
{
    public static bool TryParseProgress(string line, out DownloadProgressInfo progress)
    {
        progress = default!;
        if (!line.StartsWith("PROGRESS|", StringComparison.Ordinal))
            return false;

        var parts = line.Split('|');
        var percentText = parts.ElementAtOrDefault(1)?.Trim().TrimEnd('%') ?? string.Empty;
        double? percent = double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed)
            ? Math.Clamp(parsed, 0, 100)
            : null;
        var speed = Normalize(parts.ElementAtOrDefault(2));
        var eta = Normalize(parts.ElementAtOrDefault(3));
        var fragmentIndex = ParseNumber(parts.ElementAtOrDefault(4));
        var fragmentCount = ParseNumber(parts.ElementAtOrDefault(5));
        var downloadedBytes = ParseLong(parts.ElementAtOrDefault(6));
        var reportedTotal = ParseLong(parts.ElementAtOrDefault(7));
        var estimatedTotal = ParseLong(parts.ElementAtOrDefault(8));
        var totalBytes = reportedTotal is > 0 ? reportedTotal : estimatedTotal is > 0 ? estimatedTotal : null;
        var partId = Normalize(parts.ElementAtOrDefault(9));
        var videoCodec = Normalize(parts.ElementAtOrDefault(10));
        var audioCodec = Normalize(parts.ElementAtOrDefault(11));
        var partKind = GetPartKind(videoCodec, audioCodec);

        if (!percent.HasValue && fragmentIndex.HasValue && fragmentCount is > 0)
            percent = Math.Clamp(fragmentIndex.Value / fragmentCount.Value * 100, 0, 100);

        var message = percent.HasValue ? $"{percent:0.0}%" : "다운로드 중";
        if (fragmentIndex.HasValue && fragmentCount is > 0)
            message += $" · 조각 {fragmentIndex:0}/{fragmentCount:0}";
        if (!string.IsNullOrWhiteSpace(speed))
            message += $" · {speed}";
        if (!string.IsNullOrWhiteSpace(eta))
            message += $" · 남은 시간 {eta}";
        if (downloadedBytes is >= 0 && totalBytes is > 0)
            message += $" · {FormatBytes(downloadedBytes.Value)} / {FormatBytes(totalBytes.Value)}";

        progress = new DownloadProgressInfo(
            percent,
            speed,
            eta,
            message,
            downloadedBytes,
            totalBytes,
            partId,
            partKind);
        return true;
    }

    private static DownloadPartKind GetPartKind(string videoCodec, string audioCodec)
    {
        var hasVideo = !string.IsNullOrWhiteSpace(videoCodec) &&
                       !videoCodec.Equals("none", StringComparison.OrdinalIgnoreCase);
        var hasAudio = !string.IsNullOrWhiteSpace(audioCodec) &&
                       !audioCodec.Equals("none", StringComparison.OrdinalIgnoreCase);

        return (hasVideo, hasAudio) switch
        {
            (true, true) => DownloadPartKind.Combined,
            (true, false) => DownloadPartKind.Video,
            (false, true) => DownloadPartKind.Audio,
            _ => DownloadPartKind.Unknown
        };
    }

    private static double? ParseNumber(string? value) =>
        double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) && parsed >= 0
            ? parsed
            : null;

    private static long? ParseLong(string? value) =>
        long.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;

    private static string Normalize(string? value) =>
        value?.Trim() is { Length: > 0 } text && text != "NA" ? text : string.Empty;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
