using Microsoft.Extensions.Options;
using NexaConnect.Services.Notification.Application.Delivery;
using NexaConnect.Services.Notification.Domain;
using NexaConnect.Services.Notification.Infrastructure;

namespace NexaConnect.UnitTests;

public sealed class NotificationDeliveryTests
{
    [Fact]
    public async Task Processor_submits_claimed_notification_and_records_provider_acceptance()
    {
        var work = Work(NotificationDeliveryOperation.Submit);
        var repository = new StubRepository(work);
        var provider = new StubProvider(new(NotificationProviderOutcome.Accepted, "test", "receipt-1"));
        var processor = new NotificationDeliveryProcessor(repository, provider,
            Options.Create(new NotificationDeliveryOptions { MaximumAttempts = 4 }));

        Assert.True(await processor.ProcessOneAsync(default));
        Assert.Same(work, provider.Submitted);
        Assert.Equal(NotificationProviderOutcome.Accepted, repository.Recorded?.Outcome);
        Assert.Equal(NotificationDeliveryStatus.ProviderAccepted, repository.Decision?.Status);
    }

    [Fact]
    public async Task Processor_classifies_provider_exception_without_exposing_message_content()
    {
        var repository = new StubRepository(Work(NotificationDeliveryOperation.Submit));
        var processor = new NotificationDeliveryProcessor(repository, new ThrowingProvider(),
            Options.Create(new NotificationDeliveryOptions()));

        Assert.True(await processor.ProcessOneAsync(default));
        Assert.Equal(NotificationProviderOutcome.TransientFailure, repository.Recorded?.Outcome);
        Assert.Equal(nameof(HttpRequestException), repository.Recorded?.ErrorCategory);
        Assert.DoesNotContain("private body", repository.Recorded?.ErrorCategory ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Domain_rejects_unapproved_channels_and_control_characters()
    {
        Assert.Throws<ArgumentException>(() => NotificationAggregate.Normalize(Guid.NewGuid(), "fax", "target", "subject", "body"));
        Assert.Throws<ArgumentException>(() => NotificationAggregate.Normalize(Guid.NewGuid(), "email", "target\0", "subject", "body"));
    }

    [Fact]
    public async Task Http_provider_sends_idempotency_authentication_and_correlation_headers()
    {
        var handler = new RecordingHandler();
        var provider = new HttpNotificationProvider(new HttpClient(handler) { BaseAddress = new Uri("https://provider.test/") },
            Options.Create(new NotificationProviderOptions { ApiToken = "secret-token", ProviderCode = "test" }));
        NotificationDeliveryWork work = Work(NotificationDeliveryOperation.Submit);

        NotificationProviderResult result = await provider.SubmitAsync(work, default);

        Assert.Equal(NotificationProviderOutcome.Accepted, result.Outcome);
        Assert.Equal(work.NotificationId.ToString("D"), handler.IdempotencyKey);
        Assert.Equal(work.RequestCorrelationId, handler.CorrelationId);
        Assert.Equal("Bearer", handler.AuthenticationScheme);
        Assert.Equal("secret-token", handler.AuthenticationParameter);
        Assert.Contains("private@example.test", handler.Body, StringComparison.Ordinal);
    }

    private static NotificationDeliveryWork Work(NotificationDeliveryOperation operation) => new(Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "email", "private@example.test", "private subject", "private body",
        operation, 1, operation == NotificationDeliveryOperation.Reconcile ? "receipt-1" : null,
        Guid.NewGuid(), Guid.NewGuid().ToString("D"));

    private sealed class StubRepository(NotificationDeliveryWork? work) : INotificationDeliveryRepository
    {
        public NotificationProviderResult? Recorded { get; private set; }
        public NotificationDeliveryDecision? Decision { get; private set; }
        public Task<NotificationDeliveryWork?> ClaimDueAsync(TimeSpan lease, CancellationToken cancellationToken) => Task.FromResult(work);
        public Task RecordAsync(NotificationDeliveryWork claimed, NotificationProviderResult result,
            NotificationDeliveryDecision decision,
            CancellationToken cancellationToken)
        {
            Recorded = result;
            Decision = decision;
            return Task.CompletedTask;
        }
    }

    private sealed class StubProvider(NotificationProviderResult result) : INotificationProvider
    {
        public NotificationDeliveryWork? Submitted { get; private set; }
        public Task<NotificationProviderResult> SubmitAsync(NotificationDeliveryWork work, CancellationToken cancellationToken)
        {
            Submitted = work;
            return Task.FromResult(result);
        }
        public Task<NotificationProviderResult> GetReceiptAsync(NotificationDeliveryWork work, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class ThrowingProvider : INotificationProvider
    {
        public Task<NotificationProviderResult> SubmitAsync(NotificationDeliveryWork work, CancellationToken cancellationToken) =>
            throw new HttpRequestException("private body");
        public Task<NotificationProviderResult> GetReceiptAsync(NotificationDeliveryWork work, CancellationToken cancellationToken) =>
            throw new HttpRequestException("private body");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? IdempotencyKey { get; private set; }
        public string? CorrelationId { get; private set; }
        public string? AuthenticationScheme { get; private set; }
        public string? AuthenticationParameter { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            IdempotencyKey = Assert.Single(request.Headers.GetValues("Idempotency-Key"));
            CorrelationId = Assert.Single(request.Headers.GetValues("X-Correlation-ID"));
            AuthenticationScheme = request.Headers.Authorization?.Scheme;
            AuthenticationParameter = request.Headers.Authorization?.Parameter;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
            {
                Content = new StringContent("{\"outcome\":\"accepted\",\"providerMessageId\":\"receipt-1\"}",
                    System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
