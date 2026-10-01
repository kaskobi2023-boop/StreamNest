namespace ChzzkDownloader.Services;

/// <summary>
/// Manages navigation lifecycle, active navigation ID tracking, policy block tracking,
/// and guarantees that status updates and UI transitions from obsolete or in-flight navigations
/// cannot overwrite or race against the active navigation.
/// </summary>
public class LoginNavigationCoordinator
{
    private readonly Dictionary<ulong, string> _policyBlockedNavigations = new();

    public ulong CurrentNavigationId { get; private set; }
    public string? CurrentStatusMessage { get; private set; }

    public void OnNavigationStarting(ulong navigationId)
    {
        CurrentNavigationId = navigationId;
    }

    public void RecordPolicyBlock(ulong navigationId, string displayOrigin)
    {
        _policyBlockedNavigations[navigationId] = displayOrigin;
        CurrentStatusMessage = $"보안 정책으로 페이지 이동이 차단되었습니다 ({displayOrigin}).";
    }

    public bool TryConsumePolicyBlock(ulong navigationId, out string? blockedOrigin)
    {
        return _policyBlockedNavigations.Remove(navigationId, out blockedOrigin);
    }

    public bool IsCurrentNavigation(ulong navigationId, bool isCleaningUp = false)
    {
        return !isCleaningUp && CurrentNavigationId == navigationId;
    }

    public bool ShouldProcessNavigationCompleted(ulong navigationId, bool isCleaningUp = false)
    {
        return IsCurrentNavigation(navigationId, isCleaningUp);
    }

    public bool TrySetStatus(ulong navigationId, string message, bool isCleaningUp = false)
    {
        if (!IsCurrentNavigation(navigationId, isCleaningUp))
            return false;

        CurrentStatusMessage = message;
        return true;
    }

    public void Reset()
    {
        CurrentNavigationId = 0;
        CurrentStatusMessage = null;
        _policyBlockedNavigations.Clear();
    }

    /// <summary>
    /// Coordinates the completion of a navigation event. Handles early rejection of stale events,
    /// policy block precedence, asynchronous recovery execution with exception handling,
    /// post-await staleness rejection, and race-free status updates.
    /// </summary>
    public async Task<bool> HandleNavigationCompletedAsync(
        ulong navigationId,
        bool isSuccess,
        string? blockedOrigin,
        Func<ulong, Task<bool>>? tryRecoverAsync,
        Action<string> setStatusAction,
        Action? updateUiAction = null,
        string? stickyStatusMessage = null,
        bool sessionCleanupHadErrors = false,
        Action<string>? consumeStickyMessageAction = null,
        bool isCleaningUp = false)
    {
        // 1. Early rejection: stale completions must not update UI or status
        if (!IsCurrentNavigation(navigationId, isCleaningUp))
            return false;

        updateUiAction?.Invoke();

        // 2. Policy block precedence: policy block notice takes precedence over recovery
        if (!string.IsNullOrEmpty(blockedOrigin))
        {
            var blockedMsg = $"보안 정책으로 페이지 이동이 차단되었습니다 ({blockedOrigin}).";
            TrySetStatus(navigationId, blockedMsg, isCleaningUp);
            setStatusAction(blockedMsg);
            return true;
        }

        // 3. Attempt async recovery if navigation was not successful
        if (!isSuccess && tryRecoverAsync != null)
        {
            bool recovered = false;
            try
            {
                recovered = await tryRecoverAsync(navigationId);
            }
            catch (Exception ex)
            {
                var errorMsg = $"인증 상태 확인 실패: {ex.Message}";
                if (TrySetStatus(navigationId, errorMsg, isCleaningUp))
                {
                    setStatusAction(errorMsg);
                }
                return false;
            }

            // Immediately check post-await guard
            if (!IsCurrentNavigation(navigationId, isCleaningUp))
                return false;

            if (recovered)
                return true;
        }

        // 4. Post-await rejection: verify navigation is still current after async work
        if (!IsCurrentNavigation(navigationId, isCleaningUp))
            return false;

        // 5. Fallback status assignment
        string statusMsg;
        bool consumedSticky = false;
        if (!string.IsNullOrEmpty(stickyStatusMessage))
        {
            statusMsg = stickyStatusMessage;
            consumedSticky = true;
        }
        else
        {
            statusMsg = isSuccess
                ? "로그인을 마쳤다면 ‘세션 확인’을 누르세요."
                : "페이지를 불러오지 못했습니다. 네트워크 연결을 확인해주세요.";
        }

        if (TrySetStatus(navigationId, statusMsg, isCleaningUp))
        {
            setStatusAction(statusMsg);
            if (consumedSticky && !sessionCleanupHadErrors)
            {
                consumeStickyMessageAction?.Invoke(statusMsg);
            }
            return true;
        }

        return false;
    }
}
