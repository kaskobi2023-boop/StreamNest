using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChzzkDownloader.Models;

namespace ChzzkDownloader.Services;

public sealed partial class YtDlpService
{
    internal const int ConcurrentFragmentCount = 32;
    internal const int WebVideoLimit = 20;

    public async Task<IReadOnlyList<WebVideoItem>> AnalyzeWebAsync(string url, CancellationToken cancellationToken = default,
        bool includePagePathInReferer = false)
    {
        var uri = await _validateWebUrl(url, cancellationToken);
        ToolLocator.EnsureToolsExist();
        ToolLocator.EnsureWebPluginExists();
        var arguments = BuildWebAnalyzeArguments(uri.AbsoluteUri, includePagePathInReferer);
        var result = await RunCaptureAsync(arguments, cancellationToken);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new YtDlpException(DescribeWebAnalysisError(result.StandardError));
        using var document = JsonDocument.Parse(result.StandardOutput);
        var videos = ParseWebVideos(document.RootElement, uri.AbsoluteUri, includePagePathInReferer);
        if (videos.Count == 0)
            throw new YtDlpException(DescribeWebAnalysisError(result.StandardError));
        return videos;
    }

    internal static IReadOnlyList<WebVideoItem> ParseWebVideos(JsonElement root, string? pageUrl = null,
        bool includePagePathInReferer = false)
    {
        var result = new List<WebVideoItem>();
        if (root.ValueKind != JsonValueKind.Object) return result;
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            var position = 0;
            foreach (var entry in entries.EnumerateArray().Take(WebVideoLimit))
            {
                position++;
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var index = ReadLong(entry, "playlist_index");
                Add(entry, index is > 0 and <= int.MaxValue ? (int)index.Value : position);
            }
        }
        else Add(root, null);
        return result;

        void Add(JsonElement entry, int? index)
        {
            if (ReadBoolean(entry, "is_live") || ReadBoolean(entry, "has_drm") ||
                ReadString(entry, "live_status") is "is_live" or "is_upcoming" ||
                entry.TryGetProperty("entries", out _)) return;
            var video = ParseVideoInfo(entry, allowUnknownWebVideo: true);
            if (video.Formats.Count > 0 && !string.IsNullOrWhiteSpace(video.Id))
                result.Add(new WebVideoItem(video, index) {
                    Snapshot = pageUrl is null ? null : CreateWebSnapshot(entry, pageUrl, includePagePathInReferer)
                });
        }
    }

    private readonly CookieFileService _cookieFileService;
    private readonly string _partialDownloadsRoot;
    private readonly Func<string, CancellationToken, Task<Uri>> _validateWebUrl = WebVideoUrlService.ValidateAsync;

    public YtDlpService(CookieFileService cookieFileService, string? partialDownloadsRoot = null)
    {
        _cookieFileService = cookieFileService;
        _partialDownloadsRoot = partialDownloadsRoot ?? ToolLocator.PartialDownloadsDirectory;
    }

    // Isolated integration tests can supply a validator for their own loopback
    // server. All public constructors retain the public-HTTPS production policy.
    internal YtDlpService(CookieFileService cookieFileService, string partialDownloadsRoot,
        Func<string, CancellationToken, Task<Uri>> validateWebUrl) : this(cookieFileService, partialDownloadsRoot)
    {
        _validateWebUrl = validateWebUrl ?? throw new ArgumentNullException(nameof(validateWebUrl));
    }

    public async Task<VideoInfo> AnalyzeAsync(
        string url,
        IReadOnlyCollection<BrowserCookie>? cookies,
        CancellationToken cancellationToken = default)
    {
        ToolLocator.EnsureToolsExist();
        var cookiePath = await _cookieFileService.CreateAsync(cookies);
        try
        {
            var arguments = new List<string>
            {
                "--no-config",
                "--no-playlist",
                "--plugin-dirs", ToolLocator.YtDlpPluginDirectory,
                "--encoding", "utf-8",
                "--skip-download",
                // Metadata extraction must not fail just because yt-dlp's default
                // format selector cannot choose a stream. Restricted YouTube videos
                // can still expose their format list after authentication, and the
                // app builds its own selectors from that list below.
                "--ignore-no-formats-error",
                "--js-runtimes", $"node:{ToolLocator.NodePath}",
                "--dump-single-json",
                "--ffmpeg-location", ToolLocator.ToolsDirectory
            };
            AddCookies(arguments, cookiePath);
            arguments.Add(url);

            var result = await RunCaptureAsync(arguments, cancellationToken);
            if (result.ExitCode != 0)
                throw new YtDlpException(CleanError(result.StandardError, cookiePath));

            using var document = JsonDocument.Parse(result.StandardOutput);
            var video = ParseVideoInfo(document.RootElement);
            if (video.Formats.Count == 0)
            {
                var detail = string.IsNullOrWhiteSpace(result.StandardError)
                    ? string.Empty
                    : $"{Environment.NewLine}{CleanError(result.StandardError, cookiePath)}";
                throw new YtDlpException(
                    $"다운로드 가능한 영상 스트림을 찾지 못했습니다. yt-dlp와 YouTube JavaScript 런타임을 확인해 주세요.{detail}");
            }

            return video;
        }
        finally
        {
            _cookieFileService.Delete(cookiePath);
        }
    }

    private async Task<string?> DownloadAttemptAsync(
        string url,
        string outputFolder,
        VideoFormatOption format,
        string partialDirectory,
        IReadOnlyCollection<BrowserCookie>? cookies,
        IProgress<DownloadProgressInfo>? progress,
        Action<string>? log,
        CancellationToken cancellationToken = default)
    {
        if (format.IsGeneralWeb)
        {
            url = (await _validateWebUrl(url, cancellationToken)).AbsoluteUri;
            ToolLocator.EnsureWebPluginExists();
            cookies = []; // Never send existing platform sessions to general websites.
        }
        ToolLocator.EnsureToolsExist();
        Directory.CreateDirectory(outputFolder);
        Directory.CreateDirectory(partialDirectory);
        var attemptDirectory = Path.Combine(partialDirectory, "active");
        Directory.CreateDirectory(attemptDirectory);
        var cookiePath = await _cookieFileService.CreateAsync(cookies);
        string? infoJsonPath = null;
        try
        {
            if (format.IsGeneralWeb && format.WebSnapshot is { } snapshot)
            {
                infoJsonPath = Path.Combine(partialDirectory, $"request-{Guid.NewGuid():N}.json");
                await File.WriteAllTextAsync(infoJsonPath, snapshot.InfoJson, new UTF8Encoding(false), cancellationToken);
            }
            // Never let an existing final file satisfy a new quality request.
            // Every source downloads into its job's staging area first.
            var engineOutputFolder = Path.Combine(attemptDirectory, "unverified");
            Directory.CreateDirectory(engineOutputFolder);
            var arguments = BuildDownloadArguments(url, engineOutputFolder, attemptDirectory, format, cookiePath, infoJsonPath);

            var startInfo = CreateStartInfo(arguments);
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var errorBuilder = new StringBuilder();
            var finalPathLock = new object();
            var progressAggregator = new DownloadProgressAggregator(format.Selector);
            string? finalPath = null;

            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (string.IsNullOrWhiteSpace(eventArgs.Data))
                    return;
                var line = eventArgs.Data.Trim();
                if (TryReportProgress(line, progressAggregator, progress))
                {
                }
                else if (line.StartsWith("FINAL|", StringComparison.Ordinal))
                {
                    lock (finalPathLock)
                        finalPath = line[6..].Trim();
                }
                else
                {
                    log?.Invoke(format.IsGeneralWeb ? WebVideoUrlService.RedactQueries(line) : line);
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (string.IsNullOrWhiteSpace(eventArgs.Data))
                    return;
                var line = eventArgs.Data.Trim();
                if (TryReportProgress(line, progressAggregator, progress))
                    return;
                lock (errorBuilder)
                    errorBuilder.AppendLine(eventArgs.Data);
                var safeLine = CleanError(line, cookiePath);
                if (!string.IsNullOrWhiteSpace(safeLine))
                    log?.Invoke(format.IsGeneralWeb ? WebVideoUrlService.RedactQueries(safeLine) : safeLine);
            };

            if (!process.Start())
                throw new InvalidOperationException("다운로드 프로세스를 시작하지 못했습니다.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
                process.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await WaitForExitAfterKillAsync(process);
                CompleteProcessExit(process);
                throw;
            }

            if (process.ExitCode != 0)
            {
                string error;
                lock (errorBuilder)
                    error = errorBuilder.ToString();
                // A failed concurrent-fragment checkpoint may have advanced
                // beyond an unavailable fragment. Never resume that attempt.
                QuarantineAttempt(partialDirectory, attemptDirectory);
                throw new YtDlpException(format.IsGeneralWeb
                    ? WebVideoUrlService.RedactQueries(CleanError(error, cookiePath)) : CleanError(error, cookiePath));
            }

            string? completedPath;
            lock (finalPathLock)
                completedPath = finalPath;

            if (format.IsGeneralWeb && string.IsNullOrWhiteSpace(completedPath))
                throw new YtDlpException("선택한 영상이 변경되었거나 다운로드되지 않았습니다. 페이지를 다시 분석해주세요.");

            completedPath = ResolveCompletedPath(completedPath, engineOutputFolder, url, format.ExpectedVideoId);

            if (string.IsNullOrWhiteSpace(completedPath))
                throw new YtDlpException("다운로드 프로세스는 종료되었지만 최종 파일 경로를 확인하지 못했습니다. 임시 파일은 이어받기를 위해 보관했습니다.");

            var fullPath = Path.GetFullPath(completedPath);
            if (!File.Exists(fullPath))
                throw new YtDlpException($"다운로드 프로세스는 종료되었지만 최종 파일을 찾지 못했습니다: {fullPath}");
            if (new FileInfo(fullPath).Length <= 0)
                throw new YtDlpException($"완료된 파일의 크기가 0바이트입니다: {fullPath}");

            progress?.Report(new DownloadProgressInfo(null, string.Empty, string.Empty, "파일 무결성 검사 중…"));
            try
            {
                MediaVerificationService.VerifyFragments(attemptDirectory);
                await MediaVerificationService.VerifyAsync(fullPath,
                    format.WebSnapshot?.DurationSeconds ?? format.ExpectedDurationSeconds,
                    format.Height, format.WebSnapshot?.ExpectsAudio ?? format.ExpectsAudio, cancellationToken);
            }
            catch (YtDlpException)
            {
                // Keep failed evidence separate so no source reuses a corrupt
                // completed file or fragments. Existing user files stay intact.
                if (infoJsonPath is not null) File.Delete(infoJsonPath);
                QuarantineAttempt(partialDirectory, attemptDirectory);
                throw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            fullPath = PublishVerifiedWebFile(fullPath, outputFolder, format.Height);
            log?.Invoke("파일 검증 통과: 영상·음성 스트림, 길이, 전체 디코딩 확인");
            if (infoJsonPath is not null) File.Delete(infoJsonPath);
            progress?.Report(new DownloadProgressInfo(100, string.Empty, string.Empty, "완료"));
            var cleanupResult = await DeletePartialDirectoryAsync(partialDirectory);
            if (!cleanupResult.Succeeded)
                log?.Invoke($"다운로드는 완료했지만 임시 조각 폴더를 정리하지 못했습니다: {cleanupResult.Error}");
            return fullPath;
        }
        finally
        {
            if (infoJsonPath is not null && File.Exists(infoJsonPath)) File.Delete(infoJsonPath);
            _cookieFileService.Delete(cookiePath);
        }
    }

    public Task<PartialCleanupResult> DeletePartialFilesAsync(
        string url,
        string outputFolder,
        VideoFormatOption format) =>
        DeletePartialDirectoryAsync(GetPartialDirectory(url, outputFolder, format));

    private string GetPartialDirectory(string url, string outputFolder, VideoFormatOption format)
    {
        var keySource = $"{url.Trim()}\n{Path.GetFullPath(outputFolder)}\n{format.Selector}";
        if (format.IsGeneralWeb)
            keySource += $"\nweb:{format.WebPlaylistIndex}:{format.ExpectedVideoId}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keySource)));
        return Path.Combine(_partialDownloadsRoot, hash[..24]);
    }

    internal static string? ResolveCompletedPath(string? reportedPath, string outputFolder, string url)
        => ResolveCompletedPath(reportedPath, outputFolder, url, null);

    private static string? ResolveCompletedPath(string? reportedPath, string outputFolder, string url, string? expectedId)
    {
        if (!string.IsNullOrWhiteSpace(reportedPath))
        {
            try
            {
                var fullReportedPath = Path.GetFullPath(reportedPath);
                if (File.Exists(fullReportedPath))
                    return fullReportedPath;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Fall back to the ASCII video-id marker below when a process output
                // path was decoded with the wrong Windows code page.
            }
        }

        if (expectedId is null && !VideoUrlService.TryParse(url, out _))
            return null;

        try
        {
            var fullOutputFolder = Path.GetFullPath(outputFolder);
            if (!Directory.Exists(fullOutputFolder))
                return null;

            var idMarker = $"[{expectedId ?? (VideoUrlService.TryParse(url, out var videoUrl) ? videoUrl.VideoId : string.Empty)}]";
            return Directory.EnumerateFiles(fullOutputFolder, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetFileName(path).Contains(idMarker, StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                               !path.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                               new FileInfo(path).Length > 0)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(Path.GetFullPath)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private async Task<PartialCleanupResult> DeletePartialDirectoryAsync(string directory)
    {
        try
        {
            var root = Path.GetFullPath(_partialDownloadsRoot);
            var candidate = Path.GetFullPath(directory);
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("앱의 임시 다운로드 폴더가 아닌 경로는 삭제할 수 없습니다.");

            int[] retryDelaysMilliseconds = [0, 150, 350, 750, 1500, 2500];
            Exception? lastException = null;
            foreach (var delay in retryDelaysMilliseconds)
            {
                if (delay > 0)
                    await Task.Delay(delay);

                try
                {
                    if (!Directory.Exists(candidate))
                        return new PartialCleanupResult(true, null);

                    foreach (var file in Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(candidate, recursive: true);
                    return new PartialCleanupResult(true, null);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    lastException = exception;
                }
            }

            return new PartialCleanupResult(
                false,
                lastException?.Message ?? "임시 다운로드 폴더를 삭제하지 못했습니다.");
        }
        catch (Exception exception)
        {
            return new PartialCleanupResult(false, exception.Message);
        }
    }

    private static void CompleteProcessExit(Process process)
    {
        try
        {
            if (process.HasExited)
                process.WaitForExit();
        }
        catch
        {
            // 취소 결과를 바꾸지 않으면서 비동기 출력 핸들러 정리만 시도한다.
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        catch
        {
            // 종료 과정의 오류는 원래 취소 흐름을 유지하기 위해 무시한다.
        }
    }

    internal static VideoInfo ParseVideoInfo(JsonElement root, bool allowUnknownWebVideo = false)
    {
        var duration = ReadDouble(root, "duration");
        var formats = new List<(int Height, double Fps, double Bitrate, long? FileSize)>();
        var hasAnyVideoFormat = false;
        var expectsAudio = root.TryGetProperty("formats", out var audioFormats) && audioFormats.ValueKind == JsonValueKind.Array
            ? audioFormats.EnumerateArray().Any(item => !ReadBoolean(item, "has_drm") &&
                ReadString(item, "acodec") is { Length: > 0 } codec && codec != "none")
            : ReadString(root, "acodec") is { Length: > 0 } audio && audio != "none";

        if (root.TryGetProperty("formats", out var formatArray) && formatArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in formatArray.EnumerateArray())
            {
                if (ReadBoolean(item, "has_drm") || !(HasVideo(item) || allowUnknownWebVideo && IsUnknownWebVideo(item)))
                    continue;
                hasAnyVideoFormat = true;
                var height = (int)(ReadDouble(item, "height") ?? 0);
                if (height <= 0)
                    continue;
                var fps = ReadDouble(item, "fps") ?? 0;
                var bitrate = ReadDouble(item, "tbr") ?? ReadDouble(item, "vbr") ?? 0;
                var fileSize = ReadLong(item, "filesize") ?? ReadLong(item, "filesize_approx");
                formats.Add((height, fps, bitrate, fileSize));
            }
        }

        if (allowUnknownWebVideo && !hasAnyVideoFormat && !root.TryGetProperty("formats", out _) &&
            !ReadBoolean(root, "has_drm") && IsUnknownWebVideo(root))
            hasAnyVideoFormat = true;

        var options = formats
            .GroupBy(format => format.Height)
            .OrderByDescending(group => group.Key)
            .Select(group =>
            {
                var fps = group.Max(item => item.Fps);
                var fileSize = group.Where(item => item.FileSize.HasValue).Select(item => item.FileSize!.Value).DefaultIfEmpty().Max();
                long? estimated = fileSize > 0
                    ? fileSize
                    : duration is > 0
                        ? (long?)(group.Max(item => item.Bitrate) * 1000 / 8 * duration.Value)
                        : null;
                var fpsLabel = fps >= 50 ? $"{Math.Round(fps):0}fps" : fps > 0 ? $"{Math.Round(fps):0}fps" : string.Empty;
                var sizeLabel = estimated is > 0 ? $" · 약 {FormatBytes(estimated.Value)}" : string.Empty;
                return new VideoFormatOption
                {
                    Height = group.Key,
                    ExpectedDurationSeconds = duration,
                    ExpectsAudio = expectsAudio,
                    Fps = fps,
                    EstimatedBytes = estimated,
                    Label = $"{group.Key}p{fpsLabel}{sizeLabel}",
                    Selector = allowUnknownWebVideo
                        ? $"bestvideo[height={group.Key}]+bestaudio/best[height={group.Key}]"
                        : $"bestvideo[height<={group.Key}]+bestaudio/best[height<={group.Key}]/best"
                };
            })
            .ToList();

        if (options.Count == 0 && hasAnyVideoFormat)
        {
            options.Add(new VideoFormatOption
            {
                Label = "최고 화질 (자동)",
                ExpectedDurationSeconds = duration,
                ExpectsAudio = expectsAudio,
                Selector = "bestvideo+bestaudio/best"
            });
        }

        return new VideoInfo
        {
            Id = ReadString(root, "id") ?? string.Empty,
            Title = ReadString(root, "title") ?? "제목 없음",
            ChannelName = ReadString(root, "channel") ?? ReadString(root, "uploader") ?? string.Empty,
            DurationSeconds = duration is > 0 ? (long)duration.Value : null,
            ThumbnailUrl = ReadString(root, "thumbnail"),
            Formats = options
        };
    }

    private static bool TryReportProgress(
        string line,
        DownloadProgressAggregator aggregator,
        IProgress<DownloadProgressInfo>? progress)
    {
        if (!YtDlpOutputParser.TryParseProgress(line, out var parsed))
            return false;
        progress?.Report(aggregator.Aggregate(parsed));
        return true;
    }

    private static void AddCookies(List<string> arguments, string? cookiePath)
    {
        if (!string.IsNullOrWhiteSpace(cookiePath))
        {
            arguments.Add("--cookies");
            arguments.Add(cookiePath);
        }
    }

    private static async Task<ProcessResult> RunCaptureAsync(List<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(arguments);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("yt-dlp를 시작하지 못했습니다.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await WaitForExitAfterKillAsync(process);
            try
            {
                await Task.WhenAll(standardOutputTask, standardErrorTask);
            }
            catch
            {
                // 취소 원인을 유지한다.
            }
            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
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
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        return startInfo;
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
            // 이미 종료된 경우 무시한다.
        }
    }

    private static bool HasVideo(JsonElement format)
    {
        var codec = ReadString(format, "vcodec");
        return !string.IsNullOrWhiteSpace(codec) && !string.Equals(codec, "none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnknownWebVideo(JsonElement format) =>
        string.IsNullOrWhiteSpace(ReadString(format, "vcodec")) &&
        ReadString(format, "ext") is "mp4" or "webm" or "mkv" or "mov" or "ts" &&
        Uri.TryCreate(ReadString(format, "url"), UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http";

    internal static void AddWebSelectionArguments(List<string> arguments, VideoFormatOption format, bool cached = false)
    {
        if (!format.IsGeneralWeb) return;
        if (format.WebPlaylistIndex is <= 0) throw new ArgumentOutOfRangeException(nameof(format));
        // Re-extract from the original page so extractor-provided headers remain intact.
        if (!cached)
            arguments.AddRange(["--playlist-items", (format.WebPlaylistIndex ?? 1).ToString(CultureInfo.InvariantCulture)]);
        if (string.IsNullOrEmpty(format.ExpectedVideoId) || format.ExpectedVideoId.Any(c => c is '\'' or '"' or '\r' or '\n'))
            throw new ArgumentException("영상 식별자를 안전하게 확인할 수 없습니다. 다시 분석해주세요.");
        arguments.AddRange(["--match-filter", $"id = '{format.ExpectedVideoId}' & !is_live & !has_drm"]);
    }

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static double? ReadDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
            return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value))
            return value;
        return property.ValueKind == JsonValueKind.String &&
               double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            ? value
            : null;
    }

    private static long? ReadLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
            return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
            return value;
        return property.ValueKind == JsonValueKind.String &&
               long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            ? value
            : null;
    }

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

    private static string CleanError(string error, string? cookiePath)
    {
        var cleaned = error;
        if (!string.IsNullOrWhiteSpace(cookiePath))
            cleaned = cleaned.Replace(cookiePath, "[임시 쿠키 파일]", StringComparison.OrdinalIgnoreCase);
        cleaned = AnsiRegex().Replace(cleaned, string.Empty).Trim();
        if (cleaned.Length > 2500)
            cleaned = cleaned[^2500..];
        return string.IsNullOrWhiteSpace(cleaned) ? "영상 처리 중 알 수 없는 오류가 발생했습니다." : cleaned;
    }

    [GeneratedRegex("\\x1B\\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex AnsiRegex();

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

public sealed class YtDlpException : Exception
{
    public YtDlpException(string message) : base(message)
    {
    }
}
