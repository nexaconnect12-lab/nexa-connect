namespace NexaConnect.Services.POS.Domain.ManualRefunds;

public sealed class ManualRefundReferenceConflictException()
    : InvalidOperationException("A refund already uses this transfer reference.");
