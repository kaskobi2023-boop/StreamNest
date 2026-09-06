using System.Net;
using System.Text.Json;
using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class WebVideoTests
{
    [Theory]
    [InlineData("https://www.w3schools.com/html/mov_bbb.mp4")]
    [InlineData("https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8?token=keep-me")]
    public void PublicWebInputsAreSeparateFromExistingPlatforms(string url)
    {
        Assert.True(WebVideoUrlService.TryParse(url, out var uri));
        Assert.Equal(url, uri.AbsoluteUri);
        Assert.False(VideoUrlService.TryParse(url, out _));
    }

    [Theory]
    [InlineData("http://www.example.com/video.mp4")]
    [InlineData("file:///C:/video.mp4")]
    [InlineData("https://user:secret@example.com/video.mp4")]
    [InlineData("https://example.com:8443/video.mp4")]
    [InlineData("https://localhost/video.mp4")]
    [InlineData("https://test.local/video.mp4")]
    [InlineData("https://127.0.0.1/video.mp4")]
    [InlineData("https://2130706433/video.mp4")]
    [InlineData("https://192.168.1.1/video.mp4")]
    [InlineData("https://[::1]/video.mp4")]
    [InlineData("https://[::ffff:127.0.0.1]/video.mp4")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://chzzk.naver.com/video/14471813")]
    public void RejectsUnsafeOrDedicatedPlatformInputs(string url) =>
        Assert.False(WebVideoUrlService.TryParse(url, out _));

    [Theory]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void SeparatesPublicAndPrivateAddresses(string address, bool expected) =>
        Assert.Equal(expected, WebVideoUrlService.IsPublicAddress(IPAddress.Parse(address)));

    [Fact]
    public void WebModeAcceptsUnknownMp4ButLegacyParsingIsUnchanged()
    {
        using var json = JsonDocument.Parse("""{"id":"sample","title":"Sample","formats":[{"ext":"mp4","url":"https://example.com/sample.mp4","vcodec":null}]}""");
        Assert.Empty(YtDlpService.ParseVideoInfo(json.RootElement).Formats);
        var item = Assert.Single(YtDlpService.ParseWebVideos(json.RootElement));
        Assert.Null(item.PlaylistIndex);
        Assert.Single(item.Video.Formats);
    }

    [Theory]
    [InlineData("none", "mp4", false)]
    [InlineData("", "m4a", false)]
    [InlineData("", "mhtml", false)]
    [InlineData("", "mp4", true)]
    public void DoesNotMistakeAudioOrStoryboardForVideo(string codec, string ext, bool accepted)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { id = "sample", formats = new[] { new { vcodec = codec, ext, url = "https://example.com/sample" } } }));
        Assert.Equal(accepted, YtDlpService.ParseWebVideos(json.RootElement).Count > 0);
    }

    [Fact]
    public void EntriesRetainOriginalIndexWhenAnUnsupportedEntryIsSkipped()
    {
        using var json = JsonDocument.Parse("""
            {"entries":[null,{"id":"audio","formats":[{"vcodec":"none"}]},
            {"id":"wanted","playlist_index":7,"formats":[{"vcodec":"h264","height":720}]}]}
            """);
        var item = Assert.Single(YtDlpService.ParseWebVideos(json.RootElement));
        Assert.Equal(7, item.PlaylistIndex);
        var selected = item.SelectFormat(item.Video.Formats[0]);
        var args = new List<string>();
        YtDlpService.AddWebSelectionArguments(args, selected);
        Assert.Equal(new[] { "--playlist-items", "7", "--match-filter", "id = 'wanted' & !is_live & !has_drm" }, args);
        Assert.True(selected.IsGeneralWeb);
        Assert.Equal("wanted", selected.ExpectedVideoId);
    }

    [Theory]
    [InlineData("\"is_live\":true,")]
    [InlineData("\"has_drm\":true,")]
    [InlineData("\"live_status\":\"is_upcoming\",")]
    public void DoesNotOfferDrmOrLiveVideo(string flag)
    {
        using var json = JsonDocument.Parse("{" + flag + "\"id\":\"sample\",\"formats\":[{\"vcodec\":\"h264\",\"height\":720}]}");
        Assert.Empty(YtDlpService.ParseWebVideos(json.RootElement));
    }

    [Fact]
    public void LegacyDownloadGetsNoWebSelectionOptions()
    {
        var args = new List<string>();
        YtDlpService.AddWebSelectionArguments(args, new VideoFormatOption());
        Assert.Empty(args);
    }

    [Fact]
    public void GeneralLogHidesSignedQueryValues()
    {
        var safe = WebVideoUrlService.RedactQueries("GET https://example.com/v.m3u8?secret=abc&key=123 failed");
        Assert.DoesNotContain("abc", safe);
        Assert.DoesNotContain("123", safe);
        Assert.Contains("example.com/v.m3u8", safe);
    }
}
