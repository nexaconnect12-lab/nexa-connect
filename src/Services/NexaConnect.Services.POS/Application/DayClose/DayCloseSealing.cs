using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.DayClose;
namespace NexaConnect.Services.POS.Application.DayClose;

public interface IDaySealStore:IDayCloseStore;
public interface IDaySealEvidenceReader:IDayCloseEvidenceReader;
public sealed class DayCloseSealing(IDaySealStore store,IDaySealEvidenceReader evidence,
    IRestaurantScopeReader scopes,IAuthorizationDecisionClient authorization,TimeProvider clock,ILoggerFactory logs)
{
    private readonly DayClosePreparation workflow=new(store,evidence,scopes,authorization,clock,logs.CreateLogger<DayClosePreparation>(),sealing:true);
    public Task<PreparationView> ReadAsync(Guid organization,Guid branch,DateOnly date,PosUserContext user,CancellationToken ct)=>workflow.ReadAsync(organization,branch,date,user,ct);
    public Task<PreparationView> SealAsync(Guid organization,PreparationCommand command,PosUserContext user,CancellationToken ct)
    {
        if(command.ReviewedCutoffVersion is null or <=0)throw new ArgumentException();
        return workflow.PrepareAsync(organization,command,user,ct);
    }
}
