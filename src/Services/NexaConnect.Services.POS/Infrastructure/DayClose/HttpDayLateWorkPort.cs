using System.Net;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Infrastructure.DayClose;
public sealed class HttpDayLateWorkPort(IHttpClientFactory clients):IDayLateWorkPort
{
 public async Task<object?> ExecuteAsync(LateWorkScope scope,DayLateWorkRequest input,string action,PosUserContext user,CancellationToken ct)
 {
  var w=scope.Window;var path=$"api/{input.Source.ToLowerInvariant()}/v1/customer/late-work";
  path+=action=="review"?"/reviews":action=="detail"?$"/{input.WorkId:D}":"";
  if(action!="review")path+=$"?organizationId={w.OrganizationId:D}&restaurantId={w.RestaurantId:D}&branchId={w.BranchId:D}&fromUtc={Uri.EscapeDataString(w.FromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(w.ToUtc.ToString("O"))}&settlementId={scope.SettlementId:D}&limit={input.Limit}"+(input.Cursor is null?"":"&cursor="+Uri.EscapeDataString(input.Cursor));
  using var request=new HttpRequestMessage(action=="review"?HttpMethod.Post:HttpMethod.Get,path);request.Headers.Authorization=new("Bearer",user.AccessToken);
  if(action=="review")request.Content=JsonContent.Create(new SourceLateReviewRequest(scope,input.Command!));
  using var response=await clients.CreateClient("DayCutoff"+input.Source).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
  if(response.StatusCode==HttpStatusCode.Forbidden)throw new UnauthorizedAccessException();
  if(response.StatusCode==HttpStatusCode.Conflict)throw new DayCloseConflictException("late_review_conflict");
  if(response.StatusCode==HttpStatusCode.BadRequest)throw new ArgumentException();
  if(response.StatusCode==HttpStatusCode.NotFound)return null;
  response.EnsureSuccessStatusCode();
  if(action=="list"){var result=await BoundedJson.ReadAsync<LateWorkPage>(response,128*1024,ct);if(result.Scope!=scope||result.Items.Length>50)throw new InvalidOperationException();return result;}
  if(action=="detail"){var result=await BoundedJson.ReadAsync<LateWorkDetail>(response,128*1024,ct);Validate(result,scope,input.WorkId);return result;}
  var review=await BoundedJson.ReadAsync<LateReviewResult>(response,128*1024,ct);Validate(review.Detail,scope,input.WorkId);
  if(review.OperationId!=input.Command!.OperationId)throw new InvalidOperationException();return review;
 }
 private static void Validate(LateWorkDetail result,LateWorkScope scope,Guid id)
 {if(result.Scope!=scope||result.Item.WorkId!=id||result.History.Length>20||result.SettlementLinks.Length>20)throw new InvalidOperationException();}
}
