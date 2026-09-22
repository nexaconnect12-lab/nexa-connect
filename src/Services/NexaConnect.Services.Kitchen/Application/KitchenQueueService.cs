using System.Globalization;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Kitchen.Application.Tenant;
using NexaConnect.Services.Kitchen.Domain;

namespace NexaConnect.Services.Kitchen.Application;

public sealed record KitchenOperatorContext(Guid OrganizationId, Guid BranchId, string ApplicationCode,
    string AuthorizationHeader, string? SubjectId, bool IsOrderWorkload);
public sealed record KitchenQueuePosition(DateTimeOffset QueuedAtUtc, Guid TicketId)
{
    public string Encode() => $"{QueuedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)}_{TicketId:D}";
    public static KitchenQueuePosition? Parse(string? cursor)
    {
        if (cursor is null) return null;
        string[] parts = cursor.Split('_');
        if (cursor.Length > 64 || parts.Length != 2 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) ||
            ticks < DateTimeOffset.MinValue.Ticks || ticks > DateTimeOffset.MaxValue.Ticks ||
            !Guid.TryParseExact(parts[1], "D", out Guid id) || id == Guid.Empty)
            throw new ArgumentException("Invalid queue cursor.");
        return new(new DateTimeOffset(ticks, TimeSpan.Zero), id);
    }
}
public sealed record KitchenQueueQuery(Guid OrganizationId, Guid BranchId, string? Station,
    int Limit, KitchenQueuePosition? After);
public sealed record KitchenQueuePage(IReadOnlyList<KitchenTicket> Items, string? NextCursor, bool CanTransition);

public interface IKitchenQueueStore
{
    // Return at most Limit + 1 active tickets in (queued time, ticket ID) order.
    Task<IReadOnlyList<KitchenTicket>> ListActiveAsync(KitchenQueueQuery query, CancellationToken cancellationToken);
}

public sealed class KitchenQueueService(IKitchenTicketStore tickets, IKitchenQueueStore queue,
    IKitchenTenantAuthorizer authorizer)
{
    public async Task<KitchenQueuePage> ListAsync(KitchenOperatorContext context, string? station,
        int limit, string? cursor, CancellationToken ct)
    {
        await RequireAccess(context, ProductPermissions.KitchenTicketRead, ct);
        if (limit is < 1 or > 100) throw new ArgumentException("Limit must be between 1 and 100.");
        if (station is not null && (string.IsNullOrWhiteSpace(station) || station.Length > 100 || station.Any(char.IsControl)))
            throw new ArgumentException("Station must contain 1 to 100 printable characters.");
        var query = new KitchenQueueQuery(context.OrganizationId, context.BranchId,
            station?.Trim().ToLowerInvariant(), limit, KitchenQueuePosition.Parse(cursor));
        var rows = await queue.ListActiveAsync(query, ct);
        var items = rows.Take(limit).ToArray();
        string? next = rows.Count > limit ? new KitchenQueuePosition(items[^1].QueuedAtUtc, items[^1].TicketId).Encode() : null;
        bool canTransition = await authorizer.HasBranchAccessAsync(context.OrganizationId, context.BranchId,
            ProductPermissions.KitchenTicketTransition, context.AuthorizationHeader, ct);
        return new(items, next, canTransition);
    }

    public async Task<KitchenTicket> GetAsync(KitchenOperatorContext context, Guid ticketId, CancellationToken ct)
    {
        await RequireAccess(context, ProductPermissions.KitchenTicketRead, ct);
        return await OwnedTicket(context, ticketId, ct);
    }

    public async Task<KitchenTicket> TransitionAsync(KitchenOperatorContext context, Guid ticketId,
        TransitionKitchenTicket command, KitchenMutationContext mutation, CancellationToken ct)
    {
        await RequireAccess(context, ProductPermissions.KitchenTicketTransition, ct);
        if (command.ExpectedConcurrencyVersion < 1 || !Enum.IsDefined(command.TargetStatus) ||
            command.ReasonCode is { Length: > 64 } || command.ReasonCode?.Any(char.IsControl) == true)
            throw new ArgumentException("A valid status, positive version and bounded reason are required.");
        var ticket = await OwnedTicket(context, ticketId, ct);
        KitchenTicketLifecycle.RequireTransition(ticket.Status, command.TargetStatus);
        // Persistence repeats version/lifecycle checks under its write lock to fence concurrent changes.
        return await tickets.TransitionAsync(context.OrganizationId, ticketId, command, mutation, ct);
    }

    private async Task<KitchenTicket> OwnedTicket(KitchenOperatorContext context, Guid id, CancellationToken ct)
    {
        var ticket = await tickets.GetAsync(context.OrganizationId, id, ct);
        return ticket is not null && ticket.BranchId == context.BranchId ? ticket : throw new KeyNotFoundException();
    }

    private async Task RequireAccess(KitchenOperatorContext context, string permission, CancellationToken ct)
    {
        if (context.OrganizationId == Guid.Empty || context.BranchId == Guid.Empty || context.IsOrderWorkload ||
            context.ApplicationCode != "nexa_connect" || string.IsNullOrWhiteSpace(context.SubjectId) ||
            string.IsNullOrWhiteSpace(context.AuthorizationHeader) ||
            !await authorizer.HasBranchAccessAsync(context.OrganizationId, context.BranchId, permission, context.AuthorizationHeader, ct))
            throw new UnauthorizedAccessException();
    }
}
