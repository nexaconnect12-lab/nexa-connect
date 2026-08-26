namespace NexaConnect.Services.Payment.Application.Intents;

public static class PaymentCaptureRecoveryRegistration
{
    public static IServiceCollection AddPaymentCaptureRecoveryWorker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (configuration.GetValue("PaymentProvider:CaptureRecoveryEnabled", true))
            services.AddHostedService<PaymentCaptureRecoveryWorker>();

        return services;
    }
}
