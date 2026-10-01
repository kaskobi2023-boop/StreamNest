using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChzzkDownloader.Services;

internal sealed record BrowserMedia(string Url, string? Manifest = null);
internal sealed record WebBrowserCapture(string PageUrl, string Title, string? Thumbnail,
    string Html, string UserAgent, IReadOnlyList<BrowserMedia> Media);

// A separate private browser executes player scripts; platform profiles are never opened.
internal sealed class WebBrowserCaptureService
{
    private readonly Func<string, CancellationToken, Task<Uri>> _validate;
    internal WebBrowserCaptureService(Func<string, CancellationToken, Task<Uri>>? validate = null) =>
        _validate = validate ?? WebVideoUrlService.ValidateAsync;

    internal async Task<WebBrowserCapture> CaptureAsync(string url, CancellationToken token)
    {
        var page = await _validate(url, token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var ct = timeout.Token;
        using var view = new WebView2();
        var host = new Window { Content = view, Width = 1024, Height = 768, Left = -12000, Top = -12000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow, Opacity = 0 };
        var media = new Dictionary<string, BrowserMedia>(StringComparer.Ordinal);
        var reads = new List<Task>();
        var network = new Dictionary<string, Task<bool>>(StringComparer.OrdinalIgnoreCase);
        var closed = false;
        var changed = Stopwatch.StartNew();
        try
        {
            host.Show();
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChzzkLocalDownloader", "WebView2Profile_GeneralCapture");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: folder).WaitAsync(ct);
            var options = env.CreateCoreWebView2ControllerOptions();
            options.ProfileName = "GeneralCapture";
            options.IsInPrivateModeEnabled = true;
            await view.EnsureCoreWebView2Async(env, options).WaitAsync(ct);
            var core = view.CoreWebView2;
            core.IsMuted = true;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += async (_, e) =>
            {
                using var deferral = e.GetDeferral();
                try
                {
                    if (e.Request.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                        e.Request.Uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return;
                    var allowed = await AllowedAsync(e.Request.Uri);
                    if (!closed && !allowed)
                        e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
                }
                catch (Exception) when (closed || ct.IsCancellationRequested) { }
            };
            core.WebResourceResponseReceived += (_, e) =>
            {
                reads.RemoveAll(task => task.IsCompleted);
                if (!closed && reads.Count < 60) reads.Add(ReadResponseAsync(e));
            };
            core.Navigate(page.AbsoluteUri);
            var elapsed = Stopwatch.StartNew();
            string html = "", title = page.Host;
            string? thumbnail = null;
            while (elapsed.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(750, ct);
                // Only actual player controls are activated. No links, forms or challenge controls.
                var raw = await core.ExecuteScriptAsync("""
                    (() => {
                      document.querySelectorAll('video').forEach(v => {v.muted=true; const p=v.play(); if(p)p.catch(()=>{});});
                      const b=document.querySelector('.vjs-big-play-button,button[aria-label="Play"],button[aria-label="재생"],.jw-icon-display');
                      if(b && !b.dataset.streamnestClicked){b.dataset.streamnestClicked='1'; b.click();}
                      return {title:document.querySelector('meta[property="og:title"]')?.content || document.title,
                        thumbnail:document.querySelector('meta[property="og:image"]')?.content,
                        html:document.documentElement.outerHTML.slice(0,2097152),
                        sources:[...document.querySelectorAll('video,video source')].map(v=>v.currentSrc||v.src).filter(Boolean).slice(0,20)};
                    })()
                    """).WaitAsync(ct);
                if (raw != "null")
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    title = root.GetProperty("title").GetString() ?? page.Host;
                    html = root.GetProperty("html").GetString() ?? "";
                    if (root.TryGetProperty("thumbnail", out var thumb)) thumbnail = thumb.GetString();
                    foreach (var source in root.GetProperty("sources").EnumerateArray())
                    {
                        var value = source.GetString();
                        if (value is not null && Uri.TryCreate(value, UriKind.Absolute, out var src) &&
                            src.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) && await AllowedAsync(value))
                            Add(new BrowserMedia(value));
                    }
                }
                if (media.Count > 0 && elapsed.Elapsed.TotalSeconds >= 6 && changed.Elapsed.TotalSeconds >= 3) break;
            }
            try { await Task.WhenAll(reads.ToArray()).WaitAsync(TimeSpan.FromSeconds(3), ct); }
            catch (TimeoutException) { /* Long-running media responses must not hide completed manifests. */ }
            // Use the final page for correct relative URLs and request context after redirects.
            var finalPage = await _validate(core.Source, ct);
            return new WebBrowserCapture(finalPage.AbsoluteUri, title, thumbnail, html, core.Settings.UserAgent,
                media.Values.Take(20).ToArray());
        }
        finally { closed = true; host.Close(); }

        void Add(BrowserMedia item)
        {
            if (closed || media.Count >= 20 || media.ContainsKey(item.Url)) return;
            media.Add(item.Url, item);
            changed.Restart();
        }

        async Task<bool> AllowedAsync(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return false;
            var key = uri.GetLeftPart(UriPartial.Authority);
            if (!network.TryGetValue(key, out var check)) network[key] = check = CheckAsync(value);
            return await check;
        }
        async Task<bool> CheckAsync(string value)
        {
            try { await _validate(value, ct); return true; }
            catch (Exception) { return false; }
        }
        async Task ReadResponseAsync(CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            try
            {
                var responseUrl = e.Request.Uri;
                if (e.Response.StatusCode != 200 || !await AllowedAsync(responseUrl) || closed) return;
                var type = e.Response.Headers.Contains("Content-Type") ? e.Response.Headers.GetHeader("Content-Type") : "";
                if (!(type.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ||
                      type.Contains("text/plain", StringComparison.OrdinalIgnoreCase) ||
                      type.Contains("octet-stream", StringComparison.OrdinalIgnoreCase) ||
                      new Uri(responseUrl).AbsolutePath.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                      new Uri(responseUrl).AbsolutePath.Contains("/hls/", StringComparison.OrdinalIgnoreCase))) return;
                if (e.Response.Headers.Contains("Content-Length") &&
                    long.TryParse(e.Response.Headers.GetHeader("Content-Length"), out var size) && size > 1048576) return;
                using var body = await e.Response.GetContentAsync().WaitAsync(ct);
                if (body is null || closed) return;
                using var reader = new StreamReader(body, Encoding.UTF8);
                var buffer = new char[1048577];
                var length = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
                if (length > 1048576) return;
                var manifest = new string(buffer, 0, length).TrimStart('\uFEFF');
                if (manifest.StartsWith("#EXTM3U", StringComparison.Ordinal)) Add(new BrowserMedia(responseUrl, manifest));
            }
            catch (Exception) { /* A failed subresource must not discard other readable players. */ }
        }
    }
}
