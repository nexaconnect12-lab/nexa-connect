using System.Reflection;
using NexaConnect.Services.POS.Domain.DayClose;

namespace NexaConnect.ArchitectureTests;

public sealed class DayCutoffDependencyTests
{
    [Fact]
    public void Cutoff_domain_models_depend_only_on_their_context_and_system_types()
    {
        Type[] models=[typeof(SettlementAcknowledgement),typeof(SettlementCommand),typeof(SettlementReceipt),typeof(BranchDaySettlement),typeof(NexaConnect.Services.POS.Domain.FinancialDayBarrier),typeof(NexaConnect.Services.Order.Domain.FinancialDayBarrier),typeof(FinalizationCommand),typeof(FinalizationCancel),typeof(FinalizationState),typeof(FinalizationFence),typeof(FinalizationPreparation),typeof(BranchDayClose),typeof(BranchDayApproval),typeof(ApprovalCommand),typeof(DayApprovalDecision),typeof(DayApprovalState),typeof(BranchDayCutoff),typeof(BranchDaySeal),typeof(DaySealComparison),typeof(DaySealRecordChange),typeof(NexaConnect.Services.POS.Domain.FinancialRecord),typeof(NexaConnect.Services.POS.Domain.FinancialImpact),typeof(NexaConnect.Services.Order.Domain.FinancialRecord),typeof(NexaConnect.Services.Order.Domain.FinancialImpact),typeof(NexaConnect.Services.POS.Domain.FinancialChange),typeof(NexaConnect.Services.Order.Domain.FinancialChange),typeof(DaySealEvidence),typeof(DaySealReference),typeof(DayIdentity),typeof(DayTender),typeof(DayEvidence),typeof(CutoffReference),
            typeof(DayCutoffEvidence),typeof(PreparationCommand),typeof(PreparationState),typeof(NexaConnect.Services.Order.Domain.LateWorkCase),typeof(NexaConnect.Services.Payment.Domain.LateWorkCase),typeof(NexaConnect.Services.POS.Domain.LateWorkCase),typeof(NexaConnect.Services.POS.Domain.CashTenderProof),typeof(NexaConnect.Services.POS.Domain.LateCashCase),typeof(NexaConnect.Services.POS.Domain.CorrectionPostingDay),typeof(NexaConnect.Services.POS.Domain.LateCashCorrection)];
        foreach(var model in models)
        {
            var types=model.GetConstructors().SelectMany(x=>x.GetParameters().Select(p=>p.ParameterType))
                .Concat(model.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)
                    .SelectMany(x=>x.GetParameters().Select(p=>p.ParameterType).Append(x.ReturnType)));
            foreach(var type in types.SelectMany(Expand))
                Assert.True(type.Namespace?.StartsWith("System",StringComparison.Ordinal)==true || models.Contains(type),$"Cutoff Domain depends on {type.FullName}");
        }
    }
    private static IEnumerable<Type> Expand(Type type)=>type.IsArray||type.IsByRef?Expand(type.GetElementType()!):new[]{type}.Concat(type.IsGenericType?type.GetGenericArguments().SelectMany(Expand):[]);
}
