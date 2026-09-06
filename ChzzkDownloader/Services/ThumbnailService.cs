using System.Net;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace ChzzkDownloader.Services;

public sealed class ThumbnailService
{
    private const int MaximumThumbnailBytes = 8 * 1024 * 1024;
    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();
    private readonly HttpClient _httpClient;

    public ThumbnailService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<BitmapImage?> LoadAsync(string? value, CancellationToken cancellationToken = default)
    {
        if (!TryCreateAllowedUri(value, out var uri))
            return null;

        try
        {
            using var response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumThumbnailBytes)
                return null;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                var count = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                    break;
                if (buffer.Length + count > MaximumThumbnailBytes)
                    return null;
                await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }

            buffer.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public static bool TryCreateAllowedUri(string? value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            uri = null!;
            return false;
        }

        var isHttps = string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                      parsed.IsDefaultPort;
        var isLoopbackHttp = string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                             IPAddress.TryParse(parsed.IdnHost, out var address) &&
                             IPAddress.IsLoopback(address);
        if (!isHttps && !isLoopbackHttp)
        {
            uri = null!;
            return false;
        }

        uri = parsed;
        return true;
    }

    private static HttpClient CreateSharedHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StreamNest/0.4 thumbnail-loader");
        return client;
    }
}
