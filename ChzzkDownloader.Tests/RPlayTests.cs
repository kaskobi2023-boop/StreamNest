using System.Text.Json;
using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class RPlayTests
{
    [Theory]
    [InlineData("https://rplay.live/play/111111111111111111111111?playlist=purchaseList", "111111111111111111111111")]
    [InlineData("https://rplay.live/play/222222222222222222222222", "222222222222222222222222")]
    [InlineData("https://www.rplay.live/play/111111111111111111111111", "111111111111111111111111")]
    public void TryParse_AcceptsSupportedRPlayUrls(string url, string expectedId)
    {
        Assert.True(VideoUrlService.TryParse(url, out var info));
        Assert.Equal(VideoSource.RPlay, info.Source);
        Assert.Equal(expectedId, info.VideoId);
        Assert.Equal($"https://rplay.live/play/{expectedId}", info.CanonicalUrl);
    }

    [Theory]
    [InlineData("https://rplay.live/")]
    [InlineData("https://rplay.live/explore")]
    [InlineData("https://evil.rplay.live/play/111111111111111111111111")]
    [InlineData("http://rplay.live/play/111111111111111111111111")]
    [InlineData("https://user:pass@rplay.live/play/111111111111111111111111")]
    public void TryParse_RejectsUnsupportedOrUnsafeRPlayUrls(string url)
    {
        Assert.False(VideoUrlService.TryParse(url, out _));
    }

    [Theory]
    [InlineData("https://rplay.live/login")]
    [InlineData("https://rplay.live/play/111111111111111111111111")]
    [InlineData("https://www.rplay.live/login")]
    [InlineData("https://api.rplay.live")]
    [InlineData("https://auth.rplay.live")]
    [InlineData("https://accounts.google.com")]
    [InlineData("https://accounts.google.co.kr")]
    [InlineData("https://consent.google.com")]
    [InlineData("https://myaccount.google.com")]
    [InlineData("https://www.google.com")]
    [InlineData("https://www.google.co.kr")]
    [InlineData("https://google.co.kr")]
    [InlineData("https://g.co")]
    public void IsAllowedTopLevelUri_AllowsRPlayHosts_And_EnforcesSourceIsolation(string url)
    {
        Assert.True(LoginSecurityPolicy.IsAllowedTopLevelUri(url, VideoSource.RPlay));

        // Cross-platform check: Chzzk and Soop must reject both RPlay hosts and regional Google hosts
        Assert.False(LoginSecurityPolicy.IsAllowedTopLevelUri(url, VideoSource.Chzzk));
        Assert.False(LoginSecurityPolicy.IsAllowedTopLevelUri(url, VideoSource.Soop));

        // YouTube isolates RPlay-only hosts, but accepts shared Google login hosts
        if (url.Contains("rplay.live"))
        {
            Assert.False(LoginSecurityPolicy.IsAllowedTopLevelUri(url, VideoSource.YouTube));
        }
        else
        {
            Assert.True(LoginSecurityPolicy.IsAllowedTopLevelUri(url, VideoSource.YouTube));
        }
    }

    [Theory]
    [InlineData("https://api.rplay.live.attacker.com")]
    [InlineData("https://attacker.com/rplay.live")]
    [InlineData("http://rplay.live")]
    [InlineData("https://rplay.live:8080/play/111111111111111111111111")]
    public void IsAllowedTopLevelUri_RejectsMaliciousOrNonDefaultPortUris(string url)
    {
        Assert.False(LoginSecurityPolicy.IsAllowedTopLevelUri(url, VideoSource.RPlay));
        Assert.False(LoginSecurityPolicy.IsAllowedTopLevelUri(url));
    }

    [Fact]
    public void IsAllowedCookieDomain_SegregatesRPlayFromOtherPlatforms()
    {
        // RPlay allowed domains
        Assert.True(LoginSecurityPolicy.IsAllowedCookieDomain(".rplay.live", VideoSource.RPlay));
        Assert.True(LoginSecurityPolicy.IsAllowedCookieDomain("rplay.live", VideoSource.RPlay));
        Assert.True(LoginSecurityPolicy.IsAllowedCookieDomain("www.rplay.live", VideoSource.RPlay));
        Assert.True(LoginSecurityPolicy.IsAllowedCookieDomain("api.rplay.live", VideoSource.RPlay));

        // RPlay must not accept other platforms' cookies
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain(".naver.com", VideoSource.RPlay));
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain(".youtube.com", VideoSource.RPlay));
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain(".sooplive.com", VideoSource.RPlay));
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain("sooplive.com", VideoSource.RPlay));
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain(".sooplive.co.kr", VideoSource.RPlay));

        // Other platforms must not accept RPlay cookies
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain(".rplay.live", VideoSource.Chzzk));
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain(".rplay.live", VideoSource.YouTube));
        Assert.False(LoginSecurityPolicy.IsAllowedCookieDomain(".rplay.live", VideoSource.Soop));
    }

    [Fact]
    public void SessionCookieStore_FiltersForeignCookiesForSource()
    {
        var store = new SessionCookieStore();
        var mixedCookies = new List<BrowserCookie>
        {
            new("_AUTHORIZATION_", "rplay_jwt_token", ".rplay.live", "/", true, true, 0),
            new("NID_AUT", "naver_auth_token", ".naver.com", "/", true, true, 0),
            new("AuthTicket", "soop_ticket", ".sooplive.com", "/", false, false, 0),
            new("SAPISID", "youtube_sid", ".youtube.com", "/", true, true, 0)
        };

        store.Set(VideoSource.RPlay, mixedCookies);
        var rplayCookies = store.Get(VideoSource.RPlay);

        Assert.Single(rplayCookies);
        Assert.Equal("_AUTHORIZATION_", rplayCookies[0].Name);
        Assert.Equal(".rplay.live", rplayCookies[0].Domain);
    }

    [Fact]
    public void IsRPlayAuthenticationCookie_StrictTokenNames()
    {
        // Verified accepted tokens
        Assert.True(PlaybackCookiePolicy.IsRPlayAuthenticationCookie(new("_AUTHORIZATION_", "jwt_val", ".rplay.live", "/", true, true, 0)));
        Assert.True(PlaybackCookiePolicy.IsRPlayAuthenticationCookie(new("_authorization_", "jwt_val", "api.rplay.live", "/", true, true, 0)));
        Assert.True(PlaybackCookiePolicy.IsRPlayAuthenticationCookie(new("Authorization", "jwt_val", "rplay.live", "/", true, true, 0)));
        Assert.True(PlaybackCookiePolicy.IsRPlayAuthenticationCookie(new("token", "jwt_val", ".rplay.live", "/", true, true, 0)));
        Assert.True(PlaybackCookiePolicy.IsRPlayAuthenticationCookie(new("access_token", "jwt_val", ".rplay.live", "/", true, true, 0)));

        // Unverified / non-bearer names are rejected
        Assert.False(PlaybackCookiePolicy.IsRPlayAuthenticationCookie(new("refresh_token", "val", ".rplay.live", "/", true, true, 0)));
        Assert.False(PlaybackCookiePolicy.IsRPlayAuthenticationCookie(new("refreshToken", "val", ".rplay.live", "/", true, true, 0)));
        Assert.False(PlaybackCookiePolicy.IsRPlayAuthenticationCookie(new("session", "val", ".rplay.live", "/", true, true, 0)));
        Assert.False(PlaybackCookiePolicy.IsRPlayAuthenticationCookie(new("connect.sid", "val", ".rplay.live", "/", true, true, 0)));

        // Empty value is rejected
        Assert.False(PlaybackCookiePolicy.IsRPlayAuthenticationCookie(new("_AUTHORIZATION_", "   ", ".rplay.live", "/", true, true, 0)));

        // Foreign domain is rejected
        Assert.False(PlaybackCookiePolicy.IsRPlayAuthenticationCookie(new("_AUTHORIZATION_", "jwt_val", ".naver.com", "/", true, true, 0)));
    }

    [Fact]
    public async Task CookieFileService_StrictlyHostScopesRPlayAuthCookies()
    {
        var cookieService = new CookieFileService();
        var cookies = new List<BrowserCookie>
        {
            new("_AUTHORIZATION_", "secret_token_val", ".rplay.live", "/", true, true, 0)
        };

        var path = await cookieService.CreateAsync(cookies, VideoSource.RPlay);
        Assert.NotNull(path);
        try
        {
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("#HttpOnly_api.rplay.live\tFALSE\t/\tTRUE\t0\t_AUTHORIZATION_\tsecret_token_val", content);
            Assert.DoesNotContain(".rplay.live\tTRUE", content);
        }
        finally
        {
            cookieService.Delete(path);
        }
    }

    [Fact]
    public void PlaybackCookiePolicy_CanExport_RPlay_StrictlyExportsOnlyVerifiedAuthCookies()
    {
        // Verified auth cookies are exportable
        Assert.True(PlaybackCookiePolicy.CanExport(new("_AUTHORIZATION_", "valid_jwt", "api.rplay.live", "/", true, true, 0)));
        Assert.True(PlaybackCookiePolicy.CanExport(new("token", "valid_jwt", "rplay.live", "/", true, true, 0)));

        // Non-auth cookies, refresh tokens, and session cookies are strictly NOT exportable for RPlay
        Assert.False(PlaybackCookiePolicy.CanExport(new("session", "session_data", "rplay.live", "/", true, true, 0)));
        Assert.False(PlaybackCookiePolicy.CanExport(new("refresh_token", "refresh_data", "rplay.live", "/", true, true, 0)));
        Assert.False(PlaybackCookiePolicy.CanExport(new("connect.sid", "sid_data", "rplay.live", "/", true, true, 0)));
        Assert.False(PlaybackCookiePolicy.CanExport(new("guest_device_id", "dev_123", "rplay.live", "/", true, true, 0)));
    }

    [Fact]
    public async Task CookieFileService_RPlay_ExcludesAllNonAuthCookiesFromExport()
    {
        var cookieService = new CookieFileService();
        var cookies = new List<BrowserCookie>
        {
            new("_AUTHORIZATION_", "secret_token_val", ".rplay.live", "/", true, true, 0),
            new("session", "leaked_session_data", ".rplay.live", "/", true, true, 0),
            new("refresh_token", "leaked_refresh_data", "api.rplay.live", "/", true, true, 0)
        };

        var path = await cookieService.CreateAsync(cookies, VideoSource.RPlay);
        Assert.NotNull(path);
        try
        {
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("api.rplay.live\tFALSE\t/\tTRUE\t0\t_AUTHORIZATION_\tsecret_token_val", content);
            Assert.DoesNotContain("leaked_session_data", content);
            Assert.DoesNotContain("leaked_refresh_data", content);
            Assert.DoesNotContain("session", content);
            Assert.DoesNotContain("refresh_token", content);
        }
        finally
        {
            cookieService.Delete(path);
        }
    }

    [Fact]
    public async Task WebVideoUrlService_RejectsRPlayUrlInGeneralWebTab()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            WebVideoUrlService.ValidateAsync("https://rplay.live/play/111111111111111111111111", CancellationToken.None));
        Assert.Contains("전용 탭", ex.Message);
    }

    [Fact]
    public void ParseVideoInfo_ParsesRPlayAudioOnlyContent()
    {
        using var json = JsonDocument.Parse("""
            {
                "id": "111111111111111111111111",
                "title": "Audio Drama Test",
                "extractor": "streamnest:rplay",
                "duration": 120.5,
                "vcodec": "none",
                "acodec": "aac",
                "formats": [
                    {
                        "format_id": "audio",
                        "url": "https://api.rplay.live/content/download?contentOid=111111111111111111111111",
                        "vcodec": "none",
                        "acodec": "aac",
                        "ext": "m4a"
                    }
                ]
            }
            """);

        // When allowAudioOnly is false (default, e.g. for Chzzk or YouTube), no fake format is created
        var videoWithoutAudioOnly = YtDlpService.ParseVideoInfo(json.RootElement, allowAudioOnly: false);
        Assert.Empty(videoWithoutAudioOnly.Formats);

        // When allowAudioOnly is true (for RPlay), audio-only format is parsed
        var video = YtDlpService.ParseVideoInfo(json.RootElement, allowAudioOnly: true);
        var format = Assert.Single(video.Formats);
        Assert.True(format.IsAudioOnly);
        Assert.True(format.ExpectsAudio);
        Assert.Equal("bestaudio/best", format.Selector);
        Assert.Contains("오디오", format.Label);
    }

    [Fact]
    public void ParseVideoInfo_ParsesRPlayVideoContent()
    {
        using var json = JsonDocument.Parse("""
            {
                "id": "222222222222222222222222",
                "title": "Video Content Test",
                "extractor": "streamnest:rplay",
                "duration": 300.0,
                "vcodec": "avc1.640028",
                "acodec": "mp4a.40.2",
                "formats": [
                    {
                        "format_id": "1080p",
                        "url": "https://api.rplay.live/content/hlsstream?contentOid=222222222222222222222222",
                        "height": 1080,
                        "fps": 30,
                        "tbr": 4500,
                        "vcodec": "avc1.640028",
                        "acodec": "mp4a.40.2",
                        "ext": "mp4"
                    }
                ]
            }
            """);

        var video = YtDlpService.ParseVideoInfo(json.RootElement);
        var format = Assert.Single(video.Formats);
        Assert.False(format.IsAudioOnly);
        Assert.True(format.ExpectsAudio);
        Assert.Equal(1080, format.Height);
        Assert.Contains("1080p", format.Label);
    }

    [Fact]
    public void WebViewCookieCollector_GetOrigins_ReturnsRPlayOrigins()
    {
        var origins = WebViewCookieCollector.GetOrigins(VideoSource.RPlay);
        Assert.Equal(3, origins.Count);
        Assert.Contains("https://rplay.live/", origins);
        Assert.Contains("https://www.rplay.live/", origins);
        Assert.Contains("https://api.rplay.live/", origins);
    }

    [Fact]
    public void PlaybackCookiePolicy_HasRPlayLoginSession_RejectsAnonymousAndAcceptsAuth()
    {
        // Anonymous / tracking / preference cookies must be rejected
        var anonymousCookies = new BrowserCookie[]
        {
            new("guest_device_id", "anon_12345", ".rplay.live", "/", true, false, 0),
            new("fam8_xuid", "xuid_999", "rplay.live", "/", true, false, 0),
            new("theme", "dark", "rplay.live", "/", true, false, 0)
        };
        Assert.False(PlaybackCookiePolicy.HasRPlayLoginSession(anonymousCookies));

        // Authentic authorization token must be accepted
        var authenticatedCookies = new BrowserCookie[]
        {
            new("guest_device_id", "anon_12345", ".rplay.live", "/", true, false, 0),
            new("_AUTHORIZATION_", RPlaySessionTests.Token(new { exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() }), ".rplay.live", "/", true, false, 0)
        };
        Assert.True(PlaybackCookiePolicy.HasRPlayLoginSession(authenticatedCookies));
    }

    [Fact]
    public void MediaVerificationService_AudioOnly_FailsWhenVideoStreamIsPresent()
    {
        using var jsonWithVideo = JsonDocument.Parse("""
            {
                "streams": [
                    { "codec_type": "video", "height": 1080, "duration": "120.0" },
                    { "codec_type": "audio", "duration": "120.0" }
                ],
                "format": { "duration": "120.0", "format_name": "mov,mp4,m4a,3gp,3g2,mj2" }
            }
            """);

        var expectation = MediaVerificationExpectation.ForAudio(120.0, "m4a");
        var ex = Assert.Throws<YtDlpException>(() => MediaVerificationService.ValidateMetadata(jsonWithVideo.RootElement, expectation));
        Assert.Contains("영상 스트림이 포함되어 있습니다", ex.Message);
    }

    [Fact]
    public void MediaVerificationService_AudioOnly_ValidatesContainerFormat()
    {
        using var rawAacJson = JsonDocument.Parse("""
            {
                "streams": [
                    { "codec_type": "audio", "duration": "120.0" }
                ],
                "format": { "duration": "120.0", "format_name": "aac" }
            }
            """);

        var expectation = MediaVerificationExpectation.ForAudio(120.0, "m4a");
        var ex = Assert.Throws<YtDlpException>(() => MediaVerificationService.ValidateMetadata(rawAacJson.RootElement, expectation));
        Assert.Contains("유효한 m4a 컨테이너가 아닙니다", ex.Message);

        using var validM4aJson = JsonDocument.Parse("""
            {
                "streams": [
                    { "codec_type": "audio", "duration": "120.0" }
                ],
                "format": { "duration": "120.0", "format_name": "mov,mp4,m4a,3gp,3g2,mj2" }
            }
            """);

        var warnings = MediaVerificationService.ValidateMetadata(validM4aJson.RootElement, expectation);
        Assert.Empty(warnings);
    }

    [Fact]
    public void RedactQueries_RedactsRPlaySignedUrlParameters()
    {
        var rawLog = "Downloading https://pb3.rplay.live/content/audio.m4a?Expires=1700000000&Signature=abc123xyz&Key-Pair-Id=K12345 to file";
        var redacted = WebVideoUrlService.RedactQueries(rawLog);
        Assert.DoesNotContain("Expires=", redacted);
        Assert.DoesNotContain("Signature=", redacted);
        Assert.DoesNotContain("Key-Pair-Id=", redacted);
        Assert.Contains("audio.m4a", redacted);
    }

    [Fact]
    public void MediaVerificationExpectation_CreatesProperConfigurations()
    {
        var audioExpectation = MediaVerificationExpectation.ForAudio(120.0);
        Assert.Equal(MediaKind.AudioOnly, audioExpectation.Kind);
        Assert.Null(audioExpectation.ExpectedHeight);
        Assert.True(audioExpectation.ExpectsAudio);
        Assert.Equal(120.0, audioExpectation.ExpectedDurationSeconds);
        Assert.Equal("m4a", audioExpectation.ExpectedContainer);

        var videoWithAudio = MediaVerificationExpectation.ForVideo(300.0, 1080, true);
        Assert.Equal(MediaKind.Video, videoWithAudio.Kind);
        Assert.Equal(1080, videoWithAudio.ExpectedHeight);
        Assert.True(videoWithAudio.ExpectsAudio);

        var videoWithoutAudio = MediaVerificationExpectation.ForVideo(300.0, 720, false);
        Assert.Equal(MediaKind.Video, videoWithoutAudio.Kind);
        Assert.Equal(720, videoWithoutAudio.ExpectedHeight);
        Assert.False(videoWithoutAudio.ExpectsAudio);
    }

    [Fact]
    public void BuildDownloadArguments_ConfiguresAudioExtractionForAudioOnly()
    {
        var audioFormat = new VideoFormatOption
        {
            Height = 0,
            Selector = "bestaudio/best",
            ExpectsAudio = true,
            IsAudioOnly = true,
            Label = "오디오 최고 음질 (자동)"
        };

        var audioArgs = YtDlpService.BuildDownloadArguments(
            "https://rplay.live/play/111111111111111111111111",
            @"C:\Downloads",
            @"C:\Temp\parts",
            audioFormat,
            @"C:\Temp\cookies.txt");

        Assert.Contains("--extract-audio", audioArgs);
        Assert.Contains("--audio-format", audioArgs);
        Assert.Contains("m4a", audioArgs);
        Assert.Contains("--audio-quality", audioArgs);
        Assert.DoesNotContain("--merge-output-format", audioArgs);

        var videoFormat = new VideoFormatOption
        {
            Height = 1080,
            Selector = "bestvideo[height<=1080]+bestaudio/best[height<=1080]/best",
            ExpectsAudio = true,
            IsAudioOnly = false,
            Label = "1080p"
        };

        var videoArgs = YtDlpService.BuildDownloadArguments(
            "https://rplay.live/play/222222222222222222222222",
            @"C:\Downloads",
            @"C:\Temp\parts",
            videoFormat,
            @"C:\Temp\cookies.txt");

        Assert.DoesNotContain("--extract-audio", videoArgs);
        Assert.Contains("--merge-output-format", videoArgs);
        Assert.Contains("mp4", videoArgs);
    }
}
