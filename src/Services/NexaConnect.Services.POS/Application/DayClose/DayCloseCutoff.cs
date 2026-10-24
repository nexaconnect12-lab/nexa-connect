using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Application.DayClose;

public interface ICutoffPreparationStore : IDayCloseStore;
public interface ICutoffEvidenceReader : IDayCloseEvidenceReader;

/// <summary>Separate durable coordination using the existing lease/version/replay preparation use case.</summary>
public sealed class DayCloseCutoff(ICutoffPreparationStore store,ICutoffEvidenceReader evidence,
    IRestaurantScopeReader scopes,IAuthorizationDecisionClient authorization,TimeProvider clock,ILoggerFactory logs)
{
    private readonly DayClosePreparation workflow=new(store,evidence,scopes,authorization,clock,logs.CreateLogger<DayClosePreparation>());
    public Task<PreparationView> ReadAsync(Guid organization,Guid branch,DateOnly date,PosUserContext user,CancellationToken ct)=>workflow.ReadAsync(organization,branch,date,user,ct);
    public Task<PreparationView> PrepareAsync(Guid organization,PreparationCommand command,PosUserContext user,CancellationToken ct)=>workflow.PrepareAsync(organization,command,user,ct);
}
