using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.Support;
using NexaConnect.Services.PlatformDirectory.Domain.Support;

namespace NexaConnect.UnitTests;

public sealed class SupportElevationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Request_enforces_the_bounded_duration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SupportElevation.Request(
            Guid.NewGuid(), Guid.NewGuid(), "nexa_connect", "support-1", "Investigate failed tenant synchronization", 241, Now));
    }

    [Fact]
    public void Support_operator_cannot_approve_their_own_elevation()
    {
        SupportElevation elevation = Requested();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => elevation.Approve("support-1", Now));

        Assert.Contains("cannot approve", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Approved_elevation_expires_at_the_approved_duration_boundary()
    {
        SupportElevation elevation = Requested();
        elevation.Approve("platform-admin-1", Now);

        Assert.True(elevation.IsEffective(Now.AddMinutes(59)));
        Assert.False(elevation.IsEffective(Now.AddMinutes(60)));
    }

    [Fact]
    public async Task Application_approval_persists_independent_approval_and_expiry()
    {
        var repository = new FakeRepository { Elevation = Requested() };
        var service = new SupportElevationApplicationService(repository, new FixedTimeProvider(Now));

        SupportElevationSummary? result = await service.ApproveAsync(
            repository.Elevation.Id, "platform-admin-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("active", result.Status);
        Assert.Equal("platform-admin-1", result.ApprovedBySubjectId);
        Assert.Equal(Now.AddMinutes(60), result.ExpiresAtUtc);
        Assert.True(repository.Approved);
    }

    private static SupportElevation Requested() => SupportElevation.Request(
        Guid.NewGuid(), Guid.NewGuid(), "nexa_connect", "support-1",
        "Investigate failed tenant synchronization", 60, Now.AddMinutes(-5));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeRepository : ISupportElevationRepository
    {
        public SupportElevation? Elevation { get; init; }
        public bool Approved { get; private set; }

        public Task CreateAsync(SupportElevation elevation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<SupportElevation?> FindAsync(Guid elevationId, CancellationToken cancellationToken) => Task.FromResult(Elevation);
        public Task<SupportElevation?> FindEffectiveAsync(Guid organizationId, string applicationCode, string supportSubjectId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(Elevation is not null && Elevation.IsEffective(now) ? Elevation : null);
        public Task<bool> TryApproveAsync(SupportElevation elevation, CancellationToken cancellationToken)
        {
            Approved = true;
            return Task.FromResult(true);
        }
        public Task<bool> TryRevokeAsync(SupportElevation elevation, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
