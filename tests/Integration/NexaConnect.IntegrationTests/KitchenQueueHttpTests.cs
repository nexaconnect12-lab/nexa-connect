extern alias KITCHEN;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexaConnect.Contracts.Platform;
using KITCHEN::NexaConnect.Services.Kitchen.Application;
using KITCHEN::NexaConnect.Services.Kitchen.Application.Tenant;

namespace NexaConnect.IntegrationTests;

public sealed class KitchenQueueHttpTests
{
    [Fact]
    public async Task Replayed_order_snapshot_is_one_queue_ticket_and_completed_ticket_disappears()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", "integration-test-token");
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId, factory.Access.Organization.ToString());
        client.DefaultRequestHeaders.Add(TenantContextHeaders.ApplicationCode, "nexa_connect");
        var command = new CreateKitchenTicket(Guid.NewGuid(), Guid.NewGuid(), factory.Access.Branch,
            [new(Guid.NewGuid(), "Meal", 1, "grill")]);
        using var created = await client.PostAsJsonAsync("/api/kitchen/v1/tickets", command);
        using var replayed = await client.PostAsJsonAsync("/api/kitchen/v1/tickets", command);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode); Assert.Equal(HttpStatusCode.Created, replayed.StatusCode);
        var original = await created.Content.ReadFromJsonAsync<JsonElement>();
        var replay = await replayed.Content.ReadFromJsonAsync<JsonElement>();
        Guid id = original.GetProperty("ticketId").GetGuid();
        Assert.Equal(id, replay.GetProperty("ticketId").GetGuid());
        client.DefaultRequestHeaders.Authorization = new("Bearer", "operator");
        string root = $"/api/kitchen/v1/branches/{factory.Access.Branch}/tickets";
        var queue = await client.GetFromJsonAsync<JsonElement>(root + "?station=grill");
        Assert.Single(queue.GetProperty("items").EnumerateArray());
        int version = 1;
        foreach (string targetStatus in new[] { "InProgress", "Ready", "Completed" })
        {
            using var response = await client.PostAsJsonAsync($"{root}/{id}/transitions", new { targetStatus, expectedConcurrencyVersion = version++ });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var empty = await client.GetFromJsonAsync<JsonElement>(root);
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        var detail = await client.GetFromJsonAsync<JsonElement>($"{root}/{id}");
        Assert.Equal("Completed", detail.GetProperty("status").GetString());
        client.DefaultRequestHeaders.Authorization = new("Bearer", "integration-test-token");
        using var cancellation = await client.PostAsync($"/api/kitchen/v1/tickets/{command.OrderId}/cancel?branchId={factory.Access.Branch}", null);
        Assert.Equal(HttpStatusCode.Conflict, cancellation.StatusCode);
    }

    [Fact]
    public async Task Queue_and_transitions_enforce_http_auth_scope_and_version()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var store = factory.Services.GetRequiredService<IKitchenTicketStore>();
        var ticket = await store.CreateAsync(factory.Access.Organization,
            new(Guid.NewGuid(), Guid.NewGuid(), factory.Access.Branch, [new(Guid.NewGuid(), "Meal", 1, "grill")]), new("fixture", Guid.NewGuid()), default);
        string root = $"/api/kitchen/v1/branches/{factory.Access.Branch}/tickets";
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId, factory.Access.Organization.ToString());
        client.DefaultRequestHeaders.Add(TenantContextHeaders.ApplicationCode, "nexa_connect");
        client.DefaultRequestHeaders.Authorization = new("Bearer", "unauthenticated");
        using var anonymous = await client.GetAsync(root); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "operator");
        using var response = await client.GetAsync(root);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ticket.TicketId, page.GetProperty("items")[0].GetProperty("ticketId").GetGuid());
        Assert.True(page.GetProperty("canTransition").GetBoolean());
        using var started = await client.PostAsJsonAsync($"{root}/{ticket.TicketId}/transitions", new { targetStatus = "InProgress", expectedConcurrencyVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var stale = await client.PostAsJsonAsync($"{root}/{ticket.TicketId}/transitions", new { targetStatus = "Ready", expectedConcurrencyVersion = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        factory.Access.Transition = false;
        using var denied = await client.PostAsJsonAsync($"{root}/{ticket.TicketId}/transitions", new { targetStatus = "Ready", expectedConcurrencyVersion = 2 });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var foreign = await client.GetAsync($"/api/kitchen/v1/branches/{Guid.NewGuid()}/tickets");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        client.DefaultRequestHeaders.Remove(TenantContextHeaders.OrganizationId);
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId, Guid.NewGuid().ToString());
        using var otherTenant = await client.GetAsync(root); Assert.Equal(HttpStatusCode.Forbidden, otherTenant.StatusCode);
    }

    [Fact]
    public async Task Read_only_queue_and_bad_bounds_fail_safely()
    {
        await using var factory = new Factory(); factory.Access.Transition = false;
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", "operator");
        client.DefaultRequestHeaders.Add(TenantContextHeaders.OrganizationId, factory.Access.Organization.ToString());
        client.DefaultRequestHeaders.Add(TenantContextHeaders.ApplicationCode, "nexa_connect");
        string root = $"/api/kitchen/v1/branches/{factory.Access.Branch}/tickets";
        var page = await client.GetFromJsonAsync<JsonElement>(root);
        Assert.False(page.GetProperty("canTransition").GetBoolean());
        foreach (string query in new[] { "?limit=101", "?cursor=invalid", "?station=%01" })
        { using var response = await client.GetAsync(root + query); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        client.DefaultRequestHeaders.Authorization = new("Bearer", "integration-test-token");
        using var workload = await client.GetAsync(root); Assert.Equal(HttpStatusCode.Forbidden, workload.StatusCode);
    }

    private sealed class Factory : WebApplicationFactory<KITCHEN::KitchenProgram>
    {
        public Access Access { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestServiceConfiguration.Configure(builder, "kitchen");
            builder.ConfigureServices(services => { services.RemoveAll<IKitchenTenantAuthorizer>(); services.AddSingleton<IKitchenTenantAuthorizer>(Access); });
        }
    }
    private sealed class Access : IKitchenTenantAuthorizer
    {
        public Guid Organization = Guid.NewGuid(), Branch = Guid.NewGuid(); public bool Transition = true;
        public Task<bool> HasBranchAccessAsync(Guid organizationId, Guid branchId, string permission, string authorizationHeader, CancellationToken ct) =>
            Task.FromResult(organizationId == Organization && branchId == Branch && (permission == ProductPermissions.KitchenTicketRead || Transition));
    }
}
