using System.ComponentModel;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
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
    private bool _googleLoginRecoveryInProgress;
    private bool _googleLoginRedirected;
    private bool _isCleaningUp;
    private readonly LoginNavigationCoordinator _navigationCoordinator = new();

    public LoginWindow(string targetUrl)
    {
        InitializeComponent();
        if (!VideoUrlService.TryParse(targetUrl, out var videoUrl))
            throw new ArgumentException("지원하는 치지직·YouTube·SOOP 영상 주소가 아닙니다.", nameof(targetUrl));

        Source = videoUrl.Source;
        _targetUrl = LoginSecurityPolicy.CreateCanonicalVideoUrl(targetUrl);
        Title = $"{VideoUrlService.GetDisplayName(Source)} 로그인";
        Loaded += LoginWindow_Loaded;
    }

    public IReadOnlyList<BrowserCookie> Cookies { get; private set; } = [];
    public bool SessionCleared { get; private set; }
    public bool SessionCleanupHadErrors { get; private set; }
    public string? SessionCleanupErrorMessage { get; private set; }
    private string? _stickyStatusMessage;
    private readonly List<Window> _childWindows = [];

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isCleaningUp)
        {
            e.Cancel = true;
            LoginStatusText.Text = "세션 정리 작업이 진행 중입니다. 완료될 때까지 창을 닫을 수 없습니다.";
            return;
        }

        foreach (var child in _childWindows.ToArray())
        {
            try { child.Close(); } catch { }
        }

        base.OnClosing(e);
    }

    private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var environment = await WebViewSessionService.CreateEnvironmentAsync(Source);
            await LoginWebView.EnsureCoreWebView2Async(environment);

            LoginWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            LoginWebView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            LoginWebView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            LoginWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            LoginWebView.CoreWebView2.FrameNavigationStarting += CoreWebView2_FrameNavigationStarting;
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

    private async void CoreWebView2_NewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        var opener = LoginWebView?.CoreWebView2;
        var environment = opener?.Environment;

        if (_isCleaningUp || !_initialized || !IsVisible ||
            opener == null || environment == null)
        {
            e.Handled = true;
            return;
        }

        bool initialAllowed =
            string.IsNullOrEmpty(e.Uri) ||
            string.Equals(e.Uri, "about:blank",
                StringComparison.OrdinalIgnoreCase) ||
            LoginSecurityPolicy.IsAllowedTopLevelUri(e.Uri, Source);

        TraceNavigation(
            "NewWindowRequested",
            e.Uri, initialAllowed, e.IsUserInitiated);

        if (!initialAllowed)
        {
            e.Handled = true;
            HandleBlockedNavigation(e.Uri, e.IsUserInitiated);
            return;
        }

        // 초기화 실패 시 즉시 해제할 수 있도록 true로 둔다.
        e.Handled = true;

        CoreWebView2Deferral? deferral = null;
        Window? childWindow = null;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        string stage = "pre-show";
        bool attached = false;

        try
        {
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = opener.Profile.ProfileName;
            options.IsInPrivateModeEnabled =
                opener.Profile.IsInPrivateModeEnabled;

            deferral = e.GetDeferral();

            // WebView2 콜백 스택을 빠져나온 후 같은 UI 스레드에서 진행.
            await System.Windows.Threading.Dispatcher.Yield(
                System.Windows.Threading.DispatcherPriority.Normal);

            if (_isCleaningUp || !_initialized || !IsVisible)
                return;

            var popup = new Window
            {
                Owner = this,
                Title = $"{VideoUrlService.GetDisplayName(Source)} 로그인",
                Width = 520,
                Height = 680,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = System.Windows.Media.Brushes.White
            };

            childWindow = popup;

            var webView = new WebView2();
            popup.Content = webView;

            popup.Closed += (_, _) =>
            {
                closed.TrySetResult();
                _childWindows.Remove(popup);

                try
                {
                    webView.Dispose();
                }
                catch (Exception ex)
                {
                    Trace.TraceError(
                        "[Login] popup dispose: {0}, HRESULT={1:X8}",
                        ex.GetType().Name, ex.HResult);
                }
            };

            _childWindows.Add(popup);

            // 핵심: HWND와 visual tree 호스팅을 먼저 만든다.
            stage = "show";
            popup.Show();

            if (closed.Task.IsCompleted ||
                _isCleaningUp || !_initialized || !IsVisible)
            {
                return;
            }

            stage = "initialize";
            Trace.WriteLine("[Login] popup initialization started");

            Task initialization =
                webView.EnsureCoreWebView2Async(environment, options);

            _ = initialization.ContinueWith(
                static task => { _ = task.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            await Task.WhenAny(initialization, closed.Task)
                .WaitAsync(TimeSpan.FromSeconds(15));

            if (closed.Task.IsCompleted ||
                _isCleaningUp || !_initialized || !IsVisible)
            {
                return;
            }

            await initialization;

            var core = webView.CoreWebView2
                ?? throw new InvalidOperationException(
                    "Popup WebView2 initialization returned no CoreWebView2.");

            stage = "configure";
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;

            core.NavigationStarting += (_, args) =>
            {
                bool navigationAllowed =
                    string.Equals(args.Uri, "about:blank",
                        StringComparison.OrdinalIgnoreCase) ||
                    LoginSecurityPolicy.IsAllowedTopLevelUri(
                        args.Uri, Source);

                TraceNavigation(
                    "Child_NavigationStarting",
                    args.Uri, navigationAllowed, args.IsUserInitiated,
                    args.NavigationId, args.IsRedirected);

                if (!navigationAllowed)
                {
                    args.Cancel = true;
                    HandleBlockedNavigation(args.Uri, args.IsUserInitiated);
                }
            };

            core.FrameNavigationStarting += (_, args) =>
            {
                bool frameAllowed =
                    LoginSecurityPolicy.IsAllowedFrameUri(args.Uri, Source);

                TraceNavigation(
                    "Child_FrameNavigationStarting",
                    args.Uri, frameAllowed, args.IsUserInitiated,
                    args.NavigationId, args.IsRedirected);

                args.Cancel = !frameAllowed;
            };

            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += CoreWebView2_PermissionRequested;
            core.NewWindowRequested += (_, args) => args.Handled = true;

            core.WindowCloseRequested += (_, _) =>
            {
                _ = Dispatcher.BeginInvoke(new Action(async () =>
                {
                    if (!closed.Task.IsCompleted)
                        CloseLoginPopup(popup);

                    if (Source == VideoSource.RPlay && LoginWebView?.CoreWebView2 != null)
                    {
                        try
                        {
                            if (LoginWebView.Source?.AbsoluteUri.Contains("/login", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                LoginWebView.CoreWebView2.Navigate(_targetUrl);
                            }
                            else
                            {
                                LoginWebView.CoreWebView2.Reload();
                            }
                        }
                        catch { }
                    }
                }));
            };

            stage = "attach";
            e.NewWindow = core;
            attached = true;
            Trace.WriteLine("[Login] popup attached");
        }
        catch (TimeoutException)
        {
            Trace.TraceError("[Login] popup initialization timed out");

            if (IsVisible && !_isCleaningUp)
            {
                LoginStatusText.Text =
                    "로그인 팝업을 준비하지 못했습니다. 다시 시도해주세요.";
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError(
                "[Login] popup stage={0}, error={1}, HRESULT={2:X8}",
                stage, ex.GetType().Name, ex.HResult);

            if (!closed.Task.IsCompleted && IsVisible && !_isCleaningUp)
                LoginStatusText.Text = "로그인 팝업을 열지 못했습니다.";
        }
        finally
        {
            try
            {
                deferral?.Complete();
            }
            catch (Exception ex)
            {
                Trace.TraceError(
                    "[Login] popup deferral: {0}, HRESULT={1:X8}",
                    ex.GetType().Name, ex.HResult);
            }
            finally
            {
                if (!attached && childWindow != null &&
                    !closed.Task.IsCompleted)
                {
                    CloseLoginPopup(childWindow);
                }
            }
        }
    }

    private static void CloseLoginPopup(Window popup)
    {
        try
        {
            popup.Close();
        }
        catch (Exception ex)
        {
            Trace.TraceError(
                "[Login] popup close: {0}, HRESULT={1:X8}",
                ex.GetType().Name, ex.HResult);
        }
    }

    private static string DiagnosticOrigin(string? value) =>
        LoginSecurityPolicy.GetDiagnosticOrigin(value);

    private void TraceNavigation(
        string kind,
        string? uri,
        bool allowed,
        bool userInitiated,
        ulong? navigationId = null,
        bool? redirected = null)
    {
        Debug.WriteLine(
            $"[Login] utc={DateTimeOffset.UtcNow:O} " +
            $"source={VideoUrlService.GetDisplayName(Source)} event={kind} " +
            (navigationId.HasValue ? $"nav={navigationId.Value} " : "") +
            $"origin={DiagnosticOrigin(uri)} " +
            $"allowed={allowed} user={userInitiated} " +
            (redirected.HasValue ? $"redirect={redirected.Value}" : ""));
    }

    private void TraceNavigation(
        string kind,
        CoreWebView2NavigationStartingEventArgs e,
        bool allowed) =>
        TraceNavigation(kind, e.Uri, allowed, e.IsUserInitiated, e.NavigationId, e.IsRedirected);

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _navigationCoordinator.OnNavigationStarting(e.NavigationId);

        var allowed = LoginSecurityPolicy.IsAllowedTopLevelUri(e.Uri, Source);
        TraceNavigation("NavigationStarting", e, allowed);

        if (allowed)
        {
            CurrentOriginText.Text = LoginSecurityPolicy.GetDisplayOrigin(e.Uri);
            return;
        }

        e.Cancel = true;
        var displayOrigin = LoginSecurityPolicy.GetDisplayOrigin(e.Uri);
        _navigationCoordinator.RecordPolicyBlock(e.NavigationId, displayOrigin);
        HandleBlockedNavigation(e.Uri, e.IsUserInitiated);
    }

    private void CoreWebView2_FrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var allowed = LoginSecurityPolicy.IsAllowedFrameUri(e.Uri, Source);
        TraceNavigation("FrameNavigationStarting", e, allowed);

        if (allowed)
            return;

        e.Cancel = true;
    }

    private void CoreWebView2_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
        LoginStatusText.Text = "로그인 전용 창에서는 파일 다운로드를 허용하지 않습니다.";
    }

    private void CoreWebView2_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        Debug.WriteLine($"[Login] PermissionRequested: {e.PermissionKind}, origin={DiagnosticOrigin(e.Uri)}, user={e.IsUserInitiated}");
        e.State = CoreWebView2PermissionState.Deny;
        e.Handled = true;
        LoginStatusText.Text = "로그인에 필요하지 않은 브라우저 권한 요청을 차단했습니다.";
    }

    private void HandleBlockedNavigation(string? value, bool userInitiated)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isCleaningUp || !_initialized || !IsVisible)
                return;

            ShowBlockedNavigationPrompt(value, userInitiated);
        }));
    }

    private void ShowBlockedNavigationPrompt(string? value, bool userInitiated)
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
        _navigationCoordinator.TryConsumePolicyBlock(e.NavigationId, out var blockedOrigin);

        if (!_navigationCoordinator.IsCurrentNavigation(e.NavigationId, _isCleaningUp) || LoginWebView?.CoreWebView2 == null)
            return;

        await _navigationCoordinator.HandleNavigationCompletedAsync(
            navigationId: e.NavigationId,
            isSuccess: e.IsSuccess,
            blockedOrigin: blockedOrigin,
            tryRecoverAsync: TryRecoverCompletedGoogleLoginAsync,
            setStatusAction: msg => LoginStatusText.Text = msg,
            updateUiAction: () =>
            {
                if (LoginWebView?.CoreWebView2 == null) return;
                BrowserLoadingText.Visibility = Visibility.Collapsed;
                BackButton.IsEnabled = e.IsSuccess && LoginWebView.CoreWebView2.CanGoBack;
                var allowedPage = LoginSecurityPolicy.IsAllowedTopLevelUri(LoginWebView.Source?.AbsoluteUri, Source);
                LoginCompleteButton.IsEnabled = allowedPage;
                CurrentOriginText.Text = LoginSecurityPolicy.GetDisplayOrigin(LoginWebView.Source?.AbsoluteUri);
            },
            stickyStatusMessage: _stickyStatusMessage,
            sessionCleanupHadErrors: SessionCleanupHadErrors,
            consumeStickyMessageAction: consumedMsg =>
            {
                if (_stickyStatusMessage == consumedMsg)
                {
                    _stickyStatusMessage = null;
                }
            },
            isCleaningUp: _isCleaningUp);
    }

    private async Task<bool> TryRecoverCompletedGoogleLoginAsync(ulong navigationId)
    {
        if ((Source != VideoSource.YouTube && Source != VideoSource.RPlay) ||
            _googleLoginRecoveryInProgress ||
            _googleLoginRedirected ||
            _isCleaningUp ||
            LoginWebView?.CoreWebView2 == null ||
            !_navigationCoordinator.IsCurrentNavigation(navigationId, _isCleaningUp) ||
            !LoginSecurityPolicy.IsGoogleAuthenticationUri(LoginWebView.Source?.AbsoluteUri))
        {
            return false;
        }

        _googleLoginRecoveryInProgress = true;
        try
        {
            int[] retryDelaysMilliseconds = [0, 250, 750];
            foreach (var delay in retryDelaysMilliseconds)
            {
                if (delay > 0)
                    await Task.Delay(delay);

                if (_isCleaningUp || LoginWebView?.CoreWebView2 == null ||
                    !_navigationCoordinator.IsCurrentNavigation(navigationId, _isCleaningUp) ||
                    !LoginSecurityPolicy.IsGoogleAuthenticationUri(LoginWebView.Source?.AbsoluteUri))
                    return false;

                if (Source == VideoSource.YouTube)
                {
                    var cookies = await WebViewCookieCollector.CollectAsync(
                        LoginWebView.CoreWebView2,
                        VideoSource.YouTube);

                    if (_isCleaningUp || LoginWebView?.CoreWebView2 == null ||
                        !_navigationCoordinator.IsCurrentNavigation(navigationId, _isCleaningUp) ||
                        !LoginSecurityPolicy.IsGoogleAuthenticationUri(LoginWebView.Source?.AbsoluteUri))
                        return false;

                    if (!WebViewCookieCollector.HasAuthenticatedYouTubeSession(cookies))
                        continue;

                    Cookies = cookies;
                    _googleLoginRedirected = true;
                    var recoveredMsg = "2단계 인증 완료를 확인했습니다. 원래 영상 페이지로 이동합니다.";
                    _navigationCoordinator.TrySetStatus(navigationId, recoveredMsg, _isCleaningUp);
                    LoginStatusText.Text = recoveredMsg;
                    BrowserLoadingText.Text = "YouTube 영상 페이지로 이동하는 중...";
                    BrowserLoadingText.Visibility = Visibility.Visible;
                    LoginWebView.CoreWebView2.Navigate(_targetUrl);
                    return true;
                }

                if (Source == VideoSource.RPlay)
                {
                    var googleCookies = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://accounts.google.com/");

                    if (_isCleaningUp || LoginWebView?.CoreWebView2 == null ||
                        !_navigationCoordinator.IsCurrentNavigation(navigationId, _isCleaningUp) ||
                        !LoginSecurityPolicy.IsGoogleAuthenticationUri(LoginWebView.Source?.AbsoluteUri))
                        return false;

                    var hasGoogleAuth = googleCookies.Any(c =>
                        (c.Name.Equals("SID", StringComparison.OrdinalIgnoreCase) ||
                         c.Name.Equals("SAPISID", StringComparison.OrdinalIgnoreCase) ||
                         c.Name.Equals("SSID", StringComparison.OrdinalIgnoreCase)) &&
                        !string.IsNullOrWhiteSpace(c.Value));

                    if (!hasGoogleAuth)
                        continue;

                    _googleLoginRedirected = true;
                    var rplayMsg = "Google 세션 쿠키가 감지되었습니다. 로그인 창 상단의 [영상 페이지]를 누르거나 해당 사이트 로그인을 완료해주세요.";
                    _navigationCoordinator.TrySetStatus(navigationId, rplayMsg, _isCleaningUp);
                    LoginStatusText.Text = rplayMsg;
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception)
        {
            var errorMsg = $"인증 상태 확인 실패: {exception.Message}";
            if (_navigationCoordinator.TrySetStatus(navigationId, errorMsg, _isCleaningUp) &&
                !_isCleaningUp && LoginWebView?.CoreWebView2 != null)
            {
                LoginStatusText.Text = errorMsg;
            }
            return false;
        }
        finally
        {
            _googleLoginRecoveryInProgress = false;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_initialized && !_isCleaningUp && LoginWebView.CoreWebView2.CanGoBack)
        {
            _stickyStatusMessage = null;
            SessionCleanupHadErrors = false;
            LoginWebView.CoreWebView2.GoBack();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_initialized && !_isCleaningUp)
        {
            _stickyStatusMessage = null;
            SessionCleanupHadErrors = false;
            LoginWebView.CoreWebView2.Reload();
        }
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_initialized && !_isCleaningUp)
        {
            _stickyStatusMessage = null;
            SessionCleanupHadErrors = false;
            LoginWebView.CoreWebView2.Navigate(_targetUrl);
        }
    }

    private async void ClearSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _isCleaningUp)
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

        // Invalidate in-memory session state immediately
        Cookies = [];
        _googleLoginRedirected = false;
        _navigationCoordinator.Reset();

        _isCleaningUp = true;
        foreach (var child in _childWindows.ToArray())
        {
            try { child.Close(); } catch { }
        }
        _childWindows.Clear();

        ClearSessionButton.IsEnabled = false;
        LoginCompleteButton.IsEnabled = false;
        BackButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        HomeButton.IsEnabled = false;

        var errors = new List<string>();
        try
        {
            LoginStatusText.Text = "저장된 로그인 세션을 삭제하는 중…";

            // Clear current active WebView2 session
            try
            {
                await WebViewSessionService.ClearAsync(LoginWebView.CoreWebView2);
            }
            catch (Exception ex)
            {
                errors.Add($"현재 세션({VideoUrlService.GetDisplayName(Source)}): {ex.Message}");
            }

            // Clear stored sessions for all other sources and legacy profiles
            try
            {
                await WebViewSessionService.ClearStoredSessionAsync(this);
            }
            catch (Exception ex)
            {
                errors.Add($"저장된 프로필: {ex.Message}");
            }

            SessionCleared = true;
            if (errors.Count > 0)
            {
                SessionCleanupHadErrors = true;
                SessionCleanupErrorMessage = string.Join(", ", errors);
                _stickyStatusMessage = $"세션 일부 삭제 실패: {SessionCleanupErrorMessage}";
                LoginStatusText.Text = _stickyStatusMessage;
            }
            else
            {
                SessionCleanupHadErrors = false;
                SessionCleanupErrorMessage = null;
                _stickyStatusMessage = "세션을 삭제했습니다. 다시 로그인해주세요.";
                LoginStatusText.Text = _stickyStatusMessage;
            }

            LoginWebView.CoreWebView2.Navigate(_targetUrl);
        }
        catch (Exception exception)
        {
            SessionCleared = true;
            SessionCleanupHadErrors = true;
            SessionCleanupErrorMessage = exception.Message;
            _stickyStatusMessage = $"세션 삭제 실패: {exception.Message}";
            LoginStatusText.Text = _stickyStatusMessage;
        }
        finally
        {
            _isCleaningUp = false;
            ClearSessionButton.IsEnabled = true;
            var allowedPage = LoginSecurityPolicy.IsAllowedTopLevelUri(LoginWebView.Source?.AbsoluteUri, Source);
            LoginCompleteButton.IsEnabled = allowedPage;
            RefreshButton.IsEnabled = true;
            HomeButton.IsEnabled = true;
            BackButton.IsEnabled = LoginWebView.CoreWebView2?.CanGoBack == true;
        }
    }

    private async void LoginCompleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _isCleaningUp)
            return;

        try
        {
            LoginCompleteButton.IsEnabled = false;
            LoginStatusText.Text = "로그인 세션을 확인하는 중…";

            if (Source == VideoSource.RPlay)
            {
                Cookies = [];
                // Give the website time to finish its own account/token refresh.
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    Cookies = await RPlaySessionService.ReadAsync(LoginWebView.CoreWebView2);
                    if (Cookies.Count > 0) break;
                    await Task.Delay(500);
                }
            }
            else
            {
                Cookies = await WebViewCookieCollector.CollectAsync(LoginWebView.CoreWebView2, Source);
            }
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

            if (Source == VideoSource.Soop && !PlaybackCookiePolicy.HasSoopLoginTickets(Cookies))
            {
                LoginStatusText.Text = "SOOP 인증 티켓을 확인하지 못했습니다. SOOP 로그인을 마친 뒤 다시 ‘세션 확인’을 눌러주세요.";
                Cookies = [];
                return;
            }

            if (Source == VideoSource.RPlay && !PlaybackCookiePolicy.HasRPlayLoginSession(Cookies))
            {
                LoginStatusText.Text = "웹 영상 로그인 세션을 확인하지 못했습니다. 해당 사이트 로그인을 마친 뒤 다시 ‘세션 확인’을 눌러주세요.";
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
