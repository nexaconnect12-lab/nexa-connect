using Microsoft.Extensions.Options;
using NexaConnect.Services.Payment.Application.Intents;
using NexaConnect.Services.Payment.Application.Refunds;
using NexaConnect.Services.Payment.Infrastructure;
using NexaConnect.Services.Payment.Infrastructure.Providers;

namespace NexaConnect.UnitTests;

public sealed class PaymentRefundLifecycleTests
{
    [Fact]
    public async Task Partial_refunds_reserve_total_and_create_immutable_receipt_snapshot()
    {
        (Guid organization, PaymentIntent intent, InMemoryPaymentRefunds store) = Captured(100m);
        var provider = new RefundProvider(new(ProviderRefundOutcome.Refunded, "refund-1", null));
        var service = new PaymentRefundService(store, provider);

        PaymentRefund first = await service.RefundAsync(organization, intent.Id, Command(40m), Context(), default);
        PaymentRefund replay = await service.RefundAsync(organization, intent.Id,
            new(first.OperationId, 40m, "THB", "customer_request", Guid.NewGuid()), Context(), default);
        PaymentRefund second = await service.RefundAsync(organization, intent.Id, Command(60m), Context(), default);

        Assert.Equal("completed", first.Status);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(40m, first.Receipt!.CumulativeRefundedAmount);
        Assert.Equal(100m, second.Receipt!.CumulativeRefundedAmount);
        Assert.StartsWith("RF-", second.Receipt.ReceiptNumber);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RefundAsync(
            organization, intent.Id, Command(0.01m), Context(), default));
    }

    [Fact]
    public async Task Response_loss_is_reconciled_by_status_and_never_repeats_command()
    {
        (Guid organization, PaymentIntent intent, InMemoryPaymentRefunds store) = Captured(100m,
            new PaymentProviderOptions { LeaseDuration = TimeSpan.FromMilliseconds(1), MaximumRefundRecoveryAttempts = 3 });
        var provider = new RefundProvider(new(ProviderRefundOutcome.Unknown, null, "provider_timeout"),
            new(ProviderRefundOutcome.Refunded, "refund-recovered", null));
        PaymentRefund uncertain = await new PaymentRefundService(store, provider).RefundAsync(
            organization, intent.Id, Command(25m), Context(), default);
        await Task.Delay(10);
        PaymentRefund recovered = await new PaymentRefundRecoveryService(store, provider).RecoverAsync(
            organization, uncertain.Id, Context(), default);

        Assert.Equal("refund_unknown", uncertain.Status);
        Assert.Equal("completed", recovered.Status);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, provider.StatusCalls);
    }

    [Fact]
    public async Task Same_operation_with_changed_financial_payload_is_rejected()
    {
        (Guid organization, PaymentIntent intent, InMemoryPaymentRefunds store) = Captured(100m);
        var provider = new RefundProvider(new(ProviderRefundOutcome.Unknown, null, "provider_timeout"));
        Guid operation = Guid.NewGuid();
        await new PaymentRefundService(store, provider).RefundAsync(organization, intent.Id,
            new(operation, 20m, "THB", "other", Guid.NewGuid()), Context(), default);

        await Assert.ThrowsAsync<PaymentRefundConflictException>(() => new PaymentRefundService(store, provider).RefundAsync(
            organization, intent.Id, new(operation, 21m, "THB", "other", Guid.NewGuid()), Context(), default));
    }

    private static (Guid, PaymentIntent, InMemoryPaymentRefunds) Captured(decimal amount, PaymentProviderOptions? options = null)
    {
        options ??= new PaymentProviderOptions(); var configured = Options.Create(options);
        var intents = new InMemoryPaymentIntents(configured); Guid organization = Guid.NewGuid(); PaymentMutationContext context = Context();
        PaymentIntent intent = intents.Create(organization, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("D"), amount, "THB", "card"), context);
        PaymentAuthorizationLease authorization = intents.BeginAuthorization(organization, intent.Id, context);
        intent = intents.CompleteAuthorization(organization, intent.Id, authorization.Intent.ConcurrencyVersion,
            ProviderAuthorizationOutcome.Authorized, "charge-1", null, context);
        PaymentAuthorizationLease capture = intents.BeginCapture(organization, intent.Id, context);
        intent = intents.CompleteCapture(organization, intent.Id, capture.Intent.ConcurrencyVersion,
            ProviderCaptureOutcome.Captured, "capture-1", null, context);
        return (organization, intent, new InMemoryPaymentRefunds(intents, configured));
    }
    private static CreatePaymentRefund Command(decimal amount) => new(Guid.NewGuid(), amount, "THB", "customer_request", Guid.NewGuid());
    private static PaymentMutationContext Context() => new("manager-1", Guid.NewGuid());

    private sealed class RefundProvider(ProviderRefundResult command, ProviderRefundResult? status = null) : IPaymentProvider
    {
        public int Calls { get; private set; }
        public int StatusCalls { get; private set; }
        public Task<ProviderAuthorizationResult> AuthorizeAsync(PaymentIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProviderRefundResult> RefundAsync(PaymentIntent intent, PaymentRefund refund, CancellationToken token)
        { Calls++; return Task.FromResult(command); }
        public Task<ProviderRefundResult> GetRefundStatusAsync(PaymentIntent intent, PaymentRefund refund, CancellationToken token)
        { StatusCalls++; return Task.FromResult(status ?? command); }
    }
}
