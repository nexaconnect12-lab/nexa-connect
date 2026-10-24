using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Services.Order.Application.Tenant;
using NexaConnect.Services.Order.Application.Workflow;
using NexaConnect.Services.Order.Domain.Qr;
namespace NexaConnect.Services.Order.Application.Orders;

public interface IQrAcceptancePort
{
    Task<QrAcceptanceContext?> ContextAsync(GuestRequestScope scope,CancellationToken ct);
}
public sealed class QrAcceptanceService(IQrRequestRepository repository,IQrAcceptancePort contexts,IQrMenuPort catalog,
    IBranchPricingPort policies,IOrderTenantAuthorizer authorization,QrMenuService guest,TimeProvider time)
{
    public async Task<QrRequestView?> AcceptAsync(Guid organization,Guid branch,Guid id,QrAcceptance input,string actor,string bearer,Guid correlation,CancellationToken ct)
    {
        Guid? decision=await authorization.GetBranchDecisionAsync(organization,branch,ProductPermissions.OrderPlace,bearer,ct);
        if(decision is null||decision==Guid.Empty||actor.Length is <1 or >200)throw new UnauthorizedAccessException();
        var value=await repository.GetAsync(organization,branch,id,ct);if(value is null)return null;
        // An already admitted command is replayed, never recreated or repriced.
        if(value.Request.Status=="accepted")return value.View();
        if(value.Request.Status is not ("pending" or "price_review")||value.Request.Version!=input.ExpectedVersion||value.Request.DeadlineUtc<=time.GetUtcNow())throw new GuestRequestConflictException();
        var scope=value.Request.Scope;var context=await contexts.ContextAsync(scope,ct);
        if(!Valid(context,scope))throw new GuestRequestConflictException();
        var items=await catalog.MenuAsync(scope.OrganizationId,scope.BranchId,ct);
        var policy=await policies.GetAsync(scope.OrganizationId,scope.RestaurantId,scope.BranchId,ct);
        var command=new PlaceOrderCommand(scope.OrganizationId,scope.BranchId,value.Quote.Lines.Select(l=>new PlaceOrderLine(l.ProductId,l.Quantity)).ToArray(),value.Quote.Currency,"cash",scope.RestaurantId);
        OrderQuote current;
        try{current=OrderPricingService.Resolve(command,policy,items.ToDictionary(i=>i.ProductId));}
        catch(ArgumentException){throw new GuestRequestConflictException();}
        if(!Valid(await contexts.ContextAsync(scope,ct),scope))throw new GuestRequestConflictException();
        if(current.Fingerprint!=value.Quote.Fingerprint||value.Request.Status=="price_review")
        {
            var p=current.Pricing;var expires=time.GetUtcNow().AddMinutes(2);if(expires>value.Request.DeadlineUtc)expires=value.Request.DeadlineUtc;
            var review=new QrQuote(current.Fingerprint,command.Currency,expires,current.Lines.Select(l=>new QrQuoteLine(l.ProductId,l.Name,l.UnitPrice,l.Quantity,l.Total)).ToArray(),p.MenuAmount,p.SubtotalAmount,p.ServiceChargeAmount,p.TaxAmount,p.TotalAmount,p.TaxPercent,p.TaxInclusive,p.ServiceChargePercent,id);
            return (await repository.ChangeAsync(value,"price_review",review,null,actor,decision,correlation,ct)).View();
        }
        var manual=OrderPricingService.Resolve(command with{PaymentMethod="cash_manual"},policy,items.ToDictionary(i=>i.ProductId));
        var order=Domain.OrderAggregate.Create(id,scope.OrganizationId,scope.BranchId,manual.Lines.ToArray(),command.Currency,scope.RestaurantId,"qr","dine_in",idempotencyKey:"qr:"+id.ToString("N"),workflowPaymentMethod:"cash_manual",workflowCorrelationId:correlation,pricing:manual.Pricing,pricingFingerprint:manual.Fingerprint);order.Submit();
        var submitted=new NexaConnect.Contracts.IntegrationEvents.OrderSubmittedV1(Guid.NewGuid(),correlation,time.GetUtcNow(),id,scope.OrganizationId,scope.BranchId,order.Lines.Select(l=>new NexaConnect.Contracts.IntegrationEvents.OrderLineSnapshot(l.ProductId,l.Name,l.UnitPrice,l.Quantity,l.PreparationStation)).ToArray(),order.TotalAmount,order.Currency);
        return (await repository.ChangeAsync(value,"accepted",null,new(order,submitted),actor,decision,correlation,ct)).View();
    }
    public async Task<QrRequestView> ConfirmAsync(string capability,Guid id,QrPriceConfirmation input,Guid correlation,CancellationToken ct)
    {
        var context=await guest.ContextAsync(capability,ct);
        var value=await repository.GetAsync(context.OrganizationId,context.BranchId,id,ct);
        if(value is null||value.ReviewQuote is null||value.Request.Scope.VisitorId!=context.VisitorId||value.Request.Scope.EntryId!=context.EntryId
            ||value.Request.Version!=input.ExpectedVersion||value.ReviewQuote.Fingerprint!=input.Fingerprint
            ||value.ReviewQuote.ExpiresAtUtc<=time.GetUtcNow())throw new GuestRequestConflictException();
        return (await repository.ChangeAsync(value,"price_confirmed",null,null,"qr-guest",null,correlation,ct)).View();
    }
    private bool Valid(QrAcceptanceContext? value,GuestRequestScope s)=>value is{DineInEnabled:true}&&value.Context is var c
        &&c.OrganizationId==s.OrganizationId&&c.RestaurantId==s.RestaurantId&&c.BranchId==s.BranchId&&c.EntryId==s.EntryId
        &&c.VisitorId==s.VisitorId&&c.TableCode==s.TableCode&&c.Currency=="THB"&&c.ExpiresAtUtc>time.GetUtcNow();
}
