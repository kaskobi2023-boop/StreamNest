using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class YtDlpOutputParserTests
{
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e999")]
    public void NonFinitePercent_FallsBackToFragmentRatio(string percent)
    {
        Assert.True(YtDlpOutputParser.TryParseProgress($"PROGRESS|{percent}|NA|NA|4|8", out var progress));
        Assert.Equal(50, progress.Percent);
    }

    [Theory]
    [InlineData("-1", 0)]
    [InlineData("101", 100)]
    public void ReportedPercent_IsClamped(string percent, double expected)
    {
        Assert.True(YtDlpOutputParser.TryParseProgress($"PROGRESS|{percent}|NA|NA|NA|NA", out var progress));
        Assert.Equal(expected, progress.Percent);
    }

    [Theory]
    [InlineData("NaN", "8")]
    [InlineData("4", "Infinity")]
    [InlineData("-1", "8")]
    [InlineData("4", "0")]
    public void InvalidFragments_DoNotProducePercent(string index, string count)
    {
        Assert.True(YtDlpOutputParser.TryParseProgress($"PROGRESS|NA|NA|NA|{index}|{count}", out var progress));
        Assert.Null(progress.Percent);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NA")]
    public void InvalidTotal_UsesEstimate(string total)
    {
        Assert.True(YtDlpOutputParser.TryParseProgress($"PROGRESS|NA|NA|NA|NA|NA|-1|{total}|2048", out var progress));
        Assert.Equal(2048, progress.TotalBytes);
        Assert.Null(progress.DownloadedBytes);
    }

    [Fact]
    public void TryParseProgress_PrefersReportedPercentOverFragmentRatio()
    {
        var parsed = YtDlpOutputParser.TryParseProgress(
            "PROGRESS|42.5%|8.2MiB/s|00:12|1|10",
            out var progress);

        Assert.True(parsed);
        Assert.Equal(42.5, progress.Percent);
        Assert.Contains("42.5%", progress.Message);
        Assert.Contains("조각 1/10", progress.Message);
        Assert.Contains("8.2MiB/s", progress.Message);
    }

    [Fact]
    public void TryParseProgress_UsesFragmentRatioOnlyWhenPercentIsMissing()
    {
        Assert.True(YtDlpOutputParser.TryParseProgress(
            "PROGRESS|NA|NA|NA|4|8",
            out var progress));

        Assert.Equal(50, progress.Percent);
        Assert.Equal("50.0% · 조각 4/8", progress.Message);
    }

    [Fact]
    public void TryParseProgress_IncludesDownloadedAndTotalBytesWhenAvailable()
    {
        Assert.True(YtDlpOutputParser.TryParseProgress(
            "PROGRESS|42.5%|8.2MiB/s|00:12|1|10|1048576|2097152|NA",
            out var progress));

        Assert.Equal(1048576, progress.DownloadedBytes);
        Assert.Equal(2097152, progress.TotalBytes);
        Assert.Contains("1 MB / 2 MB", progress.Message);
    }

    [Theory]
    [InlineData("137", "avc1.640028", "none", DownloadPartKind.Video)]
    [InlineData("140", "none", "mp4a.40.2", DownloadPartKind.Audio)]
    [InlineData("22", "avc1.64001F", "mp4a.40.2", DownloadPartKind.Combined)]
    public void TryParseProgress_IdentifiesDownloadPart(
        string partId,
        string videoCodec,
        string audioCodec,
        DownloadPartKind expectedKind)
    {
        Assert.True(YtDlpOutputParser.TryParseProgress(
            $"PROGRESS|42.5%|8.2MiB/s|00:12|1|10|1048576|2097152|NA|{partId}|{videoCodec}|{audioCodec}",
            out var progress));

        Assert.Equal(partId, progress.PartId);
        Assert.Equal(expectedKind, progress.PartKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[download] 25%")]
    [InlineData("PROGRESSIVE|25%|1MiB/s|10|1|4")]
    public void TryParseProgress_IgnoresUnrelatedLines(string line)
    {
        Assert.False(YtDlpOutputParser.TryParseProgress(line, out _));
    }
}
