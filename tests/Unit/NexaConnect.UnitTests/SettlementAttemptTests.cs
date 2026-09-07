using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class SettlementAttemptTests
{
    private static LocalPendingSettlementState Attempt() => new(Guid.NewGuid(), 120m, "THB",
        Guid.NewGuid(), "promptpay_manual", true, "test-reference");

    [Fact]
    public async Task Recovery_is_uncertain_before_send_and_cleared_only_after_success()
    {
        var attempt = Attempt();
        LocalPendingSettlementState? saved = null;
        bool sent = false;
        int result = await SettlementAttempt.ExecuteAsync(attempt, state => saved = state,
            () =>
            {
                Assert.Equal(attempt with { OutcomeUncertain = true }, saved);
                sent = true;
                return Task.FromResult(42);
            }, () => { Assert.True(sent); saved = null; });
        Assert.Equal(42, result);
        Assert.Null(saved);
    }

    [Fact]
    public async Task Local_persistence_failure_prevents_network_request()
    {
        bool sent = false;
        await Assert.ThrowsAsync<IOException>(() => SettlementAttempt.ExecuteAsync(Attempt(),
            _ => throw new IOException("Disk unavailable"),
            () => { sent = true; return Task.FromResult(42); },
            () => throw new InvalidOperationException("Must not clear")));
        Assert.False(sent);
    }

    [Fact]
    public async Task Lost_response_retains_identical_tender_and_replay_identity()
    {
        var attempt = Attempt();
        LocalPendingSettlementState? saved = null;
        await Assert.ThrowsAsync<HttpRequestException>(() => SettlementAttempt.ExecuteAsync<int>(attempt,
            state => saved = state, () => throw new HttpRequestException("Lost response"),
            () => throw new InvalidOperationException("Must not clear")));
        Assert.Equal(attempt with { OutcomeUncertain = true }, saved);
        await SettlementAttempt.ExecuteAsync(saved!, state => Assert.Equal(saved, state),
            () => Task.FromResult(42), () => { });
    }

    [Fact]
    public async Task Cleanup_failure_does_not_report_success()
    {
        LocalPendingSettlementState? saved = null;
        await Assert.ThrowsAsync<IOException>(() => SettlementAttempt.ExecuteAsync(Attempt(),
            state => saved = state, () => Task.FromResult(42),
            () => throw new IOException("Cannot remove recovery file")));
        Assert.True(saved!.OutcomeUncertain);
    }
}
