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
        var terminalId = Guid.NewGuid();
        var store = new FakeCashSessionStore();
        var service = new CashSessionApplicationService(store);
        var command = new RecordCashMovementCommand(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "sale",
            12.50m,
            "cash-sale",
            operationId,
            terminalId);

        await service.RecordMovementAsync(command, "cashier-1", CancellationToken.None);

        Assert.True(store.MovementRecorded);
        Assert.Equal(operationId, store.ClientOperationId);
        Assert.Equal(terminalId, store.TerminalId);
        Assert.Equal(64, store.PayloadHash?.Length);
    }

    [Fact]
    public async Task Summary_requires_terminal_scope_and_returns_server_calculated_amounts()
    {
        var store = new FakeCashSessionStore();
        var service = new CashSessionApplicationService(store);

        CashSessionSummary result = await service.GetSummaryAsync(
            store.SessionId, "cashier-1", store.TerminalIdValue, CancellationToken.None);

        Assert.Equal(125m, result.ExpectedClosingAmount);
        Assert.Equal(25m, result.NetMovementAmount);
        await Assert.ThrowsAsync<CashSessionValidationException>(() => service.GetSummaryAsync(
            store.SessionId, "cashier-1", Guid.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task Close_requires_reviewed_version_and_passes_cashier_terminal_scope()
    {
        var store = new FakeCashSessionStore();
        var service = new CashSessionApplicationService(store);

        await service.CloseAsync(store.SessionId, 124m, 7, "cashier-1", store.TerminalIdValue,
            CancellationToken.None);

        Assert.Equal(7, store.ClosedVersion);
        Assert.Equal("cashier-1", store.ClosedSubject);
        await Assert.ThrowsAsync<CashSessionValidationException>(() => service.CloseAsync(
            store.SessionId, 124m, 0, "cashier-1", store.TerminalIdValue, CancellationToken.None));
    }

    private sealed class FakeCashSessionStore : ICashSessionStore
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public string? Currency { get; private set; }
        public bool MovementRecorded { get; private set; }
        public Guid? ClientOperationId { get; private set; }
        public Guid? TerminalId { get; private set; }
        public string? PayloadHash { get; private set; }
        public Guid TerminalIdValue { get; } = Guid.NewGuid();
        public long? ClosedVersion { get; private set; }
        public string? ClosedSubject { get; private set; }

        public Task<Guid> OpenAsync(Guid shiftId, Guid storeId, string currency, decimal openingAmount, CancellationToken cancellationToken)
        {
            Currency = currency;
            return Task.FromResult(SessionId);
        }

        public Task<bool> RecordMovementAsync(Guid cashSessionId, string movementType, decimal amount, string recordedBy, string? reasonCode, Guid? clientOperationId, Guid? terminalId, string payloadHash, CancellationToken cancellationToken)
        {
            MovementRecorded = true;
            ClientOperationId = clientOperationId;
            TerminalId = terminalId;
            PayloadHash = payloadHash;
            return Task.FromResult(true);
        }

        public Task<CashSessionSummary?> GetSummaryAsync(Guid cashSessionId, string subject, Guid terminalId,
            CancellationToken cancellationToken) => Task.FromResult<CashSessionSummary?>(
                cashSessionId == SessionId && subject == "cashier-1" && terminalId == TerminalIdValue
                    ? new CashSessionSummary(SessionId, Guid.NewGuid(), Guid.NewGuid(), TerminalIdValue, "THB",
                        100m, 25m, 125m, null, null, "open", DateTimeOffset.UtcNow, null, 7, [])
                    : null);

        public Task CloseAsync(Guid cashSessionId, decimal actualClosingAmount, long expectedConcurrencyVersion,
            string subject, Guid terminalId, CancellationToken cancellationToken)
        {
            ClosedVersion = expectedConcurrencyVersion;
            ClosedSubject = subject;
            return Task.CompletedTask;
        }
    }
}
