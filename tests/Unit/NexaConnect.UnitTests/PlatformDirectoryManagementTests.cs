using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.ControlPlane;

namespace NexaConnect.UnitTests;

public sealed class PlatformDirectoryManagementTests
{
    [Fact]
    public async Task Organization_creation_normalizes_input_and_records_actor()
    {
        var repository = new FakeRepository();
        var service = new PlatformDirectoryManagementService(repository);

        await service.CreateOrganizationAsync(
            new CreateOrganizationRequest(" acme ", " Acme ", " UTC "),
            "owner-1",
            CancellationToken.None);

        Assert.Equal("acme", repository.OrganizationRequest?.Code);
        Assert.Equal("Acme", repository.OrganizationRequest?.Name);
        Assert.Equal("UTC", repository.OrganizationRequest?.DefaultTimeZone);
        Assert.Equal("owner-1", repository.Actor);
    }

    [Fact]
    public async Task Membership_change_rejects_a_mismatched_route_subject()
    {
        var service = new PlatformDirectoryManagementService(new FakeRepository());

        await Assert.ThrowsAsync<ArgumentException>(() => service.ChangeMembershipAsync(
            Guid.NewGuid(),
            "subject-1",
            new ChangeOrganizationMembershipRequest("subject-2", "active"),
            "owner-1",
            CancellationToken.None));
    }

    [Fact]
    public async Task Product_access_rejects_unknown_status_before_persistence()
    {
        var repository = new FakeRepository();
        var service = new PlatformDirectoryManagementService(repository);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ChangeProductAccessAsync(
            Guid.NewGuid(),
            new ChangeOrganizationProductAccessRequest("nexa_connect", "grant"),
            "owner-1",
            CancellationToken.None));

        Assert.Null(repository.ProductAccessRequest);
    }

    private sealed class FakeRepository : IPlatformDirectoryManagementRepository
    {
        public Task<IReadOnlyCollection<OrganizationSummary>> ListOrganizationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<OrganizationSummary>>([]);

        public CreateOrganizationRequest? OrganizationRequest { get; private set; }
        public ChangeOrganizationProductAccessRequest? ProductAccessRequest { get; private set; }
        public string? Actor { get; private set; }

        public Task<OrganizationSummary> CreateOrganizationAsync(CreateOrganizationRequest request, string actorSubjectId, CancellationToken cancellationToken)
        {
            OrganizationRequest = request;
            Actor = actorSubjectId;
            return Task.FromResult(new OrganizationSummary(Guid.NewGuid(), request.Code, request.Name, "active", request.DefaultTimeZone));
        }

        public Task<bool> UpdateOrganizationAsync(Guid organizationId, UpdateOrganizationRequest request, string actorSubjectId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> ChangeMembershipAsync(Guid organizationId, string subjectId, ChangeOrganizationMembershipRequest request, string actorSubjectId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<ProductRegistration> RegisterProductAsync(RegisterProductRequest request, string actorSubjectId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProductRegistration(request.ApplicationCode, request.Name, "active"));

        public Task<bool> ChangeProductAccessAsync(Guid organizationId, ChangeOrganizationProductAccessRequest request, string actorSubjectId, CancellationToken cancellationToken)
        {
            ProductAccessRequest = request;
            return Task.FromResult(true);
        }
    }
}
