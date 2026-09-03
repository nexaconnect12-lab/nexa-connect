using System.Security.Cryptography;
using System.Text;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;

namespace NexaConnect.Services.Order.Application.ManualTenders;

public sealed record ConfirmManualTenderCommand(Guid OrganizationId, Guid BranchId, Guid OrderId, Guid TerminalId,
    Guid IdempotencyKey, string Method, decimal Amount, string Currency, bool ReceiptConfirmed, string? BankReference,
    string OperatorSubjectId, Guid AuthorizationDecisionId, Guid CorrelationId);
public sealed record ManualTenderResult(Guid SettlementId, Guid OrderId, string Status, string Method, decimal Amount,
    string Currency, DateTimeOffset OccurredAtUtc, bool Replayed);
public sealed record StoredManualTender(Guid SettlementId, Guid OrderId, string Fingerprint, string Method,
    decimal Amount, string Currency, DateTimeOffset OccurredAtUtc);
public enum ManualTenderCommitStatus { Created, Replayed, IdempotencyConflict, StateConflict }
public sealed record ManualTenderCommitResult(ManualTenderCommitStatus Status, StoredManualTender? Settlement);

public interface IManualTenderRepository
{
    Task<StoredManualTender?> FindAsync(Guid organizationId, Guid branchId, Guid idempotencyKey, CancellationToken cancellationToken);
    Task<ManualTenderCommitResult> CommitAsync(OrderAggregate order, ManualTenderSettlement settlement, string fingerprint,
        Guid authorizationDecisionId, OrderManualTenderSettledV1 integrationEvent, PlatformAuditEventV1 audit,
        CancellationToken cancellationToken);
}

public sealed class ManualTenderApplicationService(IOrderRepository orders, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ManualTenderResult?> ConfirmAsync(ConfirmManualTenderCommand command, CancellationToken cancellationToken)
    {
        if (orders is not IOrderLookup lookup || orders is not IManualTenderRepository repository)
            throw new InvalidOperationException("Manual tender settlement requires a durable Order repository.");
        if (command.AuthorizationDecisionId == Guid.Empty || command.CorrelationId == Guid.Empty)
            throw new ArgumentException("Authorization decision and correlation identifiers are required.");
        string fingerprint = Fingerprint(command);
        StoredManualTender? existing = await repository.FindAsync(command.OrganizationId, command.BranchId, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(existing.Fingerprint), Convert.FromHexString(fingerprint)))
                throw new InvalidOperationException("The idempotency key was already used for a different manual settlement.");
            return ToResult(existing, true);
        }
        OrderAggregate? order = await lookup.GetAsync(command.OrderId, cancellationToken);
        if (order is null || order.OrganizationId != command.OrganizationId || order.BranchId != command.BranchId) return null;
        DateTimeOffset now = clock.GetUtcNow();
        ManualTenderSettlement settlement = ManualTenderSettlement.Create(order, command.OrganizationId, command.BranchId,
            command.TerminalId, command.IdempotencyKey, command.Method, command.Amount, command.Currency,
            command.OperatorSubjectId, command.ReceiptConfirmed, command.BankReference, now);
        order.MarkManuallyPaid(settlement);
        var settled = new OrderManualTenderSettledV1(Guid.NewGuid(), command.CorrelationId, now, order.OrganizationId,
            order.RestaurantId, order.BranchId, order.Id, settlement.Id, settlement.TerminalId,
            settlement.Method == ManualTenderMethod.Cash ? "cash" : "promptpay_manual", settlement.Amount, settlement.Currency);
        var audit = new PlatformAuditEventV1(Guid.NewGuid(), command.CorrelationId, now, settlement.OperatorSubjectId,
            order.OrganizationId, "order.manual-tender.settled", "order", order.Id.ToString("D"), "succeeded");
        ManualTenderCommitResult committed = await repository.CommitAsync(order, settlement, fingerprint,
            command.AuthorizationDecisionId, settled, audit, cancellationToken);
        if (committed.Status == ManualTenderCommitStatus.StateConflict)
        {
            StoredManualTender? raced = await repository.FindAsync(command.OrganizationId, command.BranchId,
                command.IdempotencyKey, cancellationToken);
            if (raced is not null)
                committed = new(raced.Fingerprint == fingerprint ? ManualTenderCommitStatus.Replayed
                    : ManualTenderCommitStatus.IdempotencyConflict, raced);
        }
        return committed.Status switch
        {
            ManualTenderCommitStatus.Created => ToResult(committed.Settlement!, false),
            ManualTenderCommitStatus.Replayed when committed.Settlement!.Fingerprint == fingerprint => ToResult(committed.Settlement, true),
            ManualTenderCommitStatus.IdempotencyConflict => throw new InvalidOperationException("The idempotency key was already used for a different manual settlement."),
            _ => throw new InvalidOperationException("The order was concurrently settled or changed; reload it before trying again.")
        };
    }

    private static ManualTenderResult ToResult(StoredManualTender value, bool replayed) =>
        new(value.SettlementId, value.OrderId, "Paid", value.Method, value.Amount, value.Currency, value.OccurredAtUtc, replayed);

    private static string Fingerprint(ConfirmManualTenderCommand command)
    {
        string normalized = string.Join('|', command.OrganizationId.ToString("N"), command.BranchId.ToString("N"),
            command.OrderId.ToString("N"), command.TerminalId.ToString("N"), command.Method.Trim().ToLowerInvariant(),
            command.Amount.ToString("0.00################", System.Globalization.CultureInfo.InvariantCulture),
            command.Currency.Trim().ToUpperInvariant(), command.ReceiptConfirmed ? "1" : "0", command.BankReference?.Trim() ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }
}
