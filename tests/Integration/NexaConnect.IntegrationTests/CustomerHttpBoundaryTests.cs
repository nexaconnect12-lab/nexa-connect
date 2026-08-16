extern alias CUSTOMER;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.Platform;
using CustomerAuthorizer = CUSTOMER::NexaConnect.Services.Customer.Application.Tenant.ICustomerTenantAuthorizer;
using CustomerProgram = CUSTOMER::CustomerProgram;

namespace NexaConnect.IntegrationTests;

public sealed class CustomerHttpBoundaryTests : IClassFixture<CustomerHttpFactory>
{
    private static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly CustomerHttpFactory factory;

    public CustomerHttpBoundaryTests(CustomerHttpFactory factory) => this.factory = factory;

    [Fact]
    public async Task Routes_enforce_authentication_tenant_disclosure_and_success_statuses()
    {
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage unauthenticated = await client.GetAsync(
            $"/api/customer/v1/organizations/{OrganizationId:D}/customers/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var missingContext = new HttpRequestMessage(HttpMethod.Post,
            $"/api/customer/v1/organizations/{OrganizationId:D}/customers")
        {
            Content = JsonContent.Create(new { customerNumber = "C-HTTP-1", displayName = "HTTP Customer" })
        };
        missingContext.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "customer");
        using HttpResponseMessage deniedCreate = await client.SendAsync(missingContext);
        Assert.Equal(HttpStatusCode.Forbidden, deniedCreate.StatusCode);

        using var create = TenantRequest(HttpMethod.Post,
            $"/api/customer/v1/organizations/{OrganizationId:D}/customers");
        create.Content = JsonContent.Create(new { customerNumber = "C-HTTP-2", displayName = "HTTP Customer" });
        using HttpResponseMessage created = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var profile = await created.Content.ReadFromJsonAsync<CustomerResponse>();
        Assert.NotNull(profile);

        using var read = TenantRequest(HttpMethod.Get,
            $"/api/customer/v1/organizations/{OrganizationId:D}/customers/{profile.Id:D}");
        using HttpResponseMessage found = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);

        Guid otherOrganization = Guid.NewGuid();
        using var hidden = TenantRequest(HttpMethod.Get,
            $"/api/customer/v1/organizations/{otherOrganization:D}/customers/{profile.Id:D}", OrganizationId);
        using HttpResponseMessage hiddenResponse = await client.SendAsync(hidden);
        Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);
    }

    private static HttpRequestMessage TenantRequest(HttpMethod method, string path, Guid? contextOrganization = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "customer");
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.OrganizationId,
            (contextOrganization ?? OrganizationId).ToString("D"));
        request.Headers.TryAddWithoutValidation(TenantContextHeaders.ApplicationCode, "nexa_connect");
        return request;
    }

    private sealed record CustomerResponse(Guid Id);
}

public sealed class CustomerHttpFactory : WebApplicationFactory<CustomerProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestServiceConfiguration.Configure(builder, "customer");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<CustomerAuthorizer>();
            services.AddSingleton<CustomerAuthorizer>(new StubCustomerAuthorizer());
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CustomerAuthenticationHandler.Scheme;
                options.DefaultChallengeScheme = CustomerAuthenticationHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, CustomerAuthenticationHandler>(
                CustomerAuthenticationHandler.Scheme, _ => { });
        });
    }

    private sealed class StubCustomerAuthorizer : CustomerAuthorizer
    {
        public Task<bool> HasOrganizationAccessAsync(Guid organizationId, string permission,
            string authorizationHeader, CancellationToken cancellationToken) =>
            Task.FromResult(organizationId == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
    }
}

internal sealed class CustomerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public new const string Scheme = "CustomerIntegrationTest";

    public CustomerAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "customer-http-user")], Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
    }
}
