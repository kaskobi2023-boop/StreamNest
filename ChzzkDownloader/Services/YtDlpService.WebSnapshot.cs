using System.Text.Json;
using System.Text.Json.Nodes;
using ChzzkDownloader.Models;

namespace ChzzkDownloader.Services;

public sealed partial class YtDlpService
{
    private static void QuarantineAttempt(string jobDirectory, string attemptDirectory)
    {
        var root = Path.GetFullPath(jobDirectory);
        var source = Path.GetFullPath(attemptDirectory);
        if (!source.Equals(Path.Combine(root, "active"), StringComparison.OrdinalIgnoreCase))
            throw new IOException("현재 작업의 임시 파일 경로를 확인하지 못했습니다.");
        var destination = Path.Combine(root, "failed-attempt-" + Guid.NewGuid().ToString("N"));
        // Both paths are immediate children of this job. Cleanup retains scope
        // even after a retry; nothing is moved into another user's job.
        if (Directory.Exists(source)) Directory.Move(source, destination);
    }

    internal static string PublishVerifiedWebFile(string source, string outputFolder, int? height = null)
    {
        var name = Path.GetFileNameWithoutExtension(source);
        if (height is > 0) name += $" [{height}p]";
        var extension = Path.GetExtension(source);
        for (var suffix = 0; suffix < 10000; suffix++)
        {
            var destination = Path.Combine(outputFolder, name + (suffix == 0 ? "" : $" ({suffix + 1})") + extension);
            try { File.Move(source, destination, overwrite: false); return destination; }
            catch (IOException) when (File.Exists(destination)) { }
        }
        throw new IOException("동일한 이름의 파일이 너무 많아 저장하지 못했습니다.");
    }

    internal static WebDownloadSnapshot CreateWebSnapshot(JsonElement entry, string pageUrl, bool includePagePathInReferer)
    {
        // An explicit allowlist excludes filenames, postprocessors, playlist
        // delegation and webpage_url (yt-dlp otherwise retries that URL itself,
        // outside our selected-video identity check).
        var snapshot = new JsonObject { ["_type"] = "video" };
        foreach (var key in new[] { "id", "title", "duration", "formats", "url", "ext", "protocol",
                     "http_headers", "vcodec", "acodec", "width", "height", "fps", "tbr", "filesize",
                     "has_drm", "is_live", "live_status", "extractor", "extractor_key", "fragment_base_url", "fragments" })
            if (entry.TryGetProperty(key, out var value)) snapshot[key] = JsonNode.Parse(value.GetRawText());
        RemoveCredentials(snapshot);
        return new WebDownloadSnapshot
        {
            PageUrl = pageUrl, VideoId = ReadString(entry, "id") ?? string.Empty,
            InfoJson = snapshot.ToJsonString(), DurationSeconds = ReadDouble(entry, "duration"),
            ExpectsAudio = entry.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array
                ? formats.EnumerateArray().Any(f => ReadString(f, "acodec") is { Length: > 0 } codec && codec != "none")
                : ReadString(entry, "acodec") is { Length: > 0 } audio && audio != "none",
            IncludePagePathInReferer = includePagePathInReferer
        };

        static void RemoveCredentials(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                foreach (var key in obj.Select(pair => pair.Key).ToArray())
                    if (key.Equals("cookies", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("cookie", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("authorization", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("proxy-authorization", StringComparison.OrdinalIgnoreCase)) obj.Remove(key);
                    else RemoveCredentials(obj[key]);
            }
            else if (node is JsonArray array)
                foreach (var child in array) RemoveCredentials(child);
        }
    }

    public async Task<string?> DownloadAsync(string url, string outputFolder, VideoFormatOption format,
        IReadOnlyCollection<BrowserCookie>? cookies, IProgress<DownloadProgressInfo>? progress,
        Action<string>? log, CancellationToken cancellationToken = default)
    {
        if (format.WebSnapshot is { } snapshot && (!format.IsGeneralWeb ||
            snapshot.PageUrl != url || snapshot.VideoId != format.ExpectedVideoId))
            throw new ArgumentException("분석한 페이지와 선택한 영상이 일치하지 않습니다. 다시 분석해주세요.");
        // A retry may reorder playlist entries. Pin the original job directory
        // so resume and the caller's cancellation cleanup target the same files.
        var partialDirectory = GetPartialDirectory(url, outputFolder, format);
        try
        {
            if (format.WebSnapshot is not null) log?.Invoke("분석한 스트림과 요청 정보를 재사용합니다.");
            return await DownloadAttemptAsync(url, outputFolder, format, partialDirectory, cookies, progress, log, cancellationToken);
        }
        catch (YtDlpException exception) when (format.IsGeneralWeb && format.WebSnapshot is not null &&
                                              IsRefreshableStreamError(exception.Message))
        {
            cancellationToken.ThrowIfCancellationRequested();
            log?.Invoke("스트림 주소가 만료되었거나 서버에서 거부했습니다. 같은 영상인지 확인하고 한 번 갱신합니다.");
            var refreshed = await AnalyzeWebAsync(url, cancellationToken, format.WebSnapshot.IncludePagePathInReferer);
            var selected = refreshed.SingleOrDefault(item => item.Video.Id == format.ExpectedVideoId);
            if (selected is null)
                throw new YtDlpException("갱신된 페이지에서 같은 영상을 확인하지 못했습니다. 다른 영상으로 전환하지 않았습니다. 다시 분석해주세요.");
            var quality = selected.Video.Formats.FirstOrDefault(candidate => candidate.Height == format.Height);
            if (quality is null)
                throw new YtDlpException("선택한 화질이 더 이상 제공되지 않습니다. 다시 분석하고 화질을 선택해주세요.");
            // Deliberately no recursive retry. Genuine permission failures and
            // missing fragments stop after this bounded refresh.
            return await DownloadAttemptAsync(url, outputFolder, selected.SelectFormat(quality), partialDirectory, cookies,
                progress, log, cancellationToken);
        }
    }

    internal static bool IsRefreshableStreamError(string message) =>
        new[] { "HTTP Error 401", "HTTP Error 403", "HTTP Error 410", "HTTP Error 404", "URL expired", "token expired" }
            .Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
