using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Kitchen.Application;
using NexaConnect.Services.Kitchen.Application.Tenant;
using NexaConnect.Services.Kitchen.Domain;
using NexaConnect.Services.Kitchen.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class KitchenQueueTests
{
    private readonly InMemoryKitchenTicketStore store = new();
    private readonly Access access = new();
    private readonly Guid organization = Guid.NewGuid(), branch = Guid.NewGuid();
    private readonly KitchenMutationContext mutation = new("operator", Guid.NewGuid());
    private KitchenQueueService Service => new(store, store, access);
    private KitchenOperatorContext Context => new(organization, branch, "nexa_connect", "Bearer test", "operator", false);
    private Task<KitchenTicket> Create(string station = "grill", Guid? org = null, Guid? location = null, Guid? order = null) =>
        store.CreateAsync(org ?? organization, new(Guid.NewGuid(), order ?? Guid.NewGuid(), location ?? branch,
            [new(Guid.NewGuid(), "Meal", 1, station)]), mutation, default);

    [Fact]
    public async Task Queue_is_scoped_active_station_filtered_and_keyset_paginated()
    {
        var first = await Create(); var second = await Create();
        await Create("bar"); await Create(org: Guid.NewGuid()); await Create(location: Guid.NewGuid());
        var cancelled = await Create(); await store.CancelAsync(organization, branch, cancelled.OrderId, mutation, default);
        var page = await Service.ListAsync(Context, " GRILL ", 1, null, default);
        Assert.Equal(first.TicketId, Assert.Single(page.Items).TicketId); Assert.NotNull(page.NextCursor);
        await store.CancelAsync(organization, branch, first.OrderId, mutation, default);
        var next = await Service.ListAsync(Context, "grill", 1, page.NextCursor, default);
        Assert.Equal(second.TicketId, Assert.Single(next.Items).TicketId); Assert.Null(next.NextCursor);
        Assert.True(next.CanTransition);
    }

    [Fact]
    public async Task Read_permission_does_not_grant_transitions_and_each_action_rechecks_access()
    {
        var ticket = await Create(); access.Transition = false;
        Assert.False((await Service.ListAsync(Context, null, 50, null, default)).CanTransition);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.TransitionAsync(Context, ticket.TicketId,
            new(KitchenTicketStatus.InProgress, 1), mutation, default));
        access.Read = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.GetAsync(Context, ticket.TicketId, default));
    }

    [Fact]
    public async Task Foreign_branch_product_subject_and_workload_cannot_operate()
    {
        var foreign = await Create(location: Guid.NewGuid());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service.TransitionAsync(Context, foreign.TicketId,
            new(KitchenTicketStatus.InProgress, 1), mutation, default));
        foreach (var context in new[] { Context with { ApplicationCode = "other" }, Context with { SubjectId = null },
                     Context with { IsOrderWorkload = true }, Context with { OrganizationId = Guid.Empty } })
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.ListAsync(context, null, 50, null, default));
    }

    [Fact]
    public async Task Stale_transition_and_payment_cancellation_race_cannot_overwrite_state()
    {
        var ticket = await Create();
        await Service.TransitionAsync(Context, ticket.TicketId, new(KitchenTicketStatus.InProgress, 1), mutation, default);
        await Assert.ThrowsAsync<KitchenConflictException>(() => Service.TransitionAsync(Context, ticket.TicketId,
            new(KitchenTicketStatus.Ready, 1), mutation, default));
        await store.CancelAsync(organization, branch, ticket.OrderId, mutation, default);
        await Assert.ThrowsAsync<KitchenConflictException>(() => Service.TransitionAsync(Context, ticket.TicketId,
            new(KitchenTicketStatus.Ready, 2), mutation, default));
        Assert.Equal(KitchenTicketStatus.Cancelled, (await Service.GetAsync(Context, ticket.TicketId, default)).Status);
    }

    [Fact]
    public async Task Completed_station_prevents_partial_order_cancellation()
    {
        var first = await Create(); var second = await Create("bar", order: first.OrderId);
        await store.TransitionAsync(organization, second.TicketId, new(KitchenTicketStatus.InProgress, 1), mutation, default);
        await store.TransitionAsync(organization, second.TicketId, new(KitchenTicketStatus.Ready, 2), mutation, default);
        await store.TransitionAsync(organization, second.TicketId, new(KitchenTicketStatus.Completed, 3), mutation, default);
        await Assert.ThrowsAsync<KitchenConflictException>(() => store.CancelAsync(organization, branch, first.OrderId, mutation, default));
        Assert.Equal(KitchenTicketStatus.Queued, (await store.GetAsync(organization, first.TicketId, default))!.Status);
        Assert.Equal(KitchenTicketStatus.Completed, (await store.GetAsync(organization, second.TicketId, default))!.Status);
    }

    [Theory]
    [InlineData(0, null)] [InlineData(101, null)] [InlineData(50, "bad")]
    [InlineData(50, "999999999999999999_00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_queue_bounds_are_rejected(int limit, string? cursor) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Service.ListAsync(Context, null, limit, cursor, default));

    private sealed class Access : IKitchenTenantAuthorizer
    {
        public bool Read = true, Transition = true;
        public Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string permission, string authorizationHeader, CancellationToken ct) =>
            Task.FromResult(permission == ProductPermissions.KitchenTicketRead ? Read : Transition);
    }
}
