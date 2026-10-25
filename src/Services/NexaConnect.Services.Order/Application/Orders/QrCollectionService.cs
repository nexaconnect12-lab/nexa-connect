using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Order.Application.ManualTenders;
using NexaConnect.Services.Order.Application.Tenant;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain;
using NexaConnect.Services.Order.Domain.Qr;

namespace NexaConnect.Services.Order.Application.Orders;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record QrCollectionCommand(Guid RestaurantId, Guid TerminalId, Guid StoreId, Guid ShiftId, Guid SettlementKey);
public sealed record QrCollectionView(Guid OrderId, Guid OrganizationId, Guid RestaurantId, Guid BranchId,
    string TableCode, string Status, string Currency, decimal TotalAmount, OrderPricing Pricing,
    IReadOnlyList<QrCollectionLine> Lines, Guid SettlementKey);
public sealed record QrCollectionLine(Guid ProductId, string Name, decimal UnitPrice, int Quantity);

public interface IQrCollectionRepository
{
    Task<QrCollectionBinding?> BindingAsync(Guid orderId, CancellationToken ct);
    Task<bool> ClaimAsync(QrCollectionBinding binding, Guid decision, Guid correlation, CancellationToken ct);
}

public sealed class QrCollectionService(IQrRequestRepository requests, IOrderRepository orders,
    IQrCollectionRepository collections, IOrderTenantAuthorizer authorization, IManualTenderAdmission admission,
    TimeProvider clock)
{
    public async Task<QrCollectionView?> CollectAsync(Guid organization, Guid branch, Guid id,
        QrCollectionCommand command, string actor, string bearer, Guid correlation, CancellationToken ct, bool preview = false)
    {
        if (new[] { organization, branch, id, command.RestaurantId, command.TerminalId, command.StoreId,
            command.ShiftId, command.SettlementKey, correlation }.Any(x => x == Guid.Empty)
            || string.IsNullOrWhiteSpace(actor) || actor.Length > 200) throw new ArgumentException("Invalid collection scope.");
        var decision = await authorization.GetBranchDecisionAsync(organization, branch,
            ProductPermissions.OrderManualPaymentConfirm, bearer, ct);
        if (decision is null || decision == Guid.Empty) throw new UnauthorizedAccessException();
        var request = await requests.GetAsync(organization, branch, id, ct);
        if (request?.OrderId != id || request.Request.Scope.RestaurantId != command.RestaurantId) return null;
        var order = await ((IOrderLookup)orders).GetAsync(id, ct);
        if (order is null || order.OrganizationId != organization || order.BranchId != branch
            || order.RestaurantId != command.RestaurantId || order.Channel != "qr") return null;
        var binding = new QrCollectionBinding(id, organization, branch, command.TerminalId, command.StoreId,
            command.ShiftId, command.SettlementKey, actor);
        var existing = await collections.BindingAsync(id, ct);
        if (existing is not null && existing != binding) throw new InvalidOperationException("Original collection belongs to another cashier, terminal, shift or operation. Reconcile it; do not collect again.");
        if (order.Status is not (OrderStatus.KitchenAccepted or OrderStatus.Paid)
            || order.Pricing is null || order.PaymentIntentId is not null)
            throw new InvalidOperationException("Original order is not ready for collection.");
        // A paid result is readable only by the exact retained collecting operation.
        if (order.Status == OrderStatus.Paid && existing is null)
            throw new InvalidOperationException("Original order is already paid. Retrieve its receipt; do not collect again.");
        if (order.Status != OrderStatus.Paid)
        {
            var at = await admission.AdmitAsync(new(organization, order.RestaurantId, branch, command.TerminalId,
                actor, "promptpay_manual", order.Currency, command.ShiftId, command.StoreId), ct);
            if ((clock.GetUtcNow() - at).Duration() > TimeSpan.FromSeconds(30)) throw new HttpRequestException("Collection admission clock is outside its supported boundary.");
            if (!preview && !await collections.ClaimAsync(binding, decision.Value, correlation, ct))
                throw new InvalidOperationException("Original collection changed. Verify its retained identity.");
        }
        return new(id, organization, order.RestaurantId, branch, request.Request.Scope.TableCode,
            order.Status.ToString(), order.Currency, order.TotalAmount, order.Pricing,
            order.Lines.Select(l => new QrCollectionLine(l.ProductId, l.Name, l.UnitPrice, l.Quantity)).ToArray(), command.SettlementKey);
    }
}
