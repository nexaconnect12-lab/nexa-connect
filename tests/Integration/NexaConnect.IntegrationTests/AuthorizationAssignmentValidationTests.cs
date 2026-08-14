extern alias AUTH;

using Assignment = AUTH::NexaConnect.Services.Authorization.Application.Assignments;

namespace NexaConnect.IntegrationTests;

public sealed class AuthorizationAssignmentValidationTests
{
    [Fact]
    public async Task Tenant_admin_is_normalized_at_organization_scope()
    {
        var repository = new RecordingRepository();
        var service = new Assignment.AuthorizationAssignmentService(repository);

        await service.AssignAsync(new(" subject ", Guid.NewGuid(), null, null, " TENANT-ADMIN "), " admin ", default);

        Assert.Equal("subject", repository.Command!.SubjectId);
        Assert.Equal("tenant-admin", repository.Command.RoleCode);
        Assert.Null(repository.Command.RestaurantId);
        Assert.Null(repository.Command.BranchId);
        Assert.Equal("admin", repository.AssignedBy);
    }

    [Fact]
    public async Task Store_manager_requires_restaurant_scope_without_branch()
    {
        var service = new Assignment.AuthorizationAssignmentService(new RecordingRepository());

        await Assert.ThrowsAsync<ArgumentException>(() => service.AssignAsync(
            new("subject", Guid.NewGuid(), null, null, "store-manager"), "admin", default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AssignAsync(
            new("subject", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "store-manager"), "admin", default));
    }

    [Fact]
    public async Task Operational_role_requires_branch_hierarchy()
    {
        var service = new Assignment.AuthorizationAssignmentService(new RecordingRepository());

        await Assert.ThrowsAsync<ArgumentException>(() => service.AssignAsync(
            new("subject", Guid.NewGuid(), Guid.NewGuid(), null, "cashier"), "admin", default));
    }

    private sealed class RecordingRepository : Assignment.IAuthorizationAssignmentRepository
    {
        public Assignment.AssignRoleCommand? Command { get; private set; }
        public string? AssignedBy { get; private set; }

        public Task<Assignment.RoleAssignmentResult> AssignAsync(Assignment.AssignRoleCommand command, string assignedBy, CancellationToken cancellationToken)
        {
            Command = command;
            AssignedBy = assignedBy;
            return Task.FromResult(new Assignment.RoleAssignmentResult(Guid.NewGuid()));
        }
    }
}
