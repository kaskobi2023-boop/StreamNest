namespace ChzzkDownloader.Models;

public enum DiagnosticState
{
    Ready,
    Warning,
    Error
}

public sealed record DiagnosticItem(
    string Name,
    DiagnosticState State,
    string Detail,
    bool BlocksDownloads = false);

public sealed class EnvironmentDiagnosticReport
{
    public required IReadOnlyList<DiagnosticItem> Items { get; init; }

    public bool CanDownload => Items.All(item => !item.BlocksDownloads || item.State != DiagnosticState.Error);

    public bool CanUseLogin => Items.FirstOrDefault(item => item.Name == "WebView2") is
        { State: not DiagnosticState.Error };

    public bool HasWarnings => Items.Any(item => item.State == DiagnosticState.Warning);

    public string StatusText => !CanDownload
        ? "환경 확인 필요"
        : HasWarnings || !CanUseLogin
            ? "도구 정상 · 일부 경고"
            : "도구 정상";

    public string ToLogText() => string.Join(
        Environment.NewLine,
        Items.Select(item => $"[{StateLabel(item.State)}] {item.Name}: {item.Detail}"));

    private static string StateLabel(DiagnosticState state) => state switch
    {
        DiagnosticState.Ready => "정상",
        DiagnosticState.Warning => "경고",
        DiagnosticState.Error => "오류",
        _ => "확인"
    };
}
