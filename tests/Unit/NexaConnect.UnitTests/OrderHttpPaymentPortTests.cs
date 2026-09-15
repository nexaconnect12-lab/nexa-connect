using System.Net;
using System.Net.Http.Json;
using NexaConnect.Services.Order.Infrastructure.Clients;

namespace NexaConnect.UnitTests;

public sealed class OrderHttpPaymentPortTests
{
    [Fact]
    public async Task Existing_captured_intent_completes_without_replaying_provider_operations()
    {
        Guid paymentId = Guid.NewGuid();
        var handler = new PaymentHandler((request, _) => Json(HttpStatusCode.Created, paymentId, "captured"));
        var port = Create(handler);

        var result = await port.AuthorizeAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100m, "THB", "card", default);

        Assert.True(result.Completed);
        Assert.Equal(paymentId, result.PaymentId);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("authorizing")]
    [InlineData("unknown")]
    [InlineData("requires_action")]
    [InlineData("capturing")]
    [InlineData("capture_unknown")]
    public async Task Existing_uncertain_intent_is_returned_without_replaying_provider_operations(string status)
    {
        Guid paymentId = Guid.NewGuid();
        var handler = new PaymentHandler((request, _) => Json(HttpStatusCode.Created, paymentId, status));
        var result = await Create(handler).AuthorizeAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100m, "THB", "card", default);

        Assert.False(result.Completed);
        Assert.Equal(status, result.Outcome);
        Assert.Equal(paymentId, result.PaymentId);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Existing_authorized_intent_only_resumes_capture()
    {
        Guid paymentId = Guid.NewGuid();
        var handler = new PaymentHandler((request, call) => call switch
        {
            1 => Json(HttpStatusCode.Created, paymentId, "authorized"),
            2 when request.RequestUri!.AbsolutePath.EndsWith("/capture", StringComparison.Ordinal) =>
                Json(HttpStatusCode.OK, paymentId, "captured"),
            _ => throw new InvalidOperationException("Unexpected payment request.")
        });

        var result = await Create(handler).AuthorizeAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100m, "THB", "card", default);

        Assert.True(result.Completed);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, path => path.EndsWith("/authorize", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pending_intent_authorizes_then_captures_once()
    {
        Guid paymentId = Guid.NewGuid();
        var handler = new PaymentHandler((request, call) => call switch
        {
            1 => Json(HttpStatusCode.Created, paymentId, "pending"),
            2 => Json(HttpStatusCode.OK, paymentId, "authorized"),
            3 => Json(HttpStatusCode.OK, paymentId, "captured"),
            _ => throw new InvalidOperationException("Unexpected payment request.")
        });

        var result = await Create(handler).AuthorizeAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100m, "THB", "card", default);

        Assert.True(result.Completed);
        Assert.EndsWith("/authorize", handler.Requests[1], StringComparison.Ordinal);
        Assert.EndsWith("/capture", handler.Requests[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conflicting_authorization_reads_authoritative_state_before_deciding()
    {
        Guid paymentId = Guid.NewGuid();
        var handler = new PaymentHandler((request, call) => call switch
        {
            1 => Json(HttpStatusCode.Created, paymentId, "pending"),
            2 => new HttpResponseMessage(HttpStatusCode.Conflict),
            3 when request.Method == HttpMethod.Get => Json(HttpStatusCode.OK, paymentId, "captured"),
            _ => throw new InvalidOperationException("Unexpected payment request.")
        });

        var result = await Create(handler).AuthorizeAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100m, "THB", "card", default);

        Assert.True(result.Completed);
        Assert.Equal(HttpMethod.Get, handler.Methods[2]);
    }

    [Fact]
    public async Task Intent_creation_failure_is_retried_by_durable_order_recovery_without_compensation()
    {
        var handler = new PaymentHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<HttpRequestException>(() => Create(handler).AuthorizeAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m, "THB", "card", default));
    }

    private static HttpPaymentPort Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://payment.test/") });

    private static HttpResponseMessage Json(HttpStatusCode statusCode, Guid id, string status) =>
        new(statusCode) { Content = JsonContent.Create(new { id, status, failureCode = (string?)null }) };

    private sealed class PaymentHandler(Func<HttpRequestMessage, int, HttpResponseMessage> send) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            Methods.Add(request.Method);
            return Task.FromResult(send(request, Requests.Count));
        }
    }
}
