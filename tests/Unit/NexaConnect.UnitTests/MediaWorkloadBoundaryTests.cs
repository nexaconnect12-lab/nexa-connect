using System.Security.Claims;
using NexaConnect.Infrastructure.Authorization;

namespace NexaConnect.UnitTests;

public sealed class MediaWorkloadBoundaryTests
{
    [Fact]
    public void Media_client_is_not_a_global_trusted_workload()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", "nexaconnect-media-service")], "test"));
        Assert.False(ServiceWorkloadPrincipal.IsTrusted(principal));
    }
}
