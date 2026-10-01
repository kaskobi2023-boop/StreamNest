using System.Text.Json;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class WebMediaStructureTests
{
    [Theory]
    [InlineData("")]
    [InlineData("\"formats\":null,")]
    public void DirectMediaRetainsKnownResolutionAndAudio(string field)
    {
        using var json = JsonDocument.Parse("{" + field + """
            "id":"fixture","url":"https://example.org/video.mp4","ext":"mp4",
            "vcodec":"h264","acodec":"aac","height":720,"fps":1,"duration":10}
            """);
        var item = Assert.Single(YtDlpService.ParseWebVideos(json.RootElement, "https://example.org/page"));
        var format = item.SelectFormat(Assert.Single(item.Video.Formats));
        Assert.Equal(720, format.Height);
        Assert.Equal(1, format.Fps);
        Assert.Equal(10, format.ExpectedDurationSeconds);
        Assert.True(format.ExpectsAudio);
        Assert.True(format.WebSnapshot!.ExpectsAudio);
        Assert.Contains("height=720", format.Selector);
        Assert.Empty(YtDlpService.ParseVideoInfo(json.RootElement).Formats);
    }

    [Fact]
    public void NullFormatsDoNotBreakParsingOrSnapshot()
    {
        using var json = JsonDocument.Parse("""
            {"id":"fixture","formats":[null,3,{"vcodec":"h264","acodec":"none","height":360},
            {"vcodec":"none","acodec":"aac","has_drm":true}]}
            """);
        var item = Assert.Single(YtDlpService.ParseWebVideos(json.RootElement, "https://example.org/page"));
        Assert.False(Assert.Single(item.Video.Formats).ExpectsAudio);
        Assert.False(item.Snapshot!.ExpectsAudio);
        using var snapshot = JsonDocument.Parse(item.Snapshot.InfoJson);
        Assert.All(snapshot.RootElement.GetProperty("formats").EnumerateArray(),
            format => Assert.Equal(JsonValueKind.Object, format.ValueKind));
    }

    [Fact]
    public void TopLevelDrmNeverCreatesAQualityOption()
    {
        using var json = JsonDocument.Parse("""
            {"id":"fixture","has_drm":true,"formats":[{"vcodec":"h264","height":720}]}
            """);
        Assert.Empty(YtDlpService.ParseVideoInfo(json.RootElement).Formats);
    }

    [Theory]
    [InlineData("{\"id\":\"audio\",\"vcodec\":\"none\",\"acodec\":\"aac\"}", "오디오 전용")]
    [InlineData("{\"formats\":[null,{\"vcodec\":\"none\",\"acodec\":\"opus\"}]}", "오디오 전용")]
    [InlineData("{\"has_drm\":true}", "DRM 보호")]
    [InlineData("{\"formats\":[{\"has_drm\":true}]}", "DRM 보호")]
    [InlineData("{\"is_live\":true}", "진행 중")]
    [InlineData("{\"live_status\":\"is_upcoming\"}", "진행 중")]
    public void EmptyResultsExplainObservedStructure(string metadata, string expected)
    {
        using var json = JsonDocument.Parse(metadata);
        Assert.Empty(YtDlpService.ParseWebVideos(json.RootElement));
        var message = YtDlpService.DescribeWebMetadataError(json.RootElement,
            "https://example.org/media?token=private-token");
        Assert.StartsWith(expected, message);
        Assert.DoesNotContain("private-token", message);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"formats\":[{\"vcodec\":\"none\",\"acodec\":\"none\",\"ext\":\"mhtml\"}]}")]
    [InlineData("{\"formats\":[{\"ext\":\"m4a\"}]}")]
    public void UnknownMetadataAndStoryboardsAreNotDiagnosedAsAudio(string metadata)
    {
        using var json = JsonDocument.Parse(metadata);
        Assert.StartsWith("지원 가능한 공개", YtDlpService.DescribeWebMetadataError(json.RootElement, ""));
    }

    [Fact]
    public void PlaylistReportsMultipleReasonsWithoutRepeatingThem()
    {
        using var json = JsonDocument.Parse("""
            {"entries":[null,{"is_live":true},{"is_live":true},{"has_drm":true},
            {"vcodec":"none","acodec":"aac"}]}
            """);
        var message = YtDlpService.DescribeWebMetadataError(json.RootElement, "");
        Assert.Equal(3, message.Split('\n').Length);
        Assert.Contains("오디오 전용", message);
        Assert.Contains("DRM", message);
    }

    [Fact]
    public void UnsupportedUrlIsNotReportedAsDamageOrConfirmedAuthenticationFailure()
    {
        var message = YtDlpService.DescribeWebAnalysisError("ERROR: Unsupported URL: https://example.org/page?token=private-token");
        Assert.StartsWith("현재 추출기", message);
        Assert.DoesNotContain("private-token", message);
    }
}
