using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ChzzkDownloader.Models;
using Microsoft.Web.WebView2.Core;

namespace ChzzkDownloader.Services;

public sealed class ToolDiagnosticService
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(8);

    public async Task<EnvironmentDiagnosticReport> RunAsync(
        string outputFolder,
        CancellationToken cancellationToken = default)
    {
        var checks = new[]
        {
            CheckExecutableAsync("yt-dlp", ToolLocator.YtDlpPath, ["--version"], cancellationToken),
            CheckExecutableAsync("FFmpeg", ToolLocator.FfmpegPath, ["-version"], cancellationToken),
            CheckExecutableAsync("ffprobe", ToolLocator.FfprobePath, ["-version"], cancellationToken),
            CheckExecutableAsync("YouTube JS 런타임", ToolLocator.NodePath, ["--version"], cancellationToken),
            CheckDirectoryAsync("저장 폴더", outputFolder, blocksDownloads: true, cancellationToken),
            CheckDirectoryAsync("임시 조각 폴더", ToolLocator.PartialDownloadsDirectory, blocksDownloads: true, cancellationToken)
        };

        var items = (await Task.WhenAll(checks)).ToList();
        items.Add(CheckWebView2());
        return new EnvironmentDiagnosticReport { Items = items };
    }

    private static async Task<DiagnosticItem> CheckExecutableAsync(
        string name,
        string path,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return new DiagnosticItem(name, DiagnosticState.Error, $"파일을 찾을 수 없습니다: {path}", true);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                return new DiagnosticItem(name, DiagnosticState.Error, "프로세스를 시작하지 못했습니다.", true);

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProcessTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new DiagnosticItem(name, DiagnosticState.Error, "8초 안에 응답하지 않았습니다.", true);
            }

            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                var detail = FirstLine(error) ?? FirstLine(output) ?? $"종료 코드 {process.ExitCode}";
                return new DiagnosticItem(name, DiagnosticState.Error, detail, true);
            }

            var version = FirstLine(output) ?? FirstLine(error) ?? "실행 확인됨";
            return new DiagnosticItem(name, DiagnosticState.Ready, version);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception exception)
        {
            return new DiagnosticItem(
                name,
                DiagnosticState.Error,
                $"실행이 차단되었거나 손상되었습니다: {exception.Message}",
                true);
        }
        catch (Exception exception)
        {
            return new DiagnosticItem(name, DiagnosticState.Error, exception.Message, true);
        }
    }

    private static async Task<DiagnosticItem> CheckDirectoryAsync(
        string name,
        string path,
        bool blocksDownloads,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new DiagnosticItem(name, DiagnosticState.Error, "폴더 경로가 비어 있습니다.", blocksDownloads);

        try
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(fullPath);
            var probePath = Path.Combine(fullPath, $".chzzk-write-check-{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(
                             probePath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             1,
                             FileOptions.Asynchronous | FileOptions.DeleteOnClose))
            {
                await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
            }

            var freeSpace = TryGetFreeSpace(fullPath);
            var detail = freeSpace.HasValue
                ? $"쓰기 가능 · 여유 공간 {FormatBytes(freeSpace.Value)}"
                : "쓰기 가능";
            var state = freeSpace is < 2L * 1024 * 1024 * 1024
                ? DiagnosticState.Warning
                : DiagnosticState.Ready;
            if (state == DiagnosticState.Warning)
                detail += " · 큰 영상을 받기에는 공간이 부족할 수 있습니다.";
            return new DiagnosticItem(name, state, detail, blocksDownloads);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new DiagnosticItem(name, DiagnosticState.Error, $"쓰기 불가: {exception.Message}", blocksDownloads);
        }
    }

    private static DiagnosticItem CheckWebView2()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(version)
                ? new DiagnosticItem("WebView2", DiagnosticState.Error, "Runtime을 찾을 수 없습니다.")
                : new DiagnosticItem("WebView2", DiagnosticState.Ready, version);
        }
        catch (Exception exception)
        {
            return new DiagnosticItem("WebView2", DiagnosticState.Error, $"로그인 브라우저 사용 불가: {exception.Message}");
        }
    }

    private static long? TryGetFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            return string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstLine(string value) => value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault();

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // 진단 프로세스가 이미 종료된 경우 무시한다.
        }
    }
}
