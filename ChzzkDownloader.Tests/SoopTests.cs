using System.Text.Json;
using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class SoopTests
{
    [Theory]
    [InlineData("https://vod.sooplive.com/player/12345678")]
    [InlineData("https://vod.sooplive.com/player/12345678?from=share#t=10")]
    [InlineData("https://vod.sooplive.com/PLAYER/STATION/12345678/")]
    [InlineData("https://vod.afreecatv.com/player/12345678")]
    [InlineData("https://vod.sooplive.com/player/12345678/catch")]
    [InlineData("https://vod.sooplive.com/player/12345678/catch/?from=share#clip")]
    public void AcceptsAndCanonicalizesVodAndClipUrls(string url)
    {
        Assert.True(VideoUrlService.TryParse(url, out var parsed));
        Assert.Equal(VideoSource.Soop, parsed.Source);
        Assert.Equal("https://vod.sooplive.com/player/12345678", parsed.CanonicalUrl);
        Assert.False(WebVideoUrlService.TryParse(url, out _));
    }

    [Theory]
    [InlineData("https://vod.sooplive.com.evil.test/player/123")]
    [InlineData("https://user:pass@vod.sooplive.com/player/123")]
    [InlineData("http://vod.sooplive.com/player/123")]
    [InlineData("https://vod.sooplive.com:444/player/123")]
    [InlineData("https://vod.sooplive.com/player/123/catchstory")]
    [InlineData("https://vod.sooplive.com/player/123/catch/extra")]
    [InlineData("https://play.sooplive.com/user/123")]
    [InlineData("https://vod.sooplive.com/player/123/extra")]
    [InlineData("https://vod.sooplive.com/player/123abc")]
    public void RejectsUnsupportedAndUnsafeUrls(string url) => Assert.False(VideoUrlService.TryParse(url, out _));

    [Fact]
    public void SoopCookiesStayInSoopScope()
    {
        Assert.All(WebViewCookieCollector.GetOrigins(VideoSource.Soop), origin => Assert.Contains("sooplive.com", origin));
        Assert.True(LoginSecurityPolicy.IsAllowedCookieDomain(".sooplive.com", VideoSource.Soop));
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain(".naver.com", VideoSource.Soop));
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain(".google.com", VideoSource.Soop));
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain(".sooplive.com", VideoSource.YouTube));
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain(".sooplive.com", VideoSource.Chzzk));
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain("sooplive.com.evil.test", VideoSource.Soop));
        Assert.True(LoginSecurityPolicy.IsAllowedTopLevelUri("https://login.sooplive.com/afreeca/login.php"));
        Assert.False(LoginSecurityPolicy.IsAllowedTopLevelUri("https://login.sooplive.com.evil.test/"));
        Assert.Contains("SOOP", LoginDisplayText.GetMissingCookieMessage(VideoSource.Soop));
    }

    private const string PartOne = """
        {"id":"vod_part_1","title":"Example part 1","duration":3,
         "formats":[{"format_id":"hls-720","height":720,"vcodec":"h264","acodec":"aac"},
                    {"format_id":"hls-1080","height":1080,"vcodec":"h264","acodec":"aac"}]}
        """;
    private const string PartTwo = """
        {"id":"vod_part_2","title":"Example part 2","duration":4,
         "formats":[{"format_id":"hls-720","height":720,"vcodec":"h264","acodec":"aac"}]}
        """;

    [Fact]
    public void MultipartUsesAllDurationsAndOnlyCommonQuality()
    {
        using var json = JsonDocument.Parse("{\"_type\":\"multi_video\",\"id\":\"123\",\"title\":\"Full VOD\",\"entries\":[" + PartOne + "," + PartTwo + "]}");
        var video = YtDlpService.ParseSoopVideo(json.RootElement);
        Assert.Equal(2, video.PartCount);
        Assert.Equal(7, video.DurationSeconds);
        var quality = Assert.Single(video.Formats);
        Assert.Equal(720, quality.Height);
        Assert.Equal(7, quality.ExpectedDurationSeconds);
        Assert.True(quality.ExpectsAudio);
        Assert.Equal(new[] { "vod_part_1", "vod_part_2" }, quality.SoopPartIds);
        var arguments = YtDlpService.BuildDownloadArguments("https://vod.sooplive.com/player/123", "out", "temp", quality, "soop.cookies");
        Assert.Contains("--concat-playlist", arguments);
        Assert.Contains("pl_video:SOOP [%(id)s].%(ext)s", arguments);
        Assert.DoesNotContain("--print", arguments);
        Assert.DoesNotContain("after_move:FINAL|%(filepath)s", arguments);
        Assert.Contains("soop:expected_ids=vod_part_1,vod_part_2", arguments);
        Assert.Contains("--abort-on-unavailable-fragments", arguments);
        Assert.Contains("--abort-on-error", arguments);
        Assert.DoesNotContain("--impersonate", arguments);
    }

    [Fact]
    public void SingleUnknownHlsRemainsDownloadableWithoutInventing1080p()
    {
        using var json = JsonDocument.Parse("""
            {"id":"old_clip","title":"Clip","duration":213,"formats":[
              {"format_id":"hls","url":"https://media.example/clip.m3u8","protocol":"m3u8_native","ext":"mp4"}]}
            """);
        var option = Assert.Single(YtDlpService.ParseSoopVideo(json.RootElement).Formats);
        Assert.Null(option.Height);
        Assert.Equal(new[] { "old_clip" }, option.SoopPartIds);
        var arguments = YtDlpService.BuildDownloadArguments("https://vod.sooplive.com/player/123", "out", "temp", option, null);
        Assert.DoesNotContain("--concat-playlist", arguments);
        Assert.Contains("after_move:FINAL|%(filepath)s", arguments);
    }

    [Theory]
    [InlineData("{\"_type\":\"multi_video\",\"entries\":[]}")]
    [InlineData("{\"_type\":\"multi_video\",\"entries\":[null]}")]
    [InlineData("{\"_type\":\"playlist\",\"entries\":[]}")]
    [InlineData("{\"id\":\"123\",\"is_live\":true}")]
    [InlineData("{\"id\":\"123\",\"has_drm\":true}")]
    public void RejectsIncompleteOrUnsupportedResults(string input)
    {
        using var json = JsonDocument.Parse(input);
        Assert.Throws<YtDlpException>(() => YtDlpService.ParseSoopVideo(json.RootElement));
    }
}
