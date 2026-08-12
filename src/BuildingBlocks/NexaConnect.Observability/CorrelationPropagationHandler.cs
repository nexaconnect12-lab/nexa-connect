using Microsoft.AspNetCore.Http;

namespace NexaConnect.Observability;

public sealed class CorrelationPropagationHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (accessor.HttpContext?.Items[CorrelationLoggingMiddleware.ItemName] is string correlationId
            && !request.Headers.Contains(CorrelationLoggingMiddleware.HeaderName))
            request.Headers.TryAddWithoutValidation(CorrelationLoggingMiddleware.HeaderName, correlationId);
        return base.SendAsync(request, cancellationToken);
    }
}
