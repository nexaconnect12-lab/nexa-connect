using System.Text.Json;
using NexaConnect.Services.Order.Domain;
namespace NexaConnect.Services.Order.Infrastructure.Persistence;

internal static class FinancialChangeAttribution
{
    public static bool? Classify(string? json, DateTimeOffset toUtc)
    {
        if (json is null) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("kind").GetString() is not ("orders" or "order_manual_tender_settlements" or "order_sale_publications")) return null;
            static DateTimeOffset[]? Times(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null
                : value.EnumerateArray().Select(t => t.GetDateTimeOffset()).ToArray();
            return FinancialChange.AffectsWindow(toUtc, Times(root.GetProperty("before")), Times(root.GetProperty("after")));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or KeyNotFoundException) { return null; }
    }
}
