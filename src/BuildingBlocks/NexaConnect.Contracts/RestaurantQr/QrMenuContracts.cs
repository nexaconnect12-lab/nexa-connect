using System.Text.Json.Serialization;

namespace NexaConnect.Contracts.RestaurantQr;

// Version 1 transport contracts. No client-supplied scope, currency or money.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrCart(IReadOnlyList<QrCartLine> Lines);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QrCartLine(Guid ProductId, int Quantity);
public sealed record QrMenuItem(Guid ProductId, string Name, decimal UnitPrice, string Currency, bool Available);
public sealed record QrMenu(IReadOnlyList<QrMenuItem> Items);
// Private Catalog-to-Order snapshot. Never returned from guest routes.
public sealed record QrCatalogItem(Guid ProductId, string Name, decimal UnitPrice, string Currency, bool Available, string PreparationStation);
public sealed record QrCatalogMenu(IReadOnlyList<QrCatalogItem> Items);
public sealed record QrQuoteLine(Guid ProductId, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);
public sealed record QrQuote(string Fingerprint, string Currency, DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<QrQuoteLine> Lines, decimal MenuAmount, decimal SubtotalAmount,
    decimal ServiceChargeAmount, decimal TaxAmount, decimal TotalAmount,
    decimal TaxPercent, bool TaxInclusive, decimal ServiceChargePercent);
