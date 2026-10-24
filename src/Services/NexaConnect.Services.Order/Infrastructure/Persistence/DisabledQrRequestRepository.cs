using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Domain.Qr;
namespace NexaConnect.Services.Order.Infrastructure.Persistence;
// No in-memory durability fallback. Disabled hosts can still resolve QR2 controllers.
public sealed class DisabledQrRequestRepository : IQrRequestRepository
{
    private static Exception Disabled()=>new HttpRequestException("Durable QR requests require PostgreSQL.");
    public Task SaveQuoteAsync(RetainedGuestQuote quote,CancellationToken ct)=>throw Disabled();
    public Task<RetainedGuestRequest> SubmitAsync(GuestRequestScope scope,QrSubmission input,DateTimeOffset expiry,Guid correlation,CancellationToken ct)=>throw Disabled();
    public Task<QrRequestList> ListAsync(Guid org,Guid branch,Guid? visitor,string state,CancellationToken ct)=>throw Disabled();
    public Task<RetainedGuestRequest?> RejectAsync(Guid org,Guid branch,Guid id,QrRejection input,string actor,Guid decision,Guid correlation,CancellationToken ct)=>throw Disabled();
}
