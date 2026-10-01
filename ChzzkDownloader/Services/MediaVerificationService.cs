using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ChzzkDownloader.Models;

namespace ChzzkDownloader.Services;

internal static class MediaVerificationService
{
    internal static void VerifyFragments(string partialDirectory)
    {
        // Retain native HLS/DASH fragments until validation. A muxer can silently
        // discard an HTTP 200 HTML/text error between otherwise valid segments.
        Span<byte> prefix = stackalloc byte[512];
        foreach (var path in Directory.EnumerateFiles(partialDirectory, "*-Frag*", SearchOption.AllDirectories))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), @"-Frag\d+$")) continue;
            using var input = File.OpenRead(path);
            var count = input.Read(prefix);
            if (count == 0 || LooksLikeTextFragment(prefix[..count]))
                throw new YtDlpException("검증 실패: 영상 조각 대신 빈 응답 또는 오류 문서가 수신되었습니다. 완료 처리하지 않았습니다.");
        }
    }

    internal static bool LooksLikeTextFragment(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < 32) return false;
        foreach (var value in prefix)
            if (value is not (9 or 10 or 13) && value is not (>= 32 and <= 126)) return false;
        return true;
    }

    public static Task<IReadOnlyList<string>> VerifyAsync(string path, double? expectedDuration, int? expectedHeight,
        bool expectsAudio, CancellationToken token)
        => VerifyAsync(path, MediaVerificationExpectation.ForVideo(expectedDuration, expectedHeight, expectsAudio), token);

    public static async Task<IReadOnlyList<string>> VerifyAsync(string path, MediaVerificationExpectation expectation,
        CancellationToken token)
    {
        // Fast completion check: inspect container/stream metadata only. Never launch
        // FFmpeg or scan/decode the whole file here; cost must not scale with playback length.
        token.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        (int Code, string Output, string Error) probe;
        try
        {
            probe = await RunAsync(ToolLocator.FfprobePath,
                ["-v", "error", "-show_entries", "format=duration,format_name:stream=codec_type,codec_name,height,duration", "-of", "json", path], timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new YtDlpException("파일 정보 확인 시간이 초과되었습니다. 저장 장치 상태를 확인하고 다시 시도해주세요.");
        }
        if (probe.Code != 0) throw new YtDlpException("다운로드 파일의 미디어 정보를 읽지 못했습니다. 완료 처리하지 않았습니다.");
        using var document = JsonDocument.Parse(probe.Output);
        var warnings = ValidateMetadata(document.RootElement, expectation).ToList();
        if (!string.IsNullOrWhiteSpace(probe.Error))
            warnings.Add("파일 정보를 읽는 중 오류가 보고되었습니다. 저장된 영상의 재생 상태를 확인해주세요.");
        token.ThrowIfCancellationRequested();
        return warnings;
    }

    internal static IReadOnlyList<string> ValidateMetadata(JsonElement root, double? expectedDuration, int? expectedHeight, bool expectsAudio)
        => ValidateMetadata(root, MediaVerificationExpectation.ForVideo(expectedDuration, expectedHeight, expectsAudio));

    internal static IReadOnlyList<string> ValidateMetadata(JsonElement root, MediaVerificationExpectation expectation)
    {
        if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            throw new YtDlpException(expectation.Kind == MediaKind.AudioOnly ? "검증 실패: 음성 스트림이 없습니다." : "검증 실패: 영상 스트림이 없습니다.");

        double? seconds = null;
        if (expectation.Kind == MediaKind.AudioOnly)
        {
            var hasVideo = streams.EnumerateArray().Any(stream => stream.TryGetProperty("codec_type", out var type) &&
                string.Equals(type.GetString(), "video", StringComparison.OrdinalIgnoreCase));
            if (hasVideo)
                throw new YtDlpException("검증 실패: 오디오 전용 파일에 영상 스트림이 포함되어 있습니다. 완료 처리하지 않았습니다.");

            var audio = streams.EnumerateArray().FirstOrDefault(stream => stream.TryGetProperty("codec_type", out var type) &&
                string.Equals(type.GetString(), "audio", StringComparison.OrdinalIgnoreCase));
            if (audio.ValueKind == JsonValueKind.Undefined) throw new YtDlpException("검증 실패: 필요한 음성 스트림이 누락되었습니다.");

            if (!string.IsNullOrWhiteSpace(expectation.ExpectedContainer))
            {
                if (!root.TryGetProperty("format", out var formatElement) ||
                    !formatElement.TryGetProperty("format_name", out var formatNameProp))
                {
                    throw new YtDlpException("검증 실패: 오디오 파일의 컨테이너 포맷을 확인할 수 없습니다. 완료 처리하지 않았습니다.");
                }

                var formatName = formatNameProp.GetString() ?? string.Empty;
                var validContainers = expectation.ExpectedContainer.ToLowerInvariant() switch
                {
                    "m4a" or "mp4" => ["mp4", "m4a", "mov", "3gp", "3g2", "mj2"],
                    "mp3" => ["mp3"],
                    "aac" => ["aac", "adts"],
                    "flac" => ["flac"],
                    "wav" => ["wav"],
                    _ => new[] { expectation.ExpectedContainer.ToLowerInvariant() }
                };

                var formatParts = formatName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (!formatParts.Any(part => validContainers.Contains(part, StringComparer.OrdinalIgnoreCase)))
                {
                    throw new YtDlpException($"검증 실패: 저장된 파일이 유효한 {expectation.ExpectedContainer} 컨테이너가 아닙니다 (감지된 포맷: {formatName}). 완료 처리하지 않았습니다.");
                }
            }

            seconds = ReadDuration(audio);
        }
        else
        {
            var video = streams.EnumerateArray().FirstOrDefault(stream => stream.TryGetProperty("codec_type", out var type) && type.GetString() == "video");
            if (video.ValueKind == JsonValueKind.Undefined) throw new YtDlpException("검증 실패: 영상 스트림이 없습니다.");
            if (expectation.ExpectedHeight is > 0 && (!video.TryGetProperty("height", out var height) || height.GetInt32() != expectation.ExpectedHeight))
                throw new YtDlpException("검증 실패: 저장된 영상의 화질이 선택한 화질과 다릅니다.");
            if (expectation.ExpectsAudio && !streams.EnumerateArray().Any(stream => stream.TryGetProperty("codec_type", out var type) && type.GetString() == "audio"))
                throw new YtDlpException("검증 실패: 필요한 음성 스트림이 누락되었습니다.");
            seconds = ReadDuration(video);
        }

        if (seconds is null && root.TryGetProperty("format", out var format)) seconds = ReadDuration(format);
        if (seconds is null)
            return ["재생 길이를 확인하지 못했습니다. 파일을 저장했지만 전체 분량 여부는 재생으로 확인해주세요."];

        var expectedDuration = expectation.ExpectedDurationSeconds;
        if (expectedDuration is > 0 && double.IsFinite(expectedDuration.Value) &&
            Math.Abs(seconds.Value - expectedDuration.Value) > Math.Max(0.5, Math.Min(30, expectedDuration.Value * 0.01)))
            return [$"길이 차이: 예상 {expectedDuration.Value:0.###}초 / 저장 {seconds.Value:0.###}초. 메타데이터 오차 또는 일부 누락 가능성이 있으니 전체 분량을 확인해주세요."];
        return [];
    }

    private static double? ReadDuration(JsonElement element) =>
        element.TryGetProperty("duration", out var duration) &&
        double.TryParse(duration.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) && value > 0 ? value : null;

    private static async Task<(int Code, string Output, string Error)> RunAsync(string executable, string[] args, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("미디어 검증 도구를 시작하지 못했습니다.");
        var stdout = ReadTailAsync(process.StandardOutput);
        var stderr = ReadTailAsync(process.StandardError);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        return (process.ExitCode, await stdout, await stderr);
    }

    private static async Task<string> ReadTailAsync(StreamReader reader)
    {
        var tail = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            tail.Append(buffer, 0, count);
            if (tail.Length > 32768) tail.Remove(0, tail.Length - 32768);
        }
        return tail.ToString();
    }
}
