using System.Text.Json;
using ChzzkDownloader.Services;
using Xunit;

namespace ChzzkDownloader.Tests;

public sealed class WebDownloadReliabilityTests
{
    [Fact]
    public void VideoDurationIsNotInflatedByContainerAudioAlignment()
    {
        using var json = JsonDocument.Parse("""
            {"streams":[{"codec_type":"video","height":144,"duration":"33107.999667"},
            {"codec_type":"audio","duration":"33108.003979"}],"format":{"duration":"33108.037021"}}
            """);
        MediaVerificationService.ValidateMetadata(json.RootElement, 33105, 144, true);
    }

    [Fact]
    public void LongerContainerAudioCannotHideTruncatedVideo()
    {
        using var json = JsonDocument.Parse("""
            {"streams":[{"codec_type":"video","height":180,"duration":"2"},
            {"codec_type":"audio","duration":"4"}],"format":{"duration":"4"}}
            """);
        Assert.Throws<YtDlpException>(() => MediaVerificationService.ValidateMetadata(json.RootElement, 4, 180, true));
    }

    [Theory]
    [InlineData("N/A")]
    [InlineData("NaN")]
    public void MissingPerTrackDurationFallsBackToContainer(string duration)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new {
            streams = new[] { new { codec_type = "video", height = 180, duration } },
            format = new { duration = "4" }
        }));
        MediaVerificationService.ValidateMetadata(json.RootElement, 4, 180, false);
    }

    [Fact]
    public void EveryParsedQualityRetainsDurationAndAudioForCompletionChecks()
    {
        using var json = JsonDocument.Parse("""
            {"id":"fixture","duration":4.04,"formats":[
            {"height":180,"vcodec":"h264","acodec":"none"},
            {"height":90,"vcodec":"h264","acodec":"none"},
            {"vcodec":"none","acodec":"aac"}]}
            """);
        var video = YtDlpService.ParseVideoInfo(json.RootElement);
        Assert.Equal(2, video.Formats.Count);
        foreach (var option in video.Formats)
        {
            Assert.Equal(4.04, option.ExpectedDurationSeconds);
            Assert.True(option.ExpectsAudio);
            var webOption = new ChzzkDownloader.Models.WebVideoItem(video, 1).SelectFormat(option);
            Assert.Equal(option.ExpectedDurationSeconds, webOption.ExpectedDurationSeconds);
            Assert.True(webOption.ExpectsAudio);
        }
    }

    [Theory]
    [InlineData("[StreamNest:HLS_ACCESS_DENIED] HTTP Error 403", "영상 주소는 찾았지만 HLS 재생목록 서버가 접근을 거부")]
    [InlineData("[StreamNest:HLS_FETCH_FAILED] HTTP Error 404", "영상 주소는 찾았지만 HLS 재생목록을 읽지 못")]
    [InlineData("HTTP Error 403 on page", "지원 가능한 공개 영상 스트림을 확인하지 못")]
    public void WebErrorsDistinguishManifestFailureFromPageFailure(string error, string expected)
    {
        var message = YtDlpService.DescribeWebAnalysisError(error + " https://example.org/path?token=secret-value");
        Assert.StartsWith(expected, message);
        Assert.DoesNotContain("secret-value", message);
    }

    [Fact]
    public void NullFailedEntriesAreNotTreatedAsVideos()
    {
        using var json = JsonDocument.Parse("null");
        Assert.Empty(YtDlpService.ParseWebVideos(json.RootElement));
    }

    [Fact]
    public void FragmentGuardRejectsTextErrorAndAcceptsBinaryContainer()
    {
        Assert.True(MediaVerificationService.LooksLikeTextFragment(System.Text.Encoding.ASCII.GetBytes(
            "<html><body>Upstream server error, please retry later</body></html>")));
        var transport = new byte[188];
        transport[0] = 0x47;
        Assert.False(MediaVerificationService.LooksLikeTextFragment(transport));
    }

    [Fact]
    public void PublishingVerifiedFileDoesNotOverwriteAnExistingDownload()
    {
        var root = Path.Combine(Path.GetTempPath(), "StreamNest-PublishTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "stage"));
        try
        {
            var original = Path.Combine(root, "video.mp4");
            var source = Path.Combine(root, "stage", "video.mp4");
            File.WriteAllText(original, "existing-user-file");
            File.WriteAllText(source, "new-verified-file");
            var published = YtDlpService.PublishVerifiedWebFile(source, root);
            Assert.Equal("existing-user-file", File.ReadAllText(original));
            Assert.Equal("video (2).mp4", Path.GetFileName(published));
            Assert.Equal("new-verified-file", File.ReadAllText(published));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SnapshotKeepsFormatsAndHeadersButNotCredentialsOrImplicitReextraction()
    {
        using var json = JsonDocument.Parse("""
            {"id":"sample","title":"Demo","duration":4,"webpage_url":"https://example.org/watch",
             "filepath":"C:/untrusted.mp4","cookies":"secret","formats":[
             {"format_id":"hls","url":"https://cdn.example.org/v.m3u8?token=keep","ext":"mp4","vcodec":"h264","acodec":"aac","height":180,
              "http_headers":{"Referer":"https://example.org/watch","Cookie":"secret","Authorization":"secret"}}]}
            """);
        var item = Assert.Single(YtDlpService.ParseWebVideos(json.RootElement, "https://example.org/watch", true));
        var selected = item.SelectFormat(Assert.Single(item.Video.Formats));
        Assert.Same(item.Snapshot, selected.WebSnapshot);
        Assert.True(item.Snapshot!.ExpectsAudio);
        Assert.DoesNotContain("secret", item.Snapshot.InfoJson);
        Assert.DoesNotContain("webpage_url", item.Snapshot.InfoJson);
        Assert.DoesNotContain("filepath", item.Snapshot.InfoJson);
        Assert.Contains("token=keep", item.Snapshot.InfoJson);
        Assert.Contains("Referer", item.Snapshot.InfoJson);
        var args = YtDlpService.BuildDownloadArguments("https://example.org/watch", "out", "parts", selected, null, "snapshot.json");
        Assert.Contains("--load-info-json", args);
        Assert.Equal("chrome", args[args.IndexOf("--impersonate") + 1]);
        Assert.DoesNotContain("--playlist-items", args);
        Assert.DoesNotContain("https://example.org/watch", args);
        Assert.Contains("--abort-on-unavailable-fragments", args);
        Assert.Contains("--keep-fragments", args);
        Assert.DoesNotContain("--ignore-errors", args);
        Assert.Contains("generic:impersonate;streamnest_packed=true;streamnest_referer=page", args);
        Assert.Equal("bestvideo[height=180]+bestaudio/best[height=180]", selected.Selector);
    }

    [Theory]
    [InlineData("HTTP Error 403: Forbidden", true)]
    [InlineData("HTTP Error 410: Gone", true)]
    [InlineData("HTTP Error 404: Not Found", true)]
    [InlineData("Disk full", false)]
    [InlineData("검증 실패: 길이가 다릅니다", false)]
    [InlineData("Download cancelled", false)]
    public void RefreshIsLimitedToAddressFailures(string error, bool expected) =>
        Assert.Equal(expected, YtDlpService.IsRefreshableStreamError(error));

    [Theory]
    [InlineData(4, 180, true, true)]
    [InlineData(2, 180, true, false)]
    [InlineData(4, 90, true, false)]
    [InlineData(4, 180, false, false)]
    public void CompletionGateRejectsTruncationWrongQualityAndMissingAudio(double duration, int height, bool audio, bool valid)
    {
        var streams = new List<object> { new { codec_type = "video", height } };
        if (audio) streams.Add(new { codec_type = "audio" });
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { streams, format = new { duration } }));
        if (valid) MediaVerificationService.ValidateMetadata(json.RootElement, 4, 180, true);
        else Assert.Throws<YtDlpException>(() => MediaVerificationService.ValidateMetadata(json.RootElement, 4, 180, true));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"streams\":[{\"codec_type\":\"audio\"}],\"format\":{\"duration\":4}}")]
    [InlineData("{\"streams\":[{\"codec_type\":\"video\"}],\"format\":{\"duration\":\"NaN\"}}")]
    public void CompletionGateRejectsNonVideoOrUnknownDuration(string info)
    {
        using var json = JsonDocument.Parse(info);
        Assert.Throws<YtDlpException>(() => MediaVerificationService.ValidateMetadata(json.RootElement, null, null, false));
    }
}
