using System.Text.Json;
using NexaConnect.Services.POS.Domain;
namespace NexaConnect.Services.POS.Infrastructure.Persistence;

internal static class FinancialChangeAttribution
{
    public static bool? Classify(string? json, DateTimeOffset toUtc)
    {
        if (json is null) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("kind").GetString() is not ("shifts" or "cash_sessions" or "cash_movements" or "cash_session_review_states")) return null;
            static DateTimeOffset[]? Times(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null
                : value.EnumerateArray().Select(t => t.GetDateTimeOffset()).ToArray();
            return FinancialChange.AffectsWindow(toUtc, Times(root.GetProperty("before")), Times(root.GetProperty("after")));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or KeyNotFoundException) { return null; }
    }
}
