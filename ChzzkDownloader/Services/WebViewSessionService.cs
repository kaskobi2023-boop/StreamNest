using System.Windows;
using ChzzkDownloader.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChzzkDownloader.Services;

public static class WebViewSessionService
{
    public static string ProfileFolder => GetProfileFolder();

    public static string GetProfileFolder(VideoSource? source = null) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChzzkLocalDownloader",
        source switch
        {
            VideoSource.Chzzk => "WebView2Profile_Chzzk",
            VideoSource.YouTube => "WebView2Profile_YouTube",
            VideoSource.Soop => "WebView2Profile_Soop",
            VideoSource.RPlay => "WebView2Profile_RPlay",
            _ => "WebView2Profile"
        });

    public static async Task<CoreWebView2Environment> CreateEnvironmentAsync(VideoSource? source = null)
    {
        var folder = GetProfileFolder(source);
        Directory.CreateDirectory(folder);
        return await CoreWebView2Environment.CreateAsync(userDataFolder: folder);
    }

    public static async Task ClearAsync(CoreWebView2 coreWebView)
    {
        coreWebView.CookieManager.DeleteAllCookies();
        await coreWebView.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
    }

    public static async Task ClearStoredSessionAsync(Window owner, VideoSource? source = null)
    {
        if (source is null)
        {
            var exceptions = new List<Exception>();

            // 1. Clear legacy profile folder (WebView2Profile)
            try
            {
                await ClearStoredSessionFolderAsync(owner, GetProfileFolder(null));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }

            // 2. Clear all platform-specific profile folders
            foreach (var s in Enum.GetValues<VideoSource>())
            {
                try
                {
                    await ClearStoredSessionFolderAsync(owner, GetProfileFolder(s));
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            if (exceptions.Count > 0)
                throw new AggregateException("하나 이상의 브라우저 프로필을 삭제하는 중 오류가 발생했습니다.", exceptions);
            return;
        }

        await ClearStoredSessionFolderAsync(owner, GetProfileFolder(source));
    }

    private static async Task ClearStoredSessionFolderAsync(Window owner, string folder)
    {
        if (!Directory.Exists(folder))
            return;

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
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: folder);
            await webView.EnsureCoreWebView2Async(environment);
            await ClearAsync(webView.CoreWebView2);
        }
        finally
        {
            host.Close();
        }
    }
}
