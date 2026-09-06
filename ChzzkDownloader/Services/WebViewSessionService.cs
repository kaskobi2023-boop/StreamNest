using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChzzkDownloader.Services;

public static class WebViewSessionService
{
    public static string ProfileFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChzzkLocalDownloader",
        "WebView2Profile");

    public static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        Directory.CreateDirectory(ProfileFolder);
        return await CoreWebView2Environment.CreateAsync(userDataFolder: ProfileFolder);
    }

    public static async Task ClearAsync(CoreWebView2 coreWebView)
    {
        coreWebView.CookieManager.DeleteAllCookies();
        await coreWebView.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
    }

    public static async Task ClearStoredSessionAsync(Window owner)
    {
        using var webView = new WebView2();
        var host = new Window
        {
            Owner = owner,
            Content = webView,
            Width = 1,
            Height = 1,
            Left = -10000,
            Top = -10000,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Opacity = 0
        };

        try
        {
            host.Show();
            var environment = await CreateEnvironmentAsync();
            await webView.EnsureCoreWebView2Async(environment);
            await ClearAsync(webView.CoreWebView2);
        }
        finally
        {
            host.Close();
        }
    }
}
