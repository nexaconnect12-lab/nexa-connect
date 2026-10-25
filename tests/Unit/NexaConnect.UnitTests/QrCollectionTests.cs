using NexaConnect.Services.Order.Domain.Qr;
using NexaConnect.Services.Order.Application.ManualTenders;
using NexaConnect.Services.Order.Infrastructure.Clients;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
namespace NexaConnect.UnitTests;
public sealed class QrCollectionTests
{
    [Fact] public async Task Legacy_admission_without_exact_scope_proof_cannot_admit_Qr_collection()
    {
        using var client=new HttpClient(new LegacyAdmission()){BaseAddress=new Uri("https://pos.example.test/")};
        var adapter=new HttpManualTenderAdmission(client,NullLogger<HttpManualTenderAdmission>.Instance);
        var request=new ManualTenderAdmissionRequest(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"cashier","promptpay_manual","THB");
        await adapter.AdmitAsync(request,default); // Existing non-QR callers remain compatible.
        await Assert.ThrowsAsync<HttpRequestException>(()=>adapter.AdmitAsync(request with{ExpectedShiftId=Guid.NewGuid(),ExpectedStoreId=Guid.NewGuid()},default));
    }
    private sealed class LegacyAdmission:HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{admittedAtUtc=DateTimeOffset.UtcNow})});}
    [Theory]
    [InlineData("actor")][InlineData("terminal")][InlineData("operation")][InlineData("organization")][InlineData("branch")][InlineData("order")]
    public void Original_collection_custody_denies_transferred_authority(string changed)
    {
        var b=new QrCollectionBinding(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"cashier");
        b.RequireOwner(b.OrderId,b.OrganizationId,b.BranchId,b.TerminalId,b.SettlementKey,b.Actor);
        Assert.Throws<InvalidOperationException>(()=>b.RequireOwner(changed=="order"?Guid.NewGuid():b.OrderId,
            changed=="organization"?Guid.NewGuid():b.OrganizationId,changed=="branch"?Guid.NewGuid():b.BranchId,
            changed=="terminal"?Guid.NewGuid():b.TerminalId,changed=="operation"?Guid.NewGuid():b.SettlementKey,changed=="actor"?"other":b.Actor));
    }
}
