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
            if (operation.TerminalFailureStatusCode is not null) continue;

            using var request = new HttpRequestMessage(new HttpMethod(operation.Method), operation.RelativeUri)
            {
                Content = new StringContent(operation.PayloadJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);
            request.Headers.Add("X-Client-Operation-Id", operation.OperationId.ToString("D"));
            if (operation.TerminalId is not Guid terminalId || terminalId == Guid.Empty)
            {
                outbox.MarkTerminalFailure(operation.OperationId, 400);
                continue;
            }
            request.Headers.Add("X-Nexa-Terminal-Id", terminalId.ToString("D"));
            outbox.MarkAttempted(operation.OperationId);
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException)
            {
                break;
            }
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    int statusCode = (int)response.StatusCode;
                    if (statusCode is 400 or 403 or 409)
                    {
                        outbox.MarkTerminalFailure(operation.OperationId, statusCode);
                        continue;
                    }
                    break;
                }
            }

            outbox.Remove(operation.OperationId);
            replayed++;
        }

        return replayed;
    }
}
