using ChzzkDownloader.Services;
using Xunit;

namespace ChzzkDownloader.Tests;

public class LoginNavigationCoordinatorTests
{
    [Fact]
    public async Task Sequence_A_RecoveryReturnsFalse_After_B_Blocked_DoesNotOverwrite_Status()
    {
        var coord = new LoginNavigationCoordinator();
        string? displayedStatus = null;

        // 1. Navigation A (id 1) starts
        coord.OnNavigationStarting(1);

        // 2. Navigation A completes with failure; recovery task is started but remains pending
        var tcsA = new TaskCompletionSource<bool>();
        var completionTaskA = coord.HandleNavigationCompletedAsync(
            navigationId: 1,
            isSuccess: false,
            blockedOrigin: null,
            tryRecoverAsync: _ => tcsA.Task,
            setStatusAction: msg => displayedStatus = msg);

        // 3. Navigation B (id 2) starts and is blocked by policy
        coord.OnNavigationStarting(2);
        coord.RecordPolicyBlock(2, "attacker.com");
        coord.TryConsumePolicyBlock(2, out var blockedB);
        await coord.HandleNavigationCompletedAsync(
            navigationId: 2,
            isSuccess: false,
            blockedOrigin: blockedB,
            tryRecoverAsync: _ => Task.FromResult(false),
            setStatusAction: msg => displayedStatus = msg);

        Assert.Equal("보안 정책으로 페이지 이동이 차단되었습니다 (attacker.com).", displayedStatus);
        Assert.Equal("보안 정책으로 페이지 이동이 차단되었습니다 (attacker.com).", coord.CurrentStatusMessage);

        // 4. Navigation A resumes and returns false
        tcsA.SetResult(false);
        var resultA = await completionTaskA;

        // 5. Assert: A was rejected post-await, and B's status survived!
        Assert.False(resultA);
        Assert.Equal("보안 정책으로 페이지 이동이 차단되었습니다 (attacker.com).", displayedStatus);
        Assert.Equal("보안 정책으로 페이지 이동이 차단되었습니다 (attacker.com).", coord.CurrentStatusMessage);
    }

    [Fact]
    public async Task Sequence_A_RecoveryThrows_After_B_Started_DoesNotOverwrite_Status()
    {
        var coord = new LoginNavigationCoordinator();
        string? displayedStatus = null;

        // 1. Navigation A starts
        coord.OnNavigationStarting(1);

        // 2. Navigation A recovery starts awaiting
        var tcsA = new TaskCompletionSource<bool>();
        var completionTaskA = coord.HandleNavigationCompletedAsync(
            navigationId: 1,
            isSuccess: false,
            blockedOrigin: null,
            tryRecoverAsync: _ => tcsA.Task,
            setStatusAction: msg => displayedStatus = msg);

        // 3. Navigation B starts and completes successfully
        coord.OnNavigationStarting(2);
        await coord.HandleNavigationCompletedAsync(
            navigationId: 2,
            isSuccess: true,
            blockedOrigin: null,
            tryRecoverAsync: null,
            setStatusAction: msg => displayedStatus = msg);

        Assert.Equal("로그인을 마쳤다면 ‘세션 확인’을 누르세요.", displayedStatus);

        // 4. Navigation A throws an exception in its async recovery
        tcsA.SetException(new InvalidOperationException("network reset"));
        var resultA = await completionTaskA;

        // 5. Assert: Exception in stale Navigation A does not overwrite B's status
        Assert.False(resultA);
        Assert.Equal("로그인을 마쳤다면 ‘세션 확인’을 누르세요.", displayedStatus);
        Assert.Equal("로그인을 마쳤다면 ‘세션 확인’을 누르세요.", coord.CurrentStatusMessage);
    }

    [Fact]
    public async Task StaleCompletion_PerformsNoUiUpdates_And_NoStatusUpdates()
    {
        var coord = new LoginNavigationCoordinator();
        string? displayedStatus = "initial";
        bool uiUpdated = false;

        // Navigation 1 starts, then Navigation 2 starts
        coord.OnNavigationStarting(1);
        coord.OnNavigationStarting(2);

        // Navigation 1 completion event arrives late
        var handled = await coord.HandleNavigationCompletedAsync(
            navigationId: 1,
            isSuccess: true,
            blockedOrigin: null,
            tryRecoverAsync: null,
            setStatusAction: msg => displayedStatus = msg,
            updateUiAction: () => uiUpdated = true);

        // Assert: completely ignored
        Assert.False(handled);
        Assert.False(uiUpdated);
        Assert.Equal("initial", displayedStatus);
    }

    [Fact]
    public async Task PolicyBlock_PreemptsRecovery_ZeroInvocations()
    {
        var coord = new LoginNavigationCoordinator();
        string? displayedStatus = null;
        int recoveryCallCount = 0;

        coord.OnNavigationStarting(1);
        coord.RecordPolicyBlock(1, "bad.com");
        coord.TryConsumePolicyBlock(1, out var blockedOrigin);

        var handled = await coord.HandleNavigationCompletedAsync(
            navigationId: 1,
            isSuccess: false,
            blockedOrigin: blockedOrigin,
            tryRecoverAsync: _ => { recoveryCallCount++; return Task.FromResult(true); },
            setStatusAction: msg => displayedStatus = msg);

        Assert.True(handled);
        Assert.Equal(0, recoveryCallCount);
        Assert.Equal("보안 정책으로 페이지 이동이 차단되었습니다 (bad.com).", displayedStatus);
    }

    [Fact]
    public async Task CleanupInProgress_AbortsCompletionAndRecovery()
    {
        var coord = new LoginNavigationCoordinator();
        string? displayedStatus = null;
        int recoveryCallCount = 0;

        coord.OnNavigationStarting(1);

        var handled = await coord.HandleNavigationCompletedAsync(
            navigationId: 1,
            isSuccess: false,
            blockedOrigin: null,
            tryRecoverAsync: _ => { recoveryCallCount++; return Task.FromResult(true); },
            setStatusAction: msg => displayedStatus = msg,
            isCleaningUp: true);

        Assert.False(handled);
        Assert.Equal(0, recoveryCallCount);
        Assert.Null(displayedStatus);
    }

    [Fact]
    public void PolicyBlock_ConsumedByCompleted_PreemptsRecovery()
    {
        var coord = new LoginNavigationCoordinator();

        coord.OnNavigationStarting(1);
        coord.RecordPolicyBlock(1, "evil.com");

        Assert.True(coord.TryConsumePolicyBlock(1, out var origin));
        Assert.Equal("evil.com", origin);

        // Cannot consume twice
        Assert.False(coord.TryConsumePolicyBlock(1, out var origin2));
        Assert.Null(origin2);
    }

    [Fact]
    public void Reset_ClearsActiveNavigationAndBlockedMap()
    {
        var coord = new LoginNavigationCoordinator();

        coord.OnNavigationStarting(5);
        coord.RecordPolicyBlock(5, "evil.com");
        Assert.Equal(5UL, coord.CurrentNavigationId);

        coord.Reset();

        Assert.Equal(0UL, coord.CurrentNavigationId);
        Assert.Null(coord.CurrentStatusMessage);
        Assert.False(coord.TryConsumePolicyBlock(5, out _));
    }

    [Fact]
    public async Task StaleCompletion_DoesNotConsume_StickyStatusMessage_Of_NewNavigation()
    {
        var coord = new LoginNavigationCoordinator();
        string? displayedStatus = null;
        string? stickyMessage = "이전 안내 메시지";
        int stickyConsumeCountA = 0;
        int stickyConsumeCountB = 0;

        // 1. Navigation A (id 1) starts with initial sticky message and begins awaiting recovery
        coord.OnNavigationStarting(1);
        var tcsA = new TaskCompletionSource<bool>();
        var completionTaskA = coord.HandleNavigationCompletedAsync(
            navigationId: 1,
            isSuccess: false,
            blockedOrigin: null,
            tryRecoverAsync: _ => tcsA.Task,
            setStatusAction: msg => displayedStatus = msg,
            stickyStatusMessage: stickyMessage,
            consumeStickyMessageAction: consumed =>
            {
                stickyConsumeCountA++;
                if (stickyMessage == consumed) stickyMessage = null;
            });

        // 2. Session is cleared, coordinator resets, and a new sticky status message is set for the new session
        coord.Reset();
        stickyMessage = "세션을 삭제했습니다. 다시 로그인해주세요.";

        // 3. Navigation B (id 2) starts
        coord.OnNavigationStarting(2);

        // 4. Stale Navigation A completes and returns false
        tcsA.SetResult(false);
        var resultA = await completionTaskA;

        // Assert: Navigation A was rejected, zero consumption callbacks fired for A,
        // and Navigation B's newly set sticky message was completely preserved!
        Assert.False(resultA);
        Assert.Equal(0, stickyConsumeCountA);
        Assert.Equal("세션을 삭제했습니다. 다시 로그인해주세요.", stickyMessage);

        // 5. Navigation B completes and consumes the sticky message
        var resultB = await coord.HandleNavigationCompletedAsync(
            navigationId: 2,
            isSuccess: true,
            blockedOrigin: null,
            tryRecoverAsync: null,
            setStatusAction: msg => displayedStatus = msg,
            stickyStatusMessage: stickyMessage,
            consumeStickyMessageAction: consumed =>
            {
                stickyConsumeCountB++;
                if (stickyMessage == consumed) stickyMessage = null;
            });

        Assert.True(resultB);
        Assert.Equal(1, stickyConsumeCountB);
        Assert.Equal("세션을 삭제했습니다. 다시 로그인해주세요.", displayedStatus);
        Assert.Null(stickyMessage); // Successfully consumed by Navigation B!
    }

    [Fact]
    public async Task RecoveryReturningTrue_RejectedIfStale()
    {
        var coord = new LoginNavigationCoordinator();
        string? displayedStatus = null;

        coord.OnNavigationStarting(1);
        var tcsA = new TaskCompletionSource<bool>();
        var completionTaskA = coord.HandleNavigationCompletedAsync(
            navigationId: 1,
            isSuccess: false,
            blockedOrigin: null,
            tryRecoverAsync: _ => tcsA.Task,
            setStatusAction: msg => displayedStatus = msg);

        // Navigation 2 starts while A is awaiting recovery
        coord.OnNavigationStarting(2);

        // A reports recovery success, but it is now stale
        tcsA.SetResult(true);
        var resultA = await completionTaskA;

        // Assert: must be rejected because navigation 1 is no longer current
        Assert.False(resultA);
        Assert.Null(displayedStatus);
    }
}
