using NexaConnect.Services.Order.Application.ManualTenders;
namespace NexaConnect.UnitTests;
// Explicit admission fixture for Order persistence/use-case tests, not a production fallback.
internal sealed class FixtureManualTenderAdmission(TimeProvider? clock = null) : IManualTenderAdmission
{
    public Task<DateTimeOffset> AdmitAsync(ManualTenderAdmissionRequest request, CancellationToken ct) =>
        Task.FromResult((clock ?? TimeProvider.System).GetUtcNow());
}
