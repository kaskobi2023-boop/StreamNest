using System.Windows;
using ChzzkDownloader.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChzzkDownloader.IntegrationTests;

internal static class WebView2ProfileHarness
{
    public static Task<T> UseAsync<T>(Func<CoreWebView2, Task<T>> action) => StaThreadRunner.RunAsync(async () =>
    {
        using var webView = new WebView2();
        var host = new Window
        {
            Content = webView,
            Width = 2,
            Height = 2,
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
            var environment = await WebViewSessionService.CreateEnvironmentAsync();
            await webView.EnsureCoreWebView2Async(environment);
            return await action(webView.CoreWebView2);
        }
        finally
        {
            host.Close();
        }
    });

    public static async Task NavigateAsync(
        CoreWebView2 coreWebView,
        string url,
        TimeSpan timeout)
    {
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, eventArgs) => completion.TrySetResult(eventArgs);
        coreWebView.NavigationCompleted += handler;
        try
        {
            coreWebView.Navigate(url);
            var result = await completion.Task.WaitAsync(timeout);
            if (!result.IsSuccess && result.WebErrorStatus == CoreWebView2WebErrorStatus.ConnectionAborted &&
                Uri.TryCreate(coreWebView.Source, UriKind.Absolute, out var currentUri) &&
                VideoUrlService.IsYouTubeHost(currentUri.IdnHost))
            {
                // YouTube가 초기화 중 window.stop()을 호출하면 사용 가능한 페이지도
                // ConnectionAborted로 보고될 수 있다. 쿠키 저장이 끝날 짧은 시간을 준다.
                await Task.Delay(750);
                return;
            }

            if (!result.IsSuccess)
                throw new InvalidOperationException($"WebView2 탐색 실패: {result.WebErrorStatus}");
        }
        finally
        {
            coreWebView.NavigationCompleted -= handler;
        }
    }
}
