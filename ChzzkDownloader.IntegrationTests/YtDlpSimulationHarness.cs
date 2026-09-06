using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ChzzkDownloader.Models;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.IntegrationTests;

internal static class YtDlpSimulationHarness
{
    public static async Task SimulateAsync(
        string url,
        string formatSelector,
        IReadOnlyCollection<BrowserCookie> cookies,
        CancellationToken cancellationToken)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"StreamNest-Simulation-{Guid.NewGuid():N}");
        var cookieService = new CookieFileService(Path.Combine(temporaryRoot, "Cookies"));
        var cookiePath = await cookieService.CreateAsync(cookies);
        try
        {
            Assert.False(string.IsNullOrWhiteSpace(cookiePath));
            ToolLocator.EnsureToolsExist();
            var startInfo = new ProcessStartInfo
            {
                FileName = ToolLocator.YtDlpPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            string[] arguments =
            [
                "--no-config",
                "--no-playlist",
                "--simulate",
                "--js-runtimes", $"node:{ToolLocator.NodePath}",
                "--ffmpeg-location", ToolLocator.ToolsDirectory,
                "--format", formatSelector,
                "--cookies", cookiePath!,
                url
            ];
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = startInfo };
            Assert.True(process.Start());
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;
            var safeError = error.Replace(cookiePath!, "[temporary cookie file]", StringComparison.OrdinalIgnoreCase);

            Assert.True(
                process.ExitCode == 0,
                $"yt-dlp stream selection simulation failed.\n{safeError}\n{output}");
        }
        finally
        {
            cookieService.Delete(cookiePath);
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    public static async Task<DownloadSampleResult> DownloadSampleAsync(
        string url,
        string formatSelector,
        IReadOnlyCollection<BrowserCookie> cookies,
        TimeSpan sampleDuration,
        CancellationToken cancellationToken)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"StreamNest-ActualDownload-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(temporaryRoot, "Output");
        var cookieService = new CookieFileService(Path.Combine(temporaryRoot, "Cookies"));
        string? cookiePath = null;
        try
        {
            Directory.CreateDirectory(outputDirectory);
            cookiePath = await cookieService.CreateAsync(cookies);
            Assert.False(string.IsNullOrWhiteSpace(cookiePath));
            ToolLocator.EnsureToolsExist();

            var startInfo = CreateStartInfo(ToolLocator.YtDlpPath);
            string[] arguments =
            [
                "--no-config",
                "--no-playlist",
                "--js-runtimes", $"node:{ToolLocator.NodePath}",
                "--ffmpeg-location", ToolLocator.ToolsDirectory,
                "--windows-filenames",
                "--paths", outputDirectory,
                "--output", "sample.%(ext)s",
                "--merge-output-format", "mp4",
                "--download-sections", $"*0-{sampleDuration.TotalSeconds:0.###}",
                "--print", "after_move:FINAL|%(filepath)s",
                "--format", formatSelector,
                "--cookies", cookiePath!,
                url
            ];
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            var processResult = await RunProcessAsync(startInfo, cancellationToken);
            var safeError = processResult.StandardError.Replace(
                cookiePath!,
                "[temporary cookie file]",
                StringComparison.OrdinalIgnoreCase);
            Assert.True(
                processResult.ExitCode == 0,
                $"yt-dlp actual sample download failed.\n{safeError}\n{processResult.StandardOutput}");

            var finalPath = processResult.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(line => line.StartsWith("FINAL|", StringComparison.Ordinal))?[6..];
            if (string.IsNullOrWhiteSpace(finalPath) || !File.Exists(finalPath))
            {
                finalPath = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(path => new FileInfo(path).Length > 0);
            }

            Assert.False(string.IsNullOrWhiteSpace(finalPath));
            Assert.True(File.Exists(finalPath));
            var bytes = new FileInfo(finalPath).Length;
            var duration = await ProbeDurationAsync(finalPath, cancellationToken);
            return new DownloadSampleResult(bytes, duration);
        }
        finally
        {
            cookieService.Delete(cookiePath);
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static async Task<double> ProbeDurationAsync(string mediaPath, CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(ToolLocator.FfprobePath);
        string[] arguments =
        [
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "json",
            mediaPath
        ];
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var result = await RunProcessAsync(startInfo, cancellationToken);
        Assert.True(result.ExitCode == 0, $"ffprobe failed.\n{result.StandardError}");
        using var document = JsonDocument.Parse(result.StandardOutput);
        var durationText = document.RootElement
            .GetProperty("format")
            .GetProperty("duration")
            .GetString();
        Assert.True(double.TryParse(
            durationText,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var duration));
        return duration;
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath) => new()
    {
        FileName = executablePath,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };

    private static async Task<ProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    internal sealed record DownloadSampleResult(long Bytes, double DurationSeconds);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
