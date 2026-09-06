namespace ChzzkDownloader.Models;

// Kept in memory, never in settings/logs. The temporary engine input is deleted
// after each attempt. URL queries may contain short-lived access tokens.
internal sealed class WebDownloadSnapshot
{
    public required string PageUrl { get; init; }
    public required string InfoJson { get; init; }
    public required string VideoId { get; init; }
    public double? DurationSeconds { get; init; }
    public bool ExpectsAudio { get; init; }
    public bool IncludePagePathInReferer { get; init; }
}
