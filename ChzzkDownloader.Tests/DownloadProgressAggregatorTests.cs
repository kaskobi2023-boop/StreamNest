using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class DownloadProgressAggregatorTests
{
    [Fact]
    public void Aggregate_MapsVideoAndAudioAcrossOneOverallHundredPercent()
    {
        var aggregator = new DownloadProgressAggregator("bestvideo+bestaudio/best");

        var videoHalf = aggregator.Aggregate(Progress(50, "137", DownloadPartKind.Video));
        var videoComplete = aggregator.Aggregate(Progress(100, "137", DownloadPartKind.Video));
        var audioStart = aggregator.Aggregate(Progress(0, "140", DownloadPartKind.Audio));
        var audioHalf = aggregator.Aggregate(Progress(50, "140", DownloadPartKind.Audio));
        var audioComplete = aggregator.Aggregate(Progress(100, "140", DownloadPartKind.Audio));

        Assert.Equal(25, videoHalf.Percent);
        Assert.Equal(50, videoComplete.Percent);
        Assert.Equal(50, audioStart.Percent);
        Assert.Equal(75, audioHalf.Percent);
        Assert.Equal(100, audioComplete.Percent);
        Assert.Contains("영상 50.0% · 전체 25.0%", videoHalf.Message);
        Assert.Contains("오디오 50.0% · 전체 75.0%", audioHalf.Message);
    }

    [Fact]
    public void Aggregate_UsesOneNormalPassForCombinedStream()
    {
        var aggregator = new DownloadProgressAggregator("bestvideo+bestaudio/best");

        var progress = aggregator.Aggregate(Progress(60, "22", DownloadPartKind.Combined));

        Assert.Equal(60, progress.Percent);
        Assert.Equal("60.0% · 1 MiB/s", progress.Message);
    }

    [Fact]
    public void Aggregate_NeverMovesOverallProgressBackward()
    {
        var aggregator = new DownloadProgressAggregator("bestvideo+bestaudio/best");

        var first = aggregator.Aggregate(Progress(80, "137", DownloadPartKind.Video));
        var delayed = aggregator.Aggregate(Progress(70, "137", DownloadPartKind.Video));

        Assert.Equal(40, first.Percent);
        Assert.Equal(40, delayed.Percent);
    }

    [Fact]
    public void Aggregate_DoesNotJumpForwardForDelayedVideoProgressAfterAudioStarts()
    {
        var aggregator = new DownloadProgressAggregator("bestvideo+bestaudio/best");

        aggregator.Aggregate(Progress(100, "137", DownloadPartKind.Video));
        var audio = aggregator.Aggregate(Progress(10, "140", DownloadPartKind.Audio));
        var delayedVideo = aggregator.Aggregate(Progress(90, "137", DownloadPartKind.Video));
        var nextAudio = aggregator.Aggregate(Progress(20, "140", DownloadPartKind.Audio));

        Assert.Equal(55d, audio.Percent!.Value, 6);
        Assert.Equal(55d, delayedVideo.Percent!.Value, 6);
        Assert.Equal(60d, nextAudio.Percent!.Value, 6);
    }

    [Fact]
    public void Aggregate_InfersSecondPartFromPercentResetWithoutCodecMetadata()
    {
        var aggregator = new DownloadProgressAggregator("bestvideo+bestaudio/best");

        aggregator.Aggregate(Progress(100, string.Empty, DownloadPartKind.Unknown));
        var secondPartHalf = aggregator.Aggregate(Progress(50, string.Empty, DownloadPartKind.Unknown));

        Assert.Equal(75, secondPartHalf.Percent);
    }

    private static DownloadProgressInfo Progress(
        double percent,
        string partId,
        DownloadPartKind partKind) =>
        new(percent, "1 MiB/s", "00:10", $"{percent:0.0}% · 1 MiB/s", PartId: partId, PartKind: partKind);
}
