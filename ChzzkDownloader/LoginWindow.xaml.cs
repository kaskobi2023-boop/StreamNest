using Microsoft.Web.WebView2.Core;
using ChzzkDownloader.Models;
using ChzzkDownloader.Services;
using System.Diagnostics;
using System.Windows;

namespace ChzzkDownloader;

public partial class LoginWindow : Window
{
    private readonly string _targetUrl;
    public VideoSource Source { get; }
    private bool _initialized;
    private bool _youtubeLoginRecoveryInProgress;
    private bool _youtubeLoginRedirected;

    public LoginWindow(string targetUrl)
    {
        InitializeComponent();
        if (!VideoUrlService.TryParse(targetUrl, out var videoUrl))
            throw new ArgumentException("지원하는 치지직 또는 YouTube 영상 주소가 아닙니다.", nameof(targetUrl));

        Source = videoUrl.Source;
        _targetUrl = LoginSecurityPolicy.CreateCanonicalVideoUrl(targetUrl);
        Title = Source == VideoSource.YouTube ? "YouTube 로그인" : "치지직 로그인";
        Loaded += LoginWindow_Loaded;
    }

    public IReadOnlyList<BrowserCookie> Cookies { get; private set; } = [];
    public bool SessionCleared { get; private set; }

    private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var environment = await WebViewSessionService.CreateEnvironmentAsync();
            await LoginWebView.EnsureCoreWebView2Async(environment);

            LoginWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            LoginWebView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            LoginWebView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            LoginWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            LoginWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            LoginWebView.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;
            LoginWebView.CoreWebView2.PermissionRequested += CoreWebView2_PermissionRequested;
            _initialized = true;
            RefreshButton.IsEnabled = true;
            HomeButton.IsEnabled = true;
            LoginWebView.CoreWebView2.Navigate(_targetUrl);
        }
        catch (Exception exception)
        {
            LoginStatusText.Text = $"내장 브라우저를 열지 못했습니다: {exception.Message}";
            BrowserLoadingText.Text = "Microsoft Edge WebView2 Runtime을 확인해주세요.";
        }
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (LoginSecurityPolicy.IsAllowedTopLevelUri(e.Uri))
        {
            LoginWebView.CoreWebView2.Navigate(e.Uri);
            return;
        }

        HandleBlockedNavigation(e.Uri, e.IsUserInitiated);
    }

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (LoginSecurityPolicy.IsAllowedTopLevelUri(e.Uri))
        {
            CurrentOriginText.Text = LoginSecurityPolicy.GetDisplayOrigin(e.Uri);
            return;
        }

        e.Cancel = true;
        HandleBlockedNavigation(e.Uri, e.IsUserInitiated);
    }

    private void CoreWebView2_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
        LoginStatusText.Text = "로그인 전용 창에서는 파일 다운로드를 허용하지 않습니다.";
    }

    private void CoreWebView2_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = CoreWebView2PermissionState.Deny;
        e.Handled = true;
        LoginStatusText.Text = "로그인에 필요하지 않은 브라우저 권한 요청을 차단했습니다.";
    }

    private void HandleBlockedNavigation(string? value, bool userInitiated)
    {
        var displayOrigin = LoginSecurityPolicy.GetDisplayOrigin(value);
        LoginStatusText.Text = $"보안을 위해 내장 로그인 창에서 {displayOrigin} 이동을 차단했습니다.";

        if (!userInitiated || !LoginSecurityPolicy.TryGetExternalHttpsUri(value, out var externalUri))
            return;

        var answer = MessageBox.Show(
            this,
            LoginDisplayText.GetExternalLinkPrompt(Source, externalUri.GetLeftPart(UriPartial.Authority)),
            "외부 링크",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = externalUri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            LoginStatusText.Text = $"기본 브라우저에서 링크를 열지 못했습니다: {exception.Message}";
        }
    }

    private async void LoginWebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        BrowserLoadingText.Visibility = Visibility.Collapsed;
        BackButton.IsEnabled = e.IsSuccess && LoginWebView.CoreWebView2.CanGoBack;
        var allowedPage = LoginSecurityPolicy.IsAllowedTopLevelUri(LoginWebView.Source?.AbsoluteUri);
        // Google can finish 2-step verification and persist the authenticated
        // cookies even when its final transition reports a navigation failure in
        // WebView2. Let the user confirm that valid session instead of trapping the
        // login dialog on an error page.
        LoginCompleteButton.IsEnabled = allowedPage;
        CurrentOriginText.Text = LoginSecurityPolicy.GetDisplayOrigin(LoginWebView.Source?.AbsoluteUri);
        if (!e.IsSuccess && await TryRecoverCompletedYouTubeLoginAsync())
            return;
        LoginStatusText.Text = e.IsSuccess
            ? "로그인을 마쳤다면 ‘세션 확인’을 누르세요."
            : "페이지를 불러오지 못했습니다. 네트워크 연결을 확인해주세요.";
    }

    private async Task<bool> TryRecoverCompletedYouTubeLoginAsync()
    {
        if (Source != VideoSource.YouTube ||
            _youtubeLoginRecoveryInProgress ||
            _youtubeLoginRedirected ||
            !LoginSecurityPolicy.IsGoogleAuthenticationUri(LoginWebView.Source?.AbsoluteUri))
        {
            return false;
        }

        _youtubeLoginRecoveryInProgress = true;
        try
        {
            int[] retryDelaysMilliseconds = [0, 250, 750];
            foreach (var delay in retryDelaysMilliseconds)
            {
                if (delay > 0)
                    await Task.Delay(delay);

                var cookies = await WebViewCookieCollector.CollectAsync(
                    LoginWebView.CoreWebView2,
                    VideoSource.YouTube);
                if (!WebViewCookieCollector.HasAuthenticatedYouTubeSession(cookies))
                    continue;

                Cookies = cookies;
                _youtubeLoginRedirected = true;
                LoginStatusText.Text = "2단계 인증 완료를 확인했습니다. 원래 영상 페이지로 이동합니다.";
                BrowserLoadingText.Text = "YouTube 영상 페이지로 이동하는 중...";
                BrowserLoadingText.Visibility = Visibility.Visible;
                LoginWebView.CoreWebView2.Navigate(_targetUrl);
                return true;
            }

            return false;
        }
        catch (Exception exception)
        {
            LoginStatusText.Text = $"2단계 인증 완료 후 영상 페이지 이동 실패: {exception.Message}";
            return false;
        }
        finally
        {
            _youtubeLoginRecoveryInProgress = false;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_initialized && LoginWebView.CoreWebView2.CanGoBack)
            LoginWebView.CoreWebView2.GoBack();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            LoginWebView.CoreWebView2.Reload();
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            LoginWebView.CoreWebView2.Navigate(_targetUrl);
    }

    private async void ClearSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
            return;

        var confirmation = MessageBox.Show(
            this,
            LoginDisplayText.ClearAllSessionsPrompt,
            "로그인 세션 삭제",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        try
        {
            LoginCompleteButton.IsEnabled = false;
            LoginStatusText.Text = "저장된 로그인 세션을 삭제하는 중…";
            await WebViewSessionService.ClearAsync(LoginWebView.CoreWebView2);
            Cookies = [];
            SessionCleared = true;
            _youtubeLoginRedirected = false;
            LoginStatusText.Text = "세션을 삭제했습니다. 다시 로그인해주세요.";
            LoginWebView.CoreWebView2.Navigate(_targetUrl);
        }
        catch (Exception exception)
        {
            LoginStatusText.Text = $"세션 삭제 실패: {exception.Message}";
        }
        finally
        {
            LoginCompleteButton.IsEnabled = true;
        }
    }

    private async void LoginCompleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
            return;

        try
        {
            LoginCompleteButton.IsEnabled = false;
            LoginStatusText.Text = "로그인 세션을 확인하는 중…";
            Cookies = await WebViewCookieCollector.CollectAsync(LoginWebView.CoreWebView2, Source);
            if (Cookies.Count == 0)
            {
                LoginStatusText.Text = LoginDisplayText.GetMissingCookieMessage(Source);
                return;
            }
            if (Source == VideoSource.YouTube &&
                !WebViewCookieCollector.HasAuthenticatedYouTubeSession(Cookies))
            {
                LoginStatusText.Text = LoginDisplayText.GetMissingCookieMessage(Source);
                Cookies = [];
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception exception)
        {
            LoginStatusText.Text = $"세션 확인 실패: {exception.Message}";
        }
        finally
        {
            LoginCompleteButton.IsEnabled = true;
        }
    }

}
