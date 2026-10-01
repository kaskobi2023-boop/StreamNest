using System.Text.Json;
using System.Text.RegularExpressions;
using ChzzkDownloader.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;

namespace ChzzkDownloader.Services;

/// <summary>Reads one coherent browser account; never writes tokens back into the website.</summary>
public static class RPlaySessionService
{
    public static async Task<IReadOnlyList<BrowserCookie>> RestoreAsync(Window? owner, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(WebViewSessionService.GetProfileFolder(VideoSource.RPlay))) return [];
        using var webView = new WebView2();
        var host = new Window
        {
            Content = webView, Width = 2, Height = 2, Left = -10000, Top = -10000,
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0,
            WindowStyle = WindowStyle.ToolWindow
        };
        if (owner is not null) host.Owner = owner;
        try
        {
            host.Show();
            var env = await WebViewSessionService.CreateEnvironmentAsync(VideoSource.RPlay);
            await webView.EnsureCoreWebView2Async(env);
            var core = webView.CoreWebView2;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NavigationStarting += (_, e) => e.Cancel = !IsSessionPage(e.Uri);
            core.Navigate("https://rplay.live/");
            for (var attempt = 0; attempt < 24; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cookies = await ReadAsync(core);
                if (cookies.Count > 0) return cookies;
                await Task.Delay(500, cancellationToken);
            }
            return [];
        }
        catch (OperationCanceledException) { throw; }
        catch { return []; } // Interactive login remains available; never expose tokens through exception text.
        finally { host.Close(); }
    }

    public const string RequestorCookie = "streamnest_rplay_requestor";
    public const string LoginTypeCookie = "streamnest_rplay_login_type";
    public const string UserAgentCookie = "streamnest_rplay_user_agent";

    // The active AccountModule is authoritative, including an explicit logged-out state.
    // Do not scan arbitrary JWTs, rank by expiry, or resurrect synthetic browser cookies.
    public const string ReadScript = """
        (() => {
            const app = document.querySelector('#app');
            let store = app?.__vue__?.$store || app?.__vue_app__?.config?.globalProperties?.$store;
            if (!store) {
                for (const node of Array.from(document.querySelectorAll('*')).slice(0, 100)) {
                    if (node.__vue__?.$store) { store = node.__vue__.$store; break; }
                }
            }
            if (!store?.state?.AccountModule) return null;
            const a = store.state.AccountModule;
            const g = store.getters || {};
            const loginType = g['AccountModule/currentUserLoginType'] ?? a.loginType;
            if (!loginType) return { token: '', userOid: '', loginType: '' };
            return {
                token: g['AccountModule/currentUserToken'] ?? a.token ?? '',
                userOid: g['AccountModule/currentUserOid'] ?? a.userInfo?.oid ?? a.userInfo?._id ?? '',
                loginType: loginType,
                userAgent: navigator.userAgent
            };
        })()
        """;

    public static bool IsSessionPage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.IdnHost.Equals("rplay.live", StringComparison.OrdinalIgnoreCase) ||
         uri.IdnHost.Equals("www.rplay.live", StringComparison.OrdinalIgnoreCase));

    public static async Task<IReadOnlyList<BrowserCookie>> ReadAsync(CoreWebView2 core)
    {
        if (!IsSessionPage(core.Source)) return [];
        var json = await core.ExecuteScriptAsync(ReadScript);
        // Navigation can complete while script execution is pending.
        if (!IsSessionPage(core.Source)) return [];
        return ParseSnapshot(json);
    }

    public static IReadOnlyList<BrowserCookie> ParseSnapshot(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return [];
            var token = root.GetProperty("token").GetString()?.Trim();
            var oid = root.GetProperty("userOid").GetString();
            var loginType = root.GetProperty("loginType").GetString();
            if (!TryGetTokenExpiry(token, out var expires) || !IsRequestor(oid) || !IsLoginType(loginType))
                return [];
            var cookies = new List<BrowserCookie>
            {
                new("_AUTHORIZATION_", token!, "api.rplay.live", "/", true, true, expires),
                new(RequestorCookie, oid!, "api.rplay.live", "/", true, true, expires),
                new(LoginTypeCookie, loginType!, "api.rplay.live", "/", true, true, expires)
            };
            if (root.TryGetProperty("userAgent", out var ua) && ua.ValueKind == JsonValueKind.String && IsUserAgent(ua.GetString()))
                cookies.Add(new(UserAgentCookie, ua.GetString()!, "api.rplay.live", "/", true, true, expires));
            return cookies;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { return []; }
    }

    // This validates syntax/lifetime, not the signature or server-side session validity.
    // RPlay legacy tokens legitimately have no exp claim; they are never ranked above others.
    public static bool TryGetTokenExpiry(string? token, out long expires)
    {
        expires = 0;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16384) return false;
        var parts = token.Split('.');
        if (parts.Length != 3 || parts.Any(p => p.Length == 0 || !Regex.IsMatch(p, "^[A-Za-z0-9_-]+$"))) return false;
        try
        {
            static byte[] Decode(string value)
            {
                value = value.Replace('-', '+').Replace('_', '/');
                return Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '='));
            }
            using var header = JsonDocument.Parse(Decode(parts[0]));
            if (!header.RootElement.TryGetProperty("alg", out var alg) ||
                alg.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(alg.GetString()) ||
                string.Equals(alg.GetString(), "none", StringComparison.OrdinalIgnoreCase)) return false;
            using var payload = JsonDocument.Parse(Decode(parts[1]));
            if (payload.RootElement.ValueKind != JsonValueKind.Object) return false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (payload.RootElement.TryGetProperty("exp", out var exp) &&
                (!exp.TryGetInt64(out expires) || expires <= now)) return false;
            if (payload.RootElement.TryGetProperty("nbf", out var nbf) &&
                (!nbf.TryGetInt64(out var validFrom) || validFrom > now + 30)) return false;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException) { return false; }
    }

    public static bool IsRequestor(string? value) => value is not null && Regex.IsMatch(value, "^[a-fA-F0-9]{24}$");
    public static bool IsLoginType(string? value) => value is not null && Regex.IsMatch(value, "^[a-zA-Z0-9_-]{1,32}$");
    public static bool IsUserAgent(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 512 && !value.Any(char.IsControl);
    public static bool IsContextCookie(BrowserCookie cookie) =>
        cookie.Domain.Equals("api.rplay.live", StringComparison.OrdinalIgnoreCase) &&
        (cookie.Name == RequestorCookie && IsRequestor(cookie.Value) ||
         cookie.Name == LoginTypeCookie && IsLoginType(cookie.Value) ||
         cookie.Name == UserAgentCookie && IsUserAgent(cookie.Value));
}
