namespace NexaConnect.Services.Payment.Domain;
public static class FinancialDayFence
{
 public const string PreparePermission="pos.day-close.finalization.prepare";
 public static bool AllowsMutation(DateTimeOffset from,DateTimeOffset to,FinancialRecord? before,FinancialRecord? after,bool uncertain,string kind)=>FinancialChange.Classify(from,to,before,after,uncertain,kind).AffectsWindow==false;
 public static void ValidateAdmission(bool sameEpoch,long expectedSuffix,long observed,bool completeAndUnrelated)
 {if(!sameEpoch||expectedSuffix<0||expectedSuffix!=observed||!completeAndUnrelated)throw new InvalidOperationException("Fence evidence changed.");}
}
