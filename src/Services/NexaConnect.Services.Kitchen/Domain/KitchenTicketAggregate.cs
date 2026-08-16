namespace NexaConnect.Services.Kitchen.Domain;

public enum KitchenTicketStatus { Queued, InProgress, Ready, Completed, Cancelled }

public sealed class KitchenConflictException(string message) : InvalidOperationException(message);

public static class KitchenTicketLifecycle
{
 public static bool CanTransition(KitchenTicketStatus from,KitchenTicketStatus to)=>
  from==to||(from==KitchenTicketStatus.Queued&&to is KitchenTicketStatus.InProgress or KitchenTicketStatus.Cancelled)
  ||(from==KitchenTicketStatus.InProgress&&to is KitchenTicketStatus.Ready or KitchenTicketStatus.Cancelled)
  ||(from==KitchenTicketStatus.Ready&&to is KitchenTicketStatus.Completed or KitchenTicketStatus.Cancelled);
 public static void RequireTransition(KitchenTicketStatus from,KitchenTicketStatus to)
 {if(!CanTransition(from,to))throw new KitchenConflictException($"Kitchen ticket cannot transition from {from} to {to}.");}
 public static string ToCode(KitchenTicketStatus status)=>status switch{KitchenTicketStatus.Queued=>"queued",KitchenTicketStatus.InProgress=>"in_progress",KitchenTicketStatus.Ready=>"ready",KitchenTicketStatus.Completed=>"completed",KitchenTicketStatus.Cancelled=>"cancelled",_=>throw new ArgumentOutOfRangeException(nameof(status))};
 public static KitchenTicketStatus Parse(string value)=>value switch{"queued"=>KitchenTicketStatus.Queued,"in_progress"=>KitchenTicketStatus.InProgress,"ready"=>KitchenTicketStatus.Ready,"completed"=>KitchenTicketStatus.Completed,"cancelled"=>KitchenTicketStatus.Cancelled,_=>throw new InvalidOperationException($"Unknown Kitchen status '{value}'.")};
}
