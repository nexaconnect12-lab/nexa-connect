using System.Net;
using System.Net.Http.Headers;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Infrastructure.Http;
using NexaConnect.Services.POS.Application.DayClose;
using NexaConnect.Services.POS.Domain.DayClose;
using NexaConnect.Services.POS.Infrastructure.Identity;

namespace NexaConnect.Services.POS.Infrastructure.DayClose;

internal static class SettlementBarrierCommand
{
    public static SourceBarrierCommand Create(SettlementState state, string owner)
    {
        var preparation = state.Preparation;
        var seals = preparation.Approval.Snapshot.Seals!;
        var seal = owner == "Order" ? seals.Order : owner == "Payment" ? seals.Payment : seals.Pos;
        var day = state.Identity;
        var window = new EndOfDayWindow(day.OrganizationId, day.RestaurantId, day.BranchId,
            preparation.Approval.Snapshot.FromUtc, preparation.Approval.Snapshot.ToUtc);
        return new(state.Id, state.Command.OperationId, new(preparation.Command.OperationId, window,
            preparation.Approval.ApprovalId, seal.SealId, preparation.ExpiresAtUtc));
    }
}

public sealed class HttpSettlementSources(IHttpClientFactory clients, PosWorkloadTokenProvider tokens) : ISettlementSources
{
    public async Task<SettlementSource[]> ReadAsync(SettlementState state,CancellationToken ct)
    {
        var bearer=await tokens.GetAsync(ct);var result=new List<SettlementSource>();
        foreach(var owner in new[]{"Order","Payment","POS"})
        {
            var command=SettlementBarrierCommand.Create(state,owner);var w=command.Fence.Window;
            var path=$"api/{owner.ToLowerInvariant()}/v1/internal/day-settlement-barriers/{state.Id:D}?organizationId={w.OrganizationId:D}&restaurantId={w.RestaurantId:D}&branchId={w.BranchId:D}&fromUtc={Uri.EscapeDataString(w.FromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(w.ToUtc.ToString("O"))}";
            using var request=new HttpRequestMessage(HttpMethod.Get,path);request.Headers.Authorization=new("Bearer",bearer);
            using var response=await clients.CreateClient("DayCutoff"+owner).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            if(response.StatusCode==HttpStatusCode.NotFound)return [];
            response.EnsureSuccessStatusCode();var proof=await BoundedJson.ReadAsync<SourceBarrierProof>(response,16*1024,ct);
            if(proof.Command!=command||proof.Phase is not("armed" or "committed" or "aborted")||proof.LateWorkCount<0
                ||proof.Phase=="armed"&&proof.DecisionId is not null||proof.Phase!="armed"&&proof.DecisionId!=state.DecisionId)
                throw new InvalidOperationException("Source barrier observation invalid.");
            result.Add(new(owner,proof));
        }
        return result.ToArray();
    }
    public async Task<SettlementSource[]> ExecuteAsync(SettlementState state, string phase, CancellationToken ct)
    {
        var bearer = await tokens.GetAsync(ct);
        var result = new List<SettlementSource>();
        foreach (var owner in new[] { "Order", "Payment", "POS" })
        {
            var command = SettlementBarrierCommand.Create(state, owner);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"api/{owner.ToLowerInvariant()}/v1/internal/day-settlement-barriers");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            request.Content = JsonContent.Create(new SourceBarrierRequest(command, phase, phase == "armed" ? null : state.DecisionId));
            using var response = await clients.CreateClient("DayCutoff" + owner).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.Conflict) throw new DayCloseConflictException("source_barrier_conflict");
            response.EnsureSuccessStatusCode();
            var proof = await BoundedJson.ReadAsync<SourceBarrierProof>(response, 16 * 1024, ct);
            if (proof.Command != command || phase == "armed" && proof.Phase is not ("armed" or "committed")
                || phase != "armed" && (proof.Phase != phase || proof.DecisionId != state.DecisionId))
                throw new InvalidOperationException("Source barrier proof invalid.");
            result.Add(new(owner, proof));
        }
        return result.ToArray();
    }
}
