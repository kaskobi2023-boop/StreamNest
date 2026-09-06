using System.Text.Json;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class YtDlpJsonParsingTests
{
    [Fact]
    public void ParseVideoInfo_AcceptsNullNumericFieldsFromRealYouTubeJson()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "id": "jNQXAC9IVRw",
              "title": "Me at the zoo",
              "duration": 19.0,
              "formats": [
                {
                  "height": 360,
                  "fps": 30,
                  "tbr": null,
                  "vbr": "250.5",
                  "filesize": null,
                  "filesize_approx": null,
                  "vcodec": "avc1.42001E",
                  "has_drm": false
                }
              ]
            }
            """);

        var video = YtDlpService.ParseVideoInfo(document.RootElement);

        Assert.Equal("jNQXAC9IVRw", video.Id);
        var format = Assert.Single(video.Formats);
        Assert.Equal(360, format.Height);
        Assert.True(format.EstimatedBytes > 0);
    }

    [Fact]
    public void ParseVideoInfo_DoesNotCreateFakeAutomaticOptionForStoryboardsOnly()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "id": "members-only",
              "title": "Restricted video",
              "formats": [
                {
                  "format_id": "sb0",
                  "vcodec": "none",
                  "acodec": "none",
                  "ext": "mhtml",
                  "has_drm": false
                }
              ]
            }
            """);

        var video = YtDlpService.ParseVideoInfo(document.RootElement);

        Assert.Empty(video.Formats);
    }
}
