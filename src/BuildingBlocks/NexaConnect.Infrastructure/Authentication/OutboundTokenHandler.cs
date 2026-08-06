using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace NexaConnect.Infrastructure.Authentication;

public sealed class OutboundTokenHandler(IConfiguration configuration) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = configuration["Authentication:OutboundToken"];
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, cancellationToken);
    }
}
