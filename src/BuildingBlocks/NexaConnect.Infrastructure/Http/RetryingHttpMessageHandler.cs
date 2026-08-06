using Microsoft.Extensions.Logging;

namespace NexaConnect.Infrastructure.Http;

public sealed class RetryingHttpMessageHandler(ILogger<RetryingHttpMessageHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            if (attempt >= 3 || ((int)response.StatusCode < 500 && response.StatusCode != System.Net.HttpStatusCode.RequestTimeout))
                return response;
            response.Dispose();
            var delay = TimeSpan.FromMilliseconds(100 * attempt);
            logger.LogWarning("Retrying outbound request {Method} {Uri} after {Delay} (attempt {Attempt})", request.Method, request.RequestUri, delay, attempt);
            await Task.Delay(delay, cancellationToken);
        }
    }
}
