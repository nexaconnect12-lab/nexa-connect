using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Services.Order.Application.Tenant;
using NexaConnect.Services.Order.Domain.Qr;

namespace NexaConnect.Services.Order.Application.Orders;

public sealed record RetainedGuestQuote(GuestRequestScope Scope, QrQuote Quote);
public sealed record QrAcceptedOrder(Domain.OrderAggregate Order,NexaConnect.Contracts.IntegrationEvents.OrderSubmittedV1 Submitted);
public sealed record RetainedGuestRequest(GuestRequest Request, QrQuote Quote,QrQuote? ReviewQuote=null,Guid? OrderId=null,string? OrderStatus=null,string? CollectionStatus=null)
{
    public QrRequestView View() => new(Request.Id, Request.Scope.TableCode, Request.Status, Request.Version,
        Request.SubmittedAtUtc, Request.DeadlineUtc, Request.Reason, Quote,OrderId,OrderStatus,ReviewQuote,CollectionStatus:CollectionStatus);
}
public interface IQrRequestRepository
{
    Task<RetainedGuestRequest?> GetAsync(Guid organization,Guid branch,Guid id,CancellationToken ct)=>throw new NotSupportedException();
    Task<RetainedGuestRequest> ChangeAsync(RetainedGuestRequest expected,string action,QrQuote? review,QrAcceptedOrder? handoff,string actor,Guid? decision,Guid correlation,CancellationToken ct)=>throw new NotSupportedException();
    Task SaveQuoteAsync(RetainedGuestQuote quote, CancellationToken ct);
    Task<RetainedGuestRequest> SubmitAsync(GuestRequestScope scope, QrSubmission submission, DateTimeOffset visitorExpiry, Guid correlation, CancellationToken ct);
    Task<QrRequestList> ListAsync(Guid organization, Guid branch, Guid? visitor, string state, CancellationToken ct);
    Task<RetainedGuestRequest?> RejectAsync(Guid organization, Guid branch, Guid id, QrRejection rejection, string actor, Guid decision, Guid correlation, CancellationToken ct);
}
public sealed class QrRequestService(QrMenuService menu, IQrRequestRepository repository, IOrderTenantAuthorizer authorization,
    IOrderReceiptRepository? receipts = null)
{
    public async Task<QrQuote> QuoteAsync(string capability, QrCart cart, CancellationToken ct)
    {
        var context = await menu.ContextAsync(capability, ct);
        var quote = (await menu.QuoteAsync(capability, cart, ct)) with { QuoteId = Guid.NewGuid() };
        if (await menu.ContextAsync(capability, ct) != context) throw new UnauthorizedAccessException();
        await repository.SaveQuoteAsync(new(Scope(context), quote), ct);
        return quote;
    }
    public async Task<QrRequestView> SubmitAsync(string capability, QrSubmission input, Guid correlation, CancellationToken ct)
    {
        if (input.QuoteId == Guid.Empty || input.Fingerprint?.Length != 64 || !input.Fingerprint.All(char.IsAsciiHexDigit)) throw new ArgumentException("Invalid quote identity.");
        var context = await menu.ContextAsync(capability, ct);
        return (await repository.SubmitAsync(Scope(context), input, context.ExpiresAtUtc, correlation, ct)).View();
    }
    public async Task<QrRequestList> MineAsync(string capability, CancellationToken ct)
    {
        var context = await menu.ContextAsync(capability, ct);
        var result = await repository.ListAsync(context.OrganizationId, context.BranchId, context.VisitorId, "all", ct);
        if (receipts is not null)
        {
            var items = new List<QrRequestView>();
            foreach (var item in result.Items)
            {
                var receipt = item.OrderId is { } id && item.OrderStatus == "paid"
                    ? await receipts.GetReceiptAsync(context.OrganizationId, context.BranchId, id, ct) : null;
                items.Add(receipt is null ? item : item with { Receipt = new(receipt.ReceiptNumber,receipt.PaidAtUtc,
                    receipt.Currency,receipt.Tender,receipt.Lines.Select(l=>new QrGuestReceiptLine(l.Name,l.UnitPrice,l.Quantity,l.Total)).ToArray(),
                    receipt.SubtotalAmount,receipt.ServiceChargeAmount,receipt.TaxAmount,receipt.TotalAmount) });
            }
            result = result with { Items = items };
        }
        if (await menu.ContextAsync(capability, ct) != context) throw new UnauthorizedAccessException();
        return result;
    }
    public async Task<QrRequestList> QueueAsync(Guid organization, Guid branch, string state, string bearer, CancellationToken ct)
    {
        await Authorize(organization, branch, ProductPermissions.OrderRead, bearer, ct);
        if (state is not ("pending" or "price_review" or "accepted" or "rejected" or "expired")) throw new ArgumentException("Invalid queue state.");
        return await repository.ListAsync(organization, branch, null, state, ct);
    }
    public async Task<QrRequestView?> RejectAsync(Guid organization, Guid branch, Guid id, QrRejection input, string actor, string bearer, Guid correlation, CancellationToken ct)
    {
        Guid decision = await Authorize(organization, branch, ProductPermissions.OrderPlace, bearer, ct);
        if (id == Guid.Empty || actor.Length is < 1 or > 200 || input.ExpectedVersion < 1) throw new ArgumentException("Invalid rejection.");
        return (await repository.RejectAsync(organization, branch, id, input, actor, decision, correlation, ct))?.View();
    }
    private async Task<Guid> Authorize(Guid organization, Guid branch, string permission, string bearer, CancellationToken ct)
    {
        var decision = await authorization.GetBranchDecisionAsync(organization, branch, permission, bearer, ct);
        return decision is null || decision == Guid.Empty ? throw new UnauthorizedAccessException() : decision.Value;
    }
    private static GuestRequestScope Scope(QrGuestContext c) => new(c.OrganizationId, c.RestaurantId, c.BranchId, c.VisitorId, c.EntryId, c.TableCode);
}
