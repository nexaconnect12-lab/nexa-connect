namespace NexaConnect.Services.Order.Application.Workflow;

internal static class OrderPaymentEventIdentity
{
    public static Guid Completion(Guid orderId, string? method) => method == "card_omise_test"
        ? new Guid(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"NexaConnect.Order.CardCompleted.v1:{orderId:D}"))[..16])
        : Guid.NewGuid();
}
