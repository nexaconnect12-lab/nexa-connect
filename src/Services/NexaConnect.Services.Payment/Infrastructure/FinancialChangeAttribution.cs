using System.Text.Json;
using NexaConnect.Contracts.Reporting;
using NexaConnect.Services.Payment.Domain;
namespace NexaConnect.Services.Payment.Infrastructure;

internal static class FinancialChangeAttribution
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static SourceSealImpact Classify(string? json,DateTimeOffset from,DateTimeOffset to)
    {
        if(json is null)return new(null,"unknown");
        try
        {
            using var document=JsonDocument.Parse(json);var root=document.RootElement;
            if(root.GetProperty("version").GetInt32()!=2)return new(null,"legacy_attribution");
            var kind=root.GetProperty("kind").GetString();
            if(kind is not ("payment_intents" or "refunds" or "refund_financial_publications"))return new(null,"unknown");
            var recordId=root.GetProperty("recordId").GetGuid();
            if(recordId==Guid.Empty)return new(null,"unknown");
            var before=root.GetProperty("before").Deserialize<FinancialRecord>(Json);
            var after=root.GetProperty("after").Deserialize<FinancialRecord>(Json);
            var impact=FinancialChange.Classify(from,to,before,after,root.GetProperty("ownershipUncertain").GetBoolean(),kind);
            var parent=after??before;
            bool origin=impact.Reason is "sales_date" or "unresolved_order" or "unresolved_payment" or "unresolved_refund" or "unresolved_shift" or "unresolved_drawer";
            return new(impact.AffectsWindow,impact.Reason,kind,recordId,parent?.Id,before?.Status,after?.Status,
                before?.Version,after?.Version,origin?before?.CreatedAtUtc:before?.FinancialAtUtc??before?.CreatedAtUtc,origin?after?.CreatedAtUtc:after?.FinancialAtUtc??after?.CreatedAtUtc);
        }
        catch(Exception e)when(e is JsonException or InvalidOperationException or FormatException or KeyNotFoundException){return new(null,"unknown");}
    }
}
