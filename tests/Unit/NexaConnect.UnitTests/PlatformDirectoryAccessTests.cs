using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.PlatformDirectory.Application.Access;
using NexaConnect.Services.PlatformDirectory.Controllers;

namespace NexaConnect.UnitTests;

public sealed class PlatformDirectoryAccessTests
{
    [Fact]
    public async Task Current_access_returns_only_the_authenticated_subject_context()
    {
        Guid organizationId = Guid.NewGuid();
        var reader = new FakeOrganizationAccessReader(
            [new OrganizationApplicationAccess(organizationId, "acme", "Acme", "nexa_connect")]);
        var controller = new OrganizationAccessController(reader)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", "subject-1")], "test"))
                }
            }
        };

        var result = await controller.GetCurrentAccessAsync(CancellationToken.None);

        var response = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
        var payload = Assert.IsType<CurrentPlatformAccessResponse>(response.Value);
        Assert.Equal("subject-1", payload.SubjectId);
        Assert.Single(payload.Organizations);
        Assert.Equal(organizationId, payload.Organizations[0].OrganizationId);
        Assert.Equal("subject-1", reader.LastSubjectId);
    }

    [Fact]
    public async Task Current_access_for_missing_subject_is_forbidden()
    {
        var controller = new OrganizationAccessController(new FakeOrganizationAccessReader([]))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.GetCurrentAccessAsync(CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Mvc.ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Current_access_accepts_the_standard_name_identifier_claim()
    {
        var reader = new FakeOrganizationAccessReader([]);
        var controller = new OrganizationAccessController(reader)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "mapped-subject")], "test"))
                }
            }
        };

        var result = await controller.GetCurrentAccessAsync(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("mapped-subject", reader.LastSubjectId);
    }

    private sealed class FakeOrganizationAccessReader(
        IReadOnlyList<OrganizationApplicationAccess> organizations) : IOrganizationAccessReader
    {
        public string? LastSubjectId { get; private set; }

        public Task<bool> HasNexaConnectAccessAsync(Guid organizationId, string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<OrganizationApplicationAccess>> GetCurrentAccessAsync(
            string subjectId,
            CancellationToken cancellationToken)
        {
            LastSubjectId = subjectId;
            return Task.FromResult(organizations);
        }
    }
}
