using ChzzkDownloader.Models;

namespace ChzzkDownloader.Services;

public sealed class SessionCookieStore
{
    private readonly Dictionary<VideoSource, IReadOnlyList<BrowserCookie>> _cookies = [];

    public IReadOnlyList<BrowserCookie> Get(VideoSource? source) =>
        source is { } value && _cookies.TryGetValue(value, out var cookies)
            ? cookies
            : [];

    public void Set(VideoSource source, IReadOnlyList<BrowserCookie> cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);
        _cookies[source] = cookies;
    }

    public void Clear() => _cookies.Clear();

    public bool HasAny => _cookies.Values.Any(cookies => cookies.Count > 0);
}
