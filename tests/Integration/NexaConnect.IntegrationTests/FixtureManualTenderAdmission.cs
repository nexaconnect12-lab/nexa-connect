extern alias ORDER;
using ORDER::NexaConnect.Services.Order.Application.ManualTenders;
namespace NexaConnect.IntegrationTests;
// Persistence fixtures deliberately substitute POS; joined acceptance exercises the real boundary.
internal sealed class FixtureManualTenderAdmission(TimeProvider? clock = null) : IManualTenderAdmission
{
    public Task<DateTimeOffset> AdmitAsync(ManualTenderAdmissionRequest request, CancellationToken ct) =>
        Task.FromResult((clock ?? TimeProvider.System).GetUtcNow());
}
