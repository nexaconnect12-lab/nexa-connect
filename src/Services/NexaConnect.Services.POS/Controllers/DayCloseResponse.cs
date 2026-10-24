using NexaConnect.Services.POS.Application.DayClose;
namespace NexaConnect.Services.POS.Controllers;

/// <summary>Preserves the original preparation/cutoff command representation on their existing routes.</summary>
internal static class DayCloseResponse
{
    public static object Legacy(PreparationView view)=>new
    {
        view.Identity,view.Version,view.Status,view.Snapshot,view.Blockers,view.ValidatedAtUtc,view.CanPrepare,
        pendingCommand=view.PendingCommand is null?null:new
        {
            view.PendingCommand.BranchId,view.PendingCommand.BusinessDate,view.PendingCommand.OperationId,
            view.PendingCommand.ExpectedVersion,view.PendingCommand.ReasonCode
        }
    };
}
