using Microsoft.AspNetCore.Http;

namespace NexaConnect.Observability;

public sealed class CorrelationPropagationHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? correlationId = accessor.HttpContext?.Items[CorrelationLoggingMiddleware.ItemName] as string
            ?? CorrelationContext.Current;
        if (correlationId is not null
            && !request.Headers.Contains(CorrelationLoggingMiddleware.HeaderName))
            request.Headers.TryAddWithoutValidation(CorrelationLoggingMiddleware.HeaderName, correlationId);
        return base.SendAsync(request, cancellationToken);
    }
}
