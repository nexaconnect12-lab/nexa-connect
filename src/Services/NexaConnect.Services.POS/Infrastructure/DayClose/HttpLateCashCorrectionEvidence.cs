using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain;
using NexaConnect.Services.POS.Infrastructure.Identity;
namespace NexaConnect.Services.POS.Infrastructure.DayClose;
public sealed class HttpLateCashCorrectionEvidence(IHttpClientFactory clients,PosWorkloadTokenProvider tokens):ILateCashCorrectionEvidence
{
 public async Task<CorrectionCalendar> CalendarAsync(Guid branch,CancellationToken ct)
 {
  using var request=new HttpRequestMessage(HttpMethod.Get,$"api/restaurant/v1/branches/{branch:D}/business-calendar");request.Headers.Authorization=new("Bearer",await tokens.GetAsync(ct));
  using var response=await clients.CreateClient("CorrectionRestaurant").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();return await BoundedJson.ReadAsync<CorrectionCalendar>(response,16*1024,ct);
 }
 public async Task<CashTenderProof?> TenderAsync(LateCashCase work,PosUserContext user,CancellationToken ct)
 {
  var t=work.Tender;using var request=new HttpRequestMessage(HttpMethod.Get,$"api/order/v1/customer/manual-tender-evidence/{t.EventId:D}?organizationId={t.OrganizationId:D}&restaurantId={t.RestaurantId:D}&branchId={t.BranchId:D}");request.Headers.Authorization=new("Bearer",user.AccessToken);
  using var response=await clients.CreateClient("DayCutoffOrder").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
  if(response.StatusCode==HttpStatusCode.Forbidden)throw new UnauthorizedAccessException();if(response.StatusCode==HttpStatusCode.NotFound)return null;
  response.EnsureSuccessStatusCode();return Proof(await BoundedJson.ReadAsync<OrderManualTenderSettledV1>(response,16*1024,ct));
 }
 public static CashTenderProof Proof(OrderManualTenderSettledV1 value)
 {
  var hash=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,new JsonSerializerOptions(JsonSerializerDefaults.Web))));
  return new(value.EventId,value.SettlementId,value.OrderId,value.TerminalId,value.OrganizationId,value.RestaurantId,value.BranchId,value.Amount,value.Currency,value.Method,value.OccurredAtUtc,hash);
 }
}
