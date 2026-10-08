using System.Net;
using System.Net.Http.Headers;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Infrastructure.DayClose;

public sealed class HttpFinalizationSources(IHttpClientFactory clients):IFinalizationSources
{
    public async Task<FinalizationFence[]> ExecuteAsync(FinalizationState state,PosUserContext user,bool? cancel,CancellationToken ct)
    {
        var result=new List<FinalizationFence>();var seals=state.Approval.Snapshot.Seals!;
        var w=new EndOfDayWindow(state.Identity.OrganizationId,state.Identity.RestaurantId,state.Identity.BranchId,state.Approval.Snapshot.FromUtc,state.Approval.Snapshot.ToUtc);
        foreach(var owner in new[]{"Order","Payment","POS"})
        {
            var expected=owner=="Order"?seals.Order:owner=="Payment"?seals.Payment:seals.Pos;
            var command=new SourceFenceCommand(state.Command.OperationId,w,state.Approval.ApprovalId,expected.SealId,state.ExpiresAtUtc);
            string path=$"api/{owner.ToLowerInvariant()}/v1/customer/day-cutoffs/fences";
            if(cancel is null)path+=$"/{command.OperationId:D}?organizationId={w.OrganizationId:D}&restaurantId={w.RestaurantId:D}&branchId={w.BranchId:D}&fromUtc={Uri.EscapeDataString(w.FromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(w.ToUtc.ToString("O"))}";
            else if(cancel.Value)path+="/cancel";
            using var request=new HttpRequestMessage(cancel is null?HttpMethod.Get:HttpMethod.Post,path);
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",user.AccessToken);if(cancel is not null)request.Content=JsonContent.Create(command);
            using var response=await clients.CreateClient("DayCutoff"+owner).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new UnauthorizedAccessException();
            if(cancel is null&&response.StatusCode==HttpStatusCode.NotFound)continue;
            response.EnsureSuccessStatusCode();var proof=await BoundedJson.ReadAsync<SourceDayFence>(response,16*1024,ct);
            if(proof.OperationId!=command.OperationId||proof.Window!=w||proof.ApprovalId!=command.ApprovalId||proof.SealId!=expected.SealId||proof.ExpiresAtUtc!=state.ExpiresAtUtc
                ||proof.Active&&proof.Cancelled||proof.AcquiredRevision is null&&!proof.Cancelled)throw new InvalidOperationException("Source fence proof invalid.");
            result.Add(new(owner,proof.SealId,proof.AcquiredRevision?.Epoch??Guid.Empty,proof.AcquiredRevision?.Revision??0,proof.ExpiresAtUtc,proof.Active,proof.Cancelled));
        }
        return result.ToArray();
    }
}
