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

    [Fact]
    public async Task Movement_passes_client_operation_id_and_stable_payload_hash_to_store()
    {
        var operationId = Guid.NewGuid();
        var store = new FakeCashSessionStore();
        var service = new CashSessionApplicationService(store);
        var command = new RecordCashMovementCommand(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "sale",
            12.50m,
            "cash-sale",
            operationId);

        await service.RecordMovementAsync(command, "cashier-1", CancellationToken.None);

        Assert.True(store.MovementRecorded);
        Assert.Equal(operationId, store.ClientOperationId);
        Assert.Equal(64, store.PayloadHash?.Length);
    }

    private sealed class FakeCashSessionStore : ICashSessionStore
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public string? Currency { get; private set; }
        public bool MovementRecorded { get; private set; }
        public Guid? ClientOperationId { get; private set; }
        public string? PayloadHash { get; private set; }

        public Task<Guid> OpenAsync(Guid shiftId, Guid storeId, string currency, decimal openingAmount, CancellationToken cancellationToken)
        {
            Currency = currency;
            return Task.FromResult(SessionId);
        }

        public Task<bool> RecordMovementAsync(Guid cashSessionId, string movementType, decimal amount, string recordedBy, string? reasonCode, Guid? clientOperationId, string payloadHash, CancellationToken cancellationToken)
        {
            MovementRecorded = true;
            ClientOperationId = clientOperationId;
            PayloadHash = payloadHash;
            return Task.FromResult(true);
        }

        public Task CloseAsync(Guid cashSessionId, decimal actualClosingAmount, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
