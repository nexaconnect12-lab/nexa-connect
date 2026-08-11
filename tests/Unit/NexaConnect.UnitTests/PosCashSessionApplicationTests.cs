using NexaConnect.Services.POS.Application.CashSessions;

namespace NexaConnect.UnitTests;

public sealed class PosCashSessionApplicationTests
{
    [Fact]
    public async Task Open_normalizes_currency_and_delegates_to_application_port()
    {
        var store = new FakeCashSessionStore();
        var service = new CashSessionApplicationService(store);

        Guid result = await service.OpenAsync(
            new OpenCashSessionCommand(Guid.NewGuid(), Guid.NewGuid(), "usd", 100m),
            "cashier-1",
            CancellationToken.None);

        Assert.Equal(store.SessionId, result);
        Assert.Equal("USD", store.Currency);
    }

    [Fact]
    public async Task Movement_rejects_an_unknown_type_before_persistence()
    {
        var store = new FakeCashSessionStore();
        var service = new CashSessionApplicationService(store);

        await Assert.ThrowsAsync<CashSessionValidationException>(() => service.RecordMovementAsync(
            new RecordCashMovementCommand(Guid.NewGuid(), "unknown", 1m, null),
            "cashier-1",
            CancellationToken.None));

        Assert.False(store.MovementRecorded);
    }

    private sealed class FakeCashSessionStore : ICashSessionStore
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public string? Currency { get; private set; }
        public bool MovementRecorded { get; private set; }

        public Task<Guid> OpenAsync(Guid shiftId, Guid storeId, string currency, decimal openingAmount, CancellationToken cancellationToken)
        {
            Currency = currency;
            return Task.FromResult(SessionId);
        }

        public Task RecordMovementAsync(Guid cashSessionId, string movementType, decimal amount, string recordedBy, string? reasonCode, CancellationToken cancellationToken)
        {
            MovementRecorded = true;
            return Task.CompletedTask;
        }

        public Task CloseAsync(Guid cashSessionId, decimal actualClosingAmount, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
