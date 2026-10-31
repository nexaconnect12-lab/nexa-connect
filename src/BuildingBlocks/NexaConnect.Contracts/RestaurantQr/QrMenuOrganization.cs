namespace NexaConnect.Contracts.RestaurantQr;
public sealed record QrMenuCategory(Guid Id,string Name);
public sealed record QrMenuPlacement(Guid ProductId,Guid? CategoryId);
public sealed record QrMenuOrganization(IReadOnlyList<QrMenuCategory> Categories,IReadOnlyList<QrMenuPlacement> Items);
