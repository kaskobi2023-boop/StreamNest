namespace ChzzkDownloader.Models;

public sealed record BrowserCookie(
    string Name,
    string Value,
    string Domain,
    string Path,
    bool IsSecure,
    bool IsHttpOnly,
    double Expires);

public sealed class ProbeResult
{
    public bool IsValidUrl { get; init; }
    public string? VideoId { get; init; }
    public string? Title { get; init; }
    public string? ChannelName { get; init; }
    public long? DurationSeconds { get; init; }
    public string? ThumbnailUrl { get; init; }
    public bool RequiresLogin { get; init; }
    public bool IsAdult { get; init; }
    public string? LoginReason { get; init; }
    public string? Error { get; init; }
}

public sealed class VideoInfo
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ChannelName { get; init; } = string.Empty;
    public long? DurationSeconds { get; init; }
    public string? ThumbnailUrl { get; init; }
    public IReadOnlyList<VideoFormatOption> Formats { get; init; } = [];
}

public sealed class VideoFormatOption
{
    internal WebDownloadSnapshot? WebSnapshot { get; init; }
    public bool IsGeneralWeb { get; init; }
    public int? WebPlaylistIndex { get; init; }
    public string? ExpectedVideoId { get; init; }
    public double? ExpectedDurationSeconds { get; init; }
    public bool ExpectsAudio { get; init; }
    public string Label { get; init; } = string.Empty;
    public int? Height { get; init; }
    public double? Fps { get; init; }
    public long? EstimatedBytes { get; init; }
    public string Selector { get; init; } = "bestvideo+bestaudio/best";

    public override string ToString() => Label;
}

public sealed record WebVideoItem(VideoInfo Video, int? PlaylistIndex)
{
    internal WebDownloadSnapshot? Snapshot { get; init; }
    public override string ToString() => PlaylistIndex is { } index ? $"{index}. {Video.Title}" : Video.Title;

    public VideoFormatOption SelectFormat(VideoFormatOption format) => new()
    {
        Label = format.Label, Height = format.Height, Fps = format.Fps,
        EstimatedBytes = format.EstimatedBytes, Selector = format.Selector,
        ExpectedDurationSeconds = format.ExpectedDurationSeconds, ExpectsAudio = format.ExpectsAudio,
        IsGeneralWeb = true, WebPlaylistIndex = PlaylistIndex, ExpectedVideoId = Video.Id, WebSnapshot = Snapshot
    };
}

public enum DownloadPartKind
{
    Unknown,
    Video,
    Audio,
    Combined
}

public sealed record DownloadProgressInfo(
    double? Percent,
    string Speed,
    string Eta,
    string Message,
    long? DownloadedBytes = null,
    long? TotalBytes = null,
    string PartId = "",
    DownloadPartKind PartKind = DownloadPartKind.Unknown);

public sealed record PartialCleanupResult(bool Succeeded, string? Error);

public sealed class AppSettings
{
    public string OutputFolder { get; set; } = string.Empty;
}
