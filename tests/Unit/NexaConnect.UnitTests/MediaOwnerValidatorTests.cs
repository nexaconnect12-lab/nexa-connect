using System.Net;
using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Media.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace NexaConnect.UnitTests;

public sealed class MediaOwnerValidatorTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Catalog_lookup_is_tenant_leading_and_uses_workload_token(HttpStatusCode status, bool expected)
    {
        using var handler = new CaptureHandler(status);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") };
        var validator = new HttpMediaOwnerValidator(client, new Tokens(), NullLogger<HttpMediaOwnerValidator>.Instance);
        Guid organizationId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();

        bool result = await validator.ExistsAsync(organizationId, "catalog", "product", productId, default);

        Assert.Equal(expected, result);
        Assert.Equal($"api/catalog/v1/internal/organizations/{organizationId:D}/products/{productId:D}/exists", handler.Path);
        Assert.Equal("Bearer media-token", handler.Authorization);
    }

    [Fact]
    public async Task Catalog_dependency_failure_fails_closed()
    {
        using var client = new HttpClient(new CaptureHandler(HttpStatusCode.ServiceUnavailable)) { BaseAddress = new Uri("https://catalog.test/") };
        var validator = new HttpMediaOwnerValidator(client, new Tokens(), NullLogger<HttpMediaOwnerValidator>.Instance);
        await Assert.ThrowsAsync<HttpRequestException>(() => validator.ExistsAsync(Guid.NewGuid(), "catalog", "product", Guid.NewGuid(), default));
    }

    private sealed class Tokens : IServiceWorkloadTokenProvider
    {
        public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult("media-token");
    }

    private sealed class CaptureHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.PathAndQuery.TrimStart('/');
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
