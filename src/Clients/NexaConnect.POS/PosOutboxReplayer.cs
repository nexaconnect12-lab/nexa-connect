using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace NexaConnect.POS;

public sealed class PosOutboxReplayer(PosClientConfiguration configuration, LocalOutboxStore outbox)
{
    public async Task<int> ReplayAsync(
        PosTokenSet token,
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { BaseAddress = new Uri(configuration.PosApi) };
        int replayed = 0;
        foreach (LocalOutboxOperation operation in outbox.Load())
        {
            if (token.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                break;
            }

            using var request = new HttpRequestMessage(new HttpMethod(operation.Method), operation.RelativeUri)
            {
                Content = new StringContent(operation.PayloadJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);
            request.Headers.Add("X-Client-Operation-Id", operation.OperationId.ToString("D"));
            outbox.MarkAttempted(operation.OperationId);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                break;
            }

            outbox.Remove(operation.OperationId);
            replayed++;
        }

        return replayed;
    }
}
