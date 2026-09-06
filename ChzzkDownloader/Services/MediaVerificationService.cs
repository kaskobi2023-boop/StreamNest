using System.Diagnostics;
using System.Text;
using System.Text.Json;

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

    public static async Task VerifyAsync(string path, double? expectedDuration, int? expectedHeight,
        bool expectsAudio, CancellationToken token)
    {
        var probe = await RunAsync(ToolLocator.FfprobePath,
            ["-v", "error", "-show_entries", "format=duration:stream=codec_type,height,duration", "-of", "json", path], token);
        if (probe.Code != 0) throw new YtDlpException("다운로드 파일의 미디어 정보를 읽지 못했습니다. 완료 처리하지 않았습니다.");
        using var document = JsonDocument.Parse(probe.Output);
        ValidateMetadata(document.RootElement, expectedDuration, expectedHeight, expectsAudio);
        // Decode, rather than stream-copy, so corrupt packets cannot silently
        // pass the completion gate. The user may cancel this phase as well.
        var decoded = await RunAsync(ToolLocator.FfmpegPath,
            ["-v", "error", "-xerror", "-nostdin", "-i", path, "-map", "0:v:0", "-map", "0:a?", "-f", "null", "-"], token);
        if (decoded.Code != 0 || !string.IsNullOrWhiteSpace(decoded.Error))
            throw new YtDlpException("다운로드 파일 전체 검사에서 손상이 발견되었습니다. 완료 처리하지 않았습니다.");
    }

    internal static void ValidateMetadata(JsonElement root, double? expectedDuration, int? expectedHeight, bool expectsAudio)
    {
        if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            throw new YtDlpException("검증 실패: 영상 스트림이 없습니다.");
        var video = streams.EnumerateArray().FirstOrDefault(stream => stream.TryGetProperty("codec_type", out var type) && type.GetString() == "video");
        if (video.ValueKind == JsonValueKind.Undefined) throw new YtDlpException("검증 실패: 영상 스트림이 없습니다.");
        if (expectedHeight is > 0 && (!video.TryGetProperty("height", out var height) || height.GetInt32() != expectedHeight))
            throw new YtDlpException("검증 실패: 저장된 영상의 화질이 선택한 화질과 다릅니다.");
        if (expectsAudio && !streams.EnumerateArray().Any(stream => stream.TryGetProperty("codec_type", out var type) && type.GetString() == "audio"))
            throw new YtDlpException("검증 실패: 필요한 음성 스트림이 누락되었습니다.");
        // Compare video metadata against the video track, not the container's
        // longest audio/edit-list span. That span can mask a short video or
        // falsely exceed the tolerance because of audio alignment padding.
        var seconds = ReadDuration(video);
        if (seconds is null && root.TryGetProperty("format", out var format)) seconds = ReadDuration(format);
        if (seconds is null)
            throw new YtDlpException("검증 실패: 재생 길이를 확인하지 못했습니다.");
        // Small muxing/rounding differences are allowed, not missing sections.
        if (expectedDuration is > 0 && double.IsFinite(expectedDuration.Value) &&
            Math.Abs(seconds.Value - expectedDuration.Value) > Math.Max(0.5, Math.Min(3, expectedDuration.Value * 0.01)))
            throw new YtDlpException("검증 실패: 저장된 파일의 길이가 분석한 영상과 다릅니다. 조각 누락 여부를 확인해주세요.");
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
