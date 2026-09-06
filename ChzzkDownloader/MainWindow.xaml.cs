using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ChzzkDownloader.Models;
using ChzzkDownloader.Services;
using Microsoft.Win32;

namespace ChzzkDownloader;

public partial class MainWindow : Window
{
    private readonly ChzzkProbeService _probeService = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly ToolDiagnosticService _toolDiagnosticService = new();
    private readonly ThumbnailService _thumbnailService = new();
    private readonly YtDlpService _ytDlpService;
    private readonly AppSettings _settings;
    private readonly SessionCookieStore _siteCookies = new();
    private VideoInfo? _currentVideo;
    private VideoSource? _currentSource;
    private CancellationTokenSource? _operationCancellation;
    private bool _isBusy;
    private bool _isDownloading;
    private bool _deletePartialFilesOnCancellation;
    private string? _lastDownloadedFile;
    private CancellationTokenSource? _diagnosticCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private bool _downloadEnvironmentReady;
    private bool _webViewReady;
    private bool _closeWhenIdle;
    private bool _allowClose;
    private bool _startupUrlPending;
    private bool _uiReady;
    private int _activeTabIndex;
    private readonly string[] _tabUrls = [string.Empty, string.Empty];
    private string? _analyzedUrl;
    private WebVideoItem? _currentWebVideo;
    private bool IsGeneralWeb => ModeTabs.SelectedIndex == 1;

    public MainWindow()
    {
        InitializeComponent();
        Title = "StreamNest 다운로더 0.6.6";
        _uiReady = true;
        _ytDlpService = new YtDlpService(new CookieFileService());
        _settings = _settingsService.Load();
        OutputFolderTextBox.Text = _settings.OutputFolder;
        AppendLog("준비되었습니다. 치지직 또는 YouTube 영상 주소를 입력해주세요.");
        Loaded += MainWindow_Loaded;

        var startupUrl = Environment.GetCommandLineArgs()
            .Skip(1)
            .FirstOrDefault(argument => VideoUrlService.TryParse(argument, out _) || WebVideoUrlService.TryParse(argument, out _));
        if (!string.IsNullOrWhiteSpace(startupUrl))
        {
            if (!VideoUrlService.TryParse(startupUrl, out _)) ModeTabs.SelectedIndex = 1;
            UrlTextBox.Text = startupUrl;
            _startupUrlPending = true;
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RunDiagnosticsAsync(showDetails: false);

        if (_startupUrlPending && _downloadEnvironmentReady)
        {
            _startupUrlPending = false;
            await AnalyzeAsync();
        }
        else
        {
            UrlTextBox.Focus();
        }
    }

    private async void DiagnosticsButton_Click(object sender, RoutedEventArgs e) =>
        await RunDiagnosticsAsync(showDetails: true);

    private async Task RunDiagnosticsAsync(bool showDetails)
    {
        if (_isBusy)
            return;

        _diagnosticCancellation?.Cancel();
        _diagnosticCancellation?.Dispose();
        _diagnosticCancellation = new CancellationTokenSource();
        DiagnosticsButton.IsEnabled = false;
        DiagnosticsButton.Content = "환경 점검 중…";
        AnalyzeButton.IsEnabled = false;

        try
        {
            var report = await _toolDiagnosticService.RunAsync(
                OutputFolderTextBox.Text.Trim(),
                _diagnosticCancellation.Token);
            _downloadEnvironmentReady = report.CanDownload;
            _webViewReady = report.CanUseLogin;

            DiagnosticsButton.Content = report.StatusText;
            DiagnosticsButton.ToolTip = report.ToLogText();
            DiagnosticsButton.Background = CreateBrush(report.CanDownload ? "#163D2B" : "#4A2430");
            DiagnosticsButton.Foreground = CreateBrush(report.CanDownload ? "#81E7AD" : "#FFB8C7");
            AppendLog($"환경 점검 결과{Environment.NewLine}{report.ToLogText()}");

            if (!report.CanDownload)
            {
                ShowState("환경 확인 필요", "#FF7D91");
                ProgressText.Text = "필수 다운로드 도구 또는 저장 폴더에 문제가 있습니다. 상세 작업 기록을 확인해주세요.";
                LogExpander.IsExpanded = true;
            }
            else if (!report.CanUseLogin)
            {
                ProgressText.Text = "공개 영상은 받을 수 있지만 로그인 영상에는 WebView2 Runtime이 필요합니다.";
                if (showDetails)
                    LogExpander.IsExpanded = true;
            }
            else if (showDetails)
            {
                ProgressText.Text = "다운로드 환경이 정상입니다.";
                LogExpander.IsExpanded = true;
            }
        }
        catch (OperationCanceledException)
        {
            // 새 진단이나 앱 종료로 취소된 경우 기존 화면 상태를 유지한다.
        }
        catch (Exception exception)
        {
            _downloadEnvironmentReady = false;
            DiagnosticsButton.Content = "환경 확인 필요";
            DiagnosticsButton.Background = CreateBrush("#4A2430");
            DiagnosticsButton.Foreground = CreateBrush("#FFB8C7");
            ProgressText.Text = "환경 점검을 완료하지 못했습니다. 상세 작업 기록을 확인해주세요.";
            AppendLog(exception.ToString());
            LogExpander.IsExpanded = true;
        }
        finally
        {
            DiagnosticsButton.IsEnabled = true;
            AnalyzeButton.IsEnabled = !_isBusy && _downloadEnvironmentReady;
        }
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

    private async void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_isBusy)
        {
            e.Handled = true;
            await AnalyzeAsync();
        }
    }

    private void UrlTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_uiReady || _isBusy)
            return;
        ResetVideoPresentation();
    }

    private void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
            UrlTextBox.Text = Clipboard.GetText().Trim();
    }

    private async Task AnalyzeAsync()
    {
        if (!_downloadEnvironmentReady)
        {
            ProgressText.Text = "먼저 환경 점검 문제를 해결해주세요.";
            return;
        }

        if (IsGeneralWeb)
        {
            await AnalyzeGeneralWebAsync();
            return;
        }

        var url = UrlTextBox.Text.Trim();
        if (!VideoUrlService.TryParse(url, out var urlInfo))
        {
            ShowState("주소 확인 필요", "#FFB86B");
            ProgressText.Text = "치지직 또는 YouTube 영상 주소를 입력해주세요.";
            return;
        }

        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        var cancellationToken = _operationCancellation.Token;
        var canonicalUrl = urlInfo.CanonicalUrl;
        SetBusy(true, "영상 정보를 확인하는 중…");
        _currentSource = urlInfo.Source;
        LoginButton.Visibility = Visibility.Collapsed;
        _currentVideo = null;
        QualityComboBox.ItemsSource = null;
        ThumbnailPresenter.Apply(ThumbnailImage, ThumbnailPlaceholder, null);
        AppendLog($"영상 확인: {urlInfo.Source} / {urlInfo.VideoId}");
        ProbeResult? probe = null;

        try
        {
            probe = urlInfo.Source == VideoSource.Chzzk
                ? await _probeService.ProbeAsync(canonicalUrl, cancellationToken)
                : new ProbeResult
                {
                    IsValidUrl = true,
                    VideoId = urlInfo.VideoId,
                    ChannelName = "YouTube"
                };
            DisplayProbe(probe);

            if (!probe.IsValidUrl)
            {
                ShowState("확인 실패", "#FF7D91");
                ProgressText.Text = probe.Error ?? "영상 정보를 확인하지 못했습니다.";
                AppendLog(ProgressText.Text);
                return;
            }

            if (!string.IsNullOrWhiteSpace(probe.Error))
            {
                AppendLog($"공개 영상 정보 확인 경고: {probe.Error}");
                ProgressText.Text = GetCookies(urlInfo.Source).Count > 0
                    ? "공개 정보 확인을 건너뛰고 로그인 세션으로 영상을 확인하는 중…"
                    : "공개 정보 API 대신 다운로드 엔진으로 영상을 확인하는 중…";
            }

            if (probe.RequiresLogin && GetCookies(urlInfo.Source).Count == 0)
            {
                ShowLoginRequired(probe.LoginReason);
                return;
            }

            var cookiesForSource = GetCookies(urlInfo.Source);
            var video = await _ytDlpService.AnalyzeAsync(canonicalUrl, cookiesForSource, cancellationToken);
            _analyzedUrl = canonicalUrl;
            _currentVideo = video;
            DisplayVideo(video);
            QualityComboBox.ItemsSource = video.Formats;
            QualityComboBox.SelectedIndex = 0;
            QualityComboBox.IsEnabled = true;
            DownloadButton.IsEnabled = true;
            var hasSession = cookiesForSource.Count > 0;
            ShowState(hasSession ? "로그인 세션 확인됨" : "다운로드 가능", "#77E8A8");
            if (hasSession)
            {
                SessionStatusText.Text = "로그인 세션 확인됨";
                SessionStatusText.Foreground = CreateBrush("#81E7AD");
            }
            ProgressText.Text = $"{video.Formats.Count}개 화질을 찾았습니다. 화질을 선택하고 다운로드하세요.";
            AppendLog($"분석 완료: {video.Title} / 화질 {video.Formats.Count}개");
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "작업을 취소했습니다.";
            AppendLog("영상 확인을 취소했습니다.");
        }
        catch (YtDlpException exception)
        {
            AppendLog(exception.Message);
            var errorKind = YtDlpErrorClassifier.Classify(exception.Message);
            if (errorKind == YtDlpErrorKind.Authentication)
                ShowLoginRequired("로그인 세션을 확인하지 못했습니다. 로그인 상태와 영상 시청 권한을 다시 확인해주세요.");
            else
            {
                ShowState("분석 실패", "#FF7D91");
                ProgressText.Text = YtDlpErrorClassifier.ToUserMessage(errorKind, "분석");
                LogExpander.IsExpanded = true;
            }
        }
        catch (Exception exception)
        {
            ShowState("오류", "#FF7D91");
            ProgressText.Text = "영상 분석 중 예상하지 못한 오류가 발생했습니다. 상세 작업 기록을 확인해주세요.";
            AppendLog(exception.ToString());
            LogExpander.IsExpanded = true;
        }
        finally
        {
            CompleteBusyOperation();
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsGeneralWeb) return;
        if (!VideoUrlService.TryParse(UrlTextBox.Text.Trim(), out var urlInfo))
        {
            ProgressText.Text = "영상 주소가 변경되었습니다. 영상을 다시 확인해주세요.";
            ResetVideoPresentation();
            return;
        }

        var loginWindow = new LoginWindow(urlInfo.CanonicalUrl) { Owner = this };
        var accepted = loginWindow.ShowDialog() == true;
        if (loginWindow.SessionCleared)
        {
            _siteCookies.Clear();
            SessionStatusText.Text = "공개 영상 모드";
            SessionStatusText.Foreground = CreateBrush("#81E7AD");
            LogoutButton.Visibility = Visibility.Collapsed;
            AppendLog("저장된 모든 서비스 세션과 메모리 쿠키를 삭제했습니다.");
        }
        if (!accepted)
        {
            AppendLog("로그인 창을 닫았습니다.");
            return;
        }

        _siteCookies.Set(urlInfo.Source, loginWindow.Cookies);
        SessionStatusText.Text = "세션 가져옴 · 확인 중";
        SessionStatusText.Foreground = CreateBrush("#FFCE6B");
        LogoutButton.Visibility = Visibility.Visible;
        AppendLog($"{urlInfo.Source} 로그인 세션을 가져왔습니다. ({loginWindow.Cookies.Count}개 쿠키, 값은 기록하지 않음)");
        await AnalyzeAsync();
    }

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        var confirmation = MessageBox.Show(
            this,
            "로그아웃하고 앱에 연결된 치지직·YouTube 로그인 세션을 모두 삭제할까요?",
            "로그아웃",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        SetBusy(true, "저장된 로그인 세션을 삭제하는 중…");
        CancelButton.IsEnabled = false;
        try
        {
            await WebViewSessionService.ClearStoredSessionAsync(this);
            _siteCookies.Clear();
            _currentVideo = null;
            _currentSource = null;
            QualityComboBox.ItemsSource = null;
            QualityComboBox.IsEnabled = false;
            DownloadButton.IsEnabled = false;
            LoginButton.Visibility = Visibility.Collapsed;
            SessionStatusText.Text = "공개 영상 모드";
            SessionStatusText.Foreground = CreateBrush("#81E7AD");
            LogoutButton.Visibility = Visibility.Collapsed;
            ShowState("로그아웃", "#B9C7D6");
            ProgressText.Text = "로그아웃했습니다. 저장된 모든 서비스 로그인 세션도 삭제했습니다.";
            AppendLog("로그아웃 완료: 메모리 쿠키와 저장된 WebView2 세션을 삭제했습니다.");
        }
        catch (Exception exception)
        {
            ShowState("로그아웃 실패", "#FF7D91");
            ProgressText.Text = $"저장된 로그인 세션을 삭제하지 못했습니다: {exception.Message}";
            AppendLog(exception.ToString());
            LogExpander.IsExpanded = true;
        }
        finally
        {
            CompleteBusyOperation();
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentVideo is null || QualityComboBox.SelectedItem is not VideoFormatOption format)
            return;

        var url = IsGeneralWeb
            ? (WebVideoUrlService.TryParse(UrlTextBox.Text, out var webUri) ? webUri.AbsoluteUri : null)
            : (VideoUrlService.TryParse(UrlTextBox.Text.Trim(), out var urlInfo) ? urlInfo.CanonicalUrl : null);
        if (url is null || url != _analyzedUrl)
        {
            ProgressText.Text = "영상 주소가 변경되었습니다. 영상을 다시 확인해주세요.";
            ResetVideoPresentation();
            return;
        }

        if (IsGeneralWeb)
        {
            if (_currentWebVideo is null) return;
            format = _currentWebVideo.SelectFormat(format);
        }
        var outputFolder = OutputFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            ProgressText.Text = "저장 폴더를 선택해주세요.";
            return;
        }

        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        _deletePartialFilesOnCancellation = false;
        DownloadProgressBar.Value = 0;
        SetBusy(true, "다운로드를 시작하는 중…", downloading: true);
        AppendLog($"다운로드 시작: {format.Label}");

        var progress = new Progress<DownloadProgressInfo>(info =>
        {
            if (info.Percent.HasValue)
                DownloadProgressBar.Value = Math.Clamp(info.Percent.Value, 0, 100);
            ProgressText.Text = info.Message;
        });
        var cookiesForSource = GetCookies(_currentSource);

        try
        {
            var finalPath = await _ytDlpService.DownloadAsync(
                url,
                outputFolder,
                format,
                cookiesForSource,
                progress,
                line => Dispatcher.InvokeAsync(() => AppendLog(line)),
                _operationCancellation.Token);

            DownloadProgressBar.Value = 100;
            ShowState("완료", "#77E8A8");
            _lastDownloadedFile = !string.IsNullOrWhiteSpace(finalPath) && File.Exists(finalPath) ? finalPath : null;
            OpenFolderButton.Content = "저장 폴더 열기";
            ProgressText.Text = _lastDownloadedFile is null
                ? "다운로드와 영상 병합이 완료되었습니다."
                : $"완료: {Path.GetFileName(_lastDownloadedFile)}";
            AppendLog(string.IsNullOrWhiteSpace(finalPath) ? "다운로드 완료" : $"저장 완료: {finalPath}");
        }
        catch (OperationCanceledException)
        {
            if (_deletePartialFilesOnCancellation)
            {
                var cleanupResult = await _ytDlpService.DeletePartialFilesAsync(url, outputFolder, format);
                if (cleanupResult.Succeeded)
                {
                    ProgressText.Text = "다운로드를 취소하고 잔여 파일을 삭제했습니다.";
                    AppendLog("사용자가 다운로드를 취소했습니다. 해당 작업의 잔여 파일을 삭제했습니다.");
                }
                else
                {
                    ProgressText.Text = "다운로드는 취소했지만 일부 잔여 파일을 삭제하지 못했습니다.";
                    AppendLog($"잔여 파일 삭제 실패: {cleanupResult.Error}");
                    LogExpander.IsExpanded = true;
                }
            }
            else
            {
                ProgressText.Text = "다운로드를 취소했습니다. 잔여 파일은 다음 다운로드에서 이어받도록 보관했습니다.";
                AppendLog("사용자가 다운로드를 취소했습니다. 이어받기를 위해 잔여 파일을 보관했습니다.");
            }
            ShowState("취소됨", "#FFB86B");
        }
        catch (YtDlpException exception)
        {
            AppendLog(exception.Message);
            var errorKind = YtDlpErrorClassifier.Classify(exception.Message);
            if (errorKind == YtDlpErrorKind.Authentication)
                ShowLoginRequired("로그인 세션이 만료되었거나 이 영상에 접근 권한이 없습니다.");
            else
            {
                ShowState("다운로드 실패", "#FF7D91");
                ProgressText.Text = YtDlpErrorClassifier.ToUserMessage(errorKind, "다운로드");
                LogExpander.IsExpanded = true;
            }
        }
        catch (Exception exception)
        {
            ShowState("오류", "#FF7D91");
            ProgressText.Text = "다운로드 중 예상하지 못한 오류가 발생했습니다. 상세 작업 기록을 확인해주세요.";
            AppendLog(exception.ToString());
            LogExpander.IsExpanded = true;
        }
        finally
        {
            CompleteBusyOperation();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading)
        {
            var dialog = new CancelDownloadDialog { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            _deletePartialFilesOnCancellation = dialog.Choice == CancelDownloadChoice.DeletePartialFiles;
        }

        CancelButton.IsEnabled = false;
        ProgressText.Text = "작업을 취소하는 중…";
        _operationCancellation?.Cancel();
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "다운로드 저장 폴더 선택",
            InitialDirectory = Directory.Exists(OutputFolderTextBox.Text)
                ? OutputFolderTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        if (dialog.ShowDialog(this) != true)
            return;
        OutputFolderTextBox.Text = dialog.FolderName;
        _settings.OutputFolder = dialog.FolderName;
        _settingsService.Save(_settings);
        AppendLog($"저장 폴더 변경: {dialog.FolderName}");
        await RunDiagnosticsAsync(showDetails: false);
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = OutputFolderService.ResolveSelectedFolder(OutputFolderTextBox.Text);
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
            AppendLog($"저장 폴더 열기: {folder}");
        }
        catch (Exception exception)
        {
            ProgressText.Text = "폴더를 열지 못했습니다. 상세 작업 기록을 확인해주세요.";
            AppendLog(exception.ToString());
            LogExpander.IsExpanded = true;
        }
    }

    private void DisplayProbe(ProbeResult probe)
    {
        if (!string.IsNullOrWhiteSpace(probe.Title))
            VideoTitleText.Text = probe.Title;
        var meta = new[] { probe.ChannelName, FormatDuration(probe.DurationSeconds) }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        VideoMetaText.Text = string.Join(" · ", meta);
        LoadThumbnail(probe.ThumbnailUrl);
    }

    private void DisplayVideo(VideoInfo video)
    {
        VideoTitleText.Text = video.Title;
        var meta = new[] { video.ChannelName, FormatDuration(video.DurationSeconds) }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        VideoMetaText.Text = string.Join(" · ", meta);
        LoadThumbnail(video.ThumbnailUrl);
    }

    private IReadOnlyList<BrowserCookie> GetCookies(VideoSource? source) => IsGeneralWeb ? [] : _siteCookies.Get(source);

    private void LoadThumbnail(string? url)
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = new CancellationTokenSource();
        ThumbnailPresenter.Apply(ThumbnailImage, ThumbnailPlaceholder, null);
        _ = LoadThumbnailAsync(url, _thumbnailCancellation.Token);
    }

    private async Task LoadThumbnailAsync(string? url, CancellationToken cancellationToken)
    {
        try
        {
            var image = await _thumbnailService.LoadAsync(url, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ThumbnailPresenter.Apply(ThumbnailImage, ThumbnailPlaceholder, image);
        }
        catch (OperationCanceledException)
        {
            // 새 영상의 썸네일 요청으로 교체되었거나 창이 닫힌 경우다.
        }
        catch
        {
            ThumbnailPresenter.Apply(ThumbnailImage, ThumbnailPlaceholder, null);
        }
    }

    private void ShowLoginRequired(string? reason)
    {
        _currentVideo = null;
        QualityComboBox.ItemsSource = null;
        QualityComboBox.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        if (IsGeneralWeb)
        {
            LoginButton.Visibility = Visibility.Collapsed;
            ShowState("일반 영상 지원 범위 확인", "#FFCE6B");
            ProgressText.Text = "일반 영상 탭은 로그인 없는 공개 영상을 지원합니다. 인증·DRM·브라우저 전용 재생이 필요한 영상은 지원하지 않을 수 있습니다.";
            return;
        }
        LoginButton.Visibility = Visibility.Visible;
        LoginButton.IsEnabled = _webViewReady && !_isBusy;
        LoginButton.Content = _webViewReady ? "로그인하고 계속" : "WebView2 필요";
        if (GetCookies(_currentSource).Count > 0)
        {
            SessionStatusText.Text = "세션 재확인 필요";
            SessionStatusText.Foreground = CreateBrush("#FFCE6B");
        }
        ShowState("로그인 필요", "#FFCE6B");
        ProgressText.Text = !_webViewReady
            ? "로그인 창을 열려면 Microsoft Edge WebView2 Runtime이 필요합니다. 환경 점검을 확인해주세요."
            : reason ?? "이 영상을 받으려면 로그인이 필요합니다.";
        AppendLog(ProgressText.Text);
    }

    private void ResetVideoPresentation()
    {
        if (!_uiReady) return;
        _thumbnailCancellation?.Cancel();
        _currentVideo = null;
        _currentSource = null;
        _analyzedUrl = null;
        _currentWebVideo = null;
        WebVideoComboBox.ItemsSource = null;
        WebVideoListPanel.Visibility = Visibility.Collapsed;
        _lastDownloadedFile = null;
        QualityComboBox.ItemsSource = null;
        QualityComboBox.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        LoginButton.Visibility = Visibility.Collapsed;
        ThumbnailPresenter.Apply(ThumbnailImage, ThumbnailPlaceholder, null);
        VideoTitleText.Text = "주소를 입력하고 ‘영상 확인’을 눌러주세요.";
        VideoMetaText.Text = IsGeneralWeb ? "공개 웹페이지·MP4·HLS 주소를 분석합니다." : "공개 영상은 로그인 없이 바로 분석됩니다.";
        ShowState("대기 중", "#B9C7D6");
        DownloadProgressBar.Value = 0;
        ProgressText.Text = "다운로드 준비가 되면 시작 버튼이 활성화됩니다.";
        OpenFolderButton.Content = "저장 폴더 열기";
    }

    private void CopyLogButton_Click(object sender, RoutedEventArgs e)
    {
        var text = LogTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ProgressText.Text = "복사할 작업 기록이 없습니다.";
            return;
        }

        try
        {
            Clipboard.SetText(text);
            ProgressText.Text = "작업 기록을 클립보드에 복사했습니다.";
        }
        catch (Exception exception)
        {
            ProgressText.Text = $"작업 기록을 복사하지 못했습니다: {exception.Message}";
        }
    }

    private void SetBusy(bool busy, string? message = null, bool downloading = false)
    {
        _isBusy = busy;
        _isDownloading = busy && downloading;
        ModeTabs.IsEnabled = !busy;
        WebRefererPathCheckBox.IsEnabled = !busy;
        WebVideoComboBox.IsEnabled = !busy;
        UrlTextBox.IsEnabled = !busy;
        PasteButton.IsEnabled = !busy;
        AnalyzeButton.IsEnabled = !busy && _downloadEnvironmentReady;
        DiagnosticsButton.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        QualityComboBox.IsEnabled = !busy && _currentVideo is not null;
        DownloadButton.IsEnabled = !busy && _currentVideo is not null;
        LoginButton.IsEnabled = !busy && _webViewReady;
        LogoutButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        CancelButton.Content = busy && downloading ? "다운로드 취소" : "작업 취소";
        if (!string.IsNullOrWhiteSpace(message))
            ProgressText.Text = message;
        if (busy && !downloading)
            DownloadProgressBar.Value = 0;
    }

    private void CompleteBusyOperation()
    {
        SetBusy(false);
        if (!_closeWhenIdle)
            return;

        _allowClose = true;
        Dispatcher.BeginInvoke(Close);
    }

    private void ShowState(string text, string color)
    {
        VideoStateText.Text = text;
        VideoStateText.Foreground = CreateBrush(color);
        VideoStateBadge.Background = CreateBrush("#1D2938");
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        var normalized = IsGeneralWeb ? WebVideoUrlService.RedactQueries(message.Trim()) : message.Trim();
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {normalized}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private void ModeTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_uiReady || e.Source != ModeTabs || _isBusy) return;
        _tabUrls[_activeTabIndex] = UrlTextBox.Text;
        _activeTabIndex = ModeTabs.SelectedIndex;
        UrlTextBox.Text = _tabUrls[_activeTabIndex];
        ResetVideoPresentation();
        UrlKindText.Text = IsGeneralWeb ? "WEB / MP4 / HLS" : "CHZZK / YouTube";
        UrlPlaceholderText.Text = IsGeneralWeb ? "공개 HTTPS 웹페이지 또는 영상 주소를 붙여넣으세요" : "치지직 또는 YouTube 영상 주소를 붙여넣으세요";
        UrlTextBox.ToolTip = UrlPlaceholderText.Text;
        System.Windows.Automation.AutomationProperties.SetName(UrlTextBox, IsGeneralWeb ? "일반 웹 영상 주소" : "치지직 또는 YouTube 영상 주소");
        ModeDescriptionText.Text = IsGeneralWeb
            ? "공개 웹 영상 · 스트림 재사용·화질 통합·파일 검증 · 일부 사이트는 지원되지 않습니다."
            : "기존 치지직·YouTube 다운로드와 로그인 기능을 사용합니다.";
        WebRefererPathCheckBox.Visibility = IsGeneralWeb ? Visibility.Visible : Visibility.Collapsed;
        SessionStatusText.Text = IsGeneralWeb ? "로그인 미사용" : _siteCookies.HasAny ? "저장된 세션 있음" : "공개 영상 모드";
        SessionStatusText.Foreground = CreateBrush("#81E7AD");
        LogoutButton.Visibility = !IsGeneralWeb && _siteCookies.HasAny ? Visibility.Visible : Visibility.Collapsed;
        UrlTextBox.Focus();
    }

    private async Task AnalyzeGeneralWebAsync()
    {
        if (!WebVideoUrlService.TryParse(UrlTextBox.Text, out var uri))
        {
            ShowState("주소 확인 필요", "#FFB86B");
            ProgressText.Text = "공개 HTTPS 웹페이지·MP4·HLS 주소를 입력해주세요. 치지직·YouTube는 전용 탭을 사용해주세요.";
            return;
        }
        ResetVideoPresentation();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        SetBusy(true, "웹 영상 정보를 확인하는 중… (최대 20개)");
        AppendLog("일반 웹 분석: 기존 영상과 Packer/HLS 후보를 함께 확인하고 다운로드 요청 정보를 보존합니다.");
        AppendLog("브라우저 호환 전송: 페이지·HLS 재생목록·영상 조각에 Chrome 요청 방식 적용 (0.6.6)");
        if (WebRefererPathCheckBox.IsChecked == true)
            AppendLog("HLS 호환 요청: 다른 서버에 페이지 경로 전달 허용 (쿼리·로그인 쿠키 제외)");
        try
        {
            var items = await _ytDlpService.AnalyzeWebAsync(uri.AbsoluteUri, _operationCancellation.Token,
                WebRefererPathCheckBox.IsChecked == true);
            _analyzedUrl = uri.AbsoluteUri;
            WebVideoComboBox.ItemsSource = items;
            WebVideoListPanel.Visibility = items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            WebVideoComboBox.SelectedIndex = 0;
            ShowState("다운로드 가능", "#77E8A8");
            ProgressText.Text = items.Count > 1 ? $"영상 {items.Count}개를 찾았습니다. 받을 영상과 화질을 선택해주세요." : "영상을 찾았습니다. 화질을 선택하고 다운로드하세요.";
            AppendLog($"일반 웹 분석 완료: 영상 {items.Count}개 / {uri.Host}");
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "분석이 취소되었거나 제한 시간(2분)을 초과했습니다.";
        }
        catch (Exception exception)
        {
            ShowState("웹 영상 분석 실패", "#FF7D91");
            ProgressText.Text = WebVideoUrlService.RedactQueries(exception.Message);
            AppendLog(ProgressText.Text);
            LogExpander.IsExpanded = true;
        }
        finally { CompleteBusyOperation(); }
    }

    private void WebRefererPath_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiReady && !_isBusy && IsGeneralWeb) ResetVideoPresentation();
    }

    private void WebVideoComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_uiReady || !IsGeneralWeb || WebVideoComboBox.SelectedItem is not WebVideoItem item) return;
        _currentWebVideo = item;
        _currentVideo = item.Video;
        _lastDownloadedFile = null;
        DisplayVideo(item.Video);
        QualityComboBox.ItemsSource = item.Video.Formats;
        QualityComboBox.SelectedIndex = 0;
        QualityComboBox.IsEnabled = !_isBusy;
        DownloadButton.IsEnabled = !_isBusy;
        DownloadProgressBar.Value = 0;
        ShowState("다운로드 가능", "#77E8A8");
        ProgressText.Text = "선택한 영상의 화질을 확인하고 다운로드하세요.";
    }

    private static string FormatDuration(long? seconds) => seconds is > 0
        ? TimeSpan.FromSeconds(seconds.Value).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss")
        : string.Empty;

    private static Brush CreateBrush(string color) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose || !_isBusy)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (_isDownloading)
        {
            var dialog = new CancelDownloadDialog { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Choice == CancelDownloadChoice.Continue)
                return;

            _deletePartialFilesOnCancellation = dialog.Choice == CancelDownloadChoice.DeletePartialFiles;
        }
        else
        {
            var answer = MessageBox.Show(
                this,
                "현재 작업을 취소하고 앱을 닫을까요?",
                "작업 취소 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        _closeWhenIdle = true;
        CancelButton.IsEnabled = false;
        ProgressText.Text = "작업을 취소하고 앱을 닫는 중…";
        _operationCancellation?.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _diagnosticCancellation?.Cancel();
        _diagnosticCancellation?.Dispose();
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        base.OnClosed(e);
    }
}
