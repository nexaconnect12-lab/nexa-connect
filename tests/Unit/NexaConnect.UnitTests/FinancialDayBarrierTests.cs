using System.Security.Claims;
using NexaConnect.Infrastructure.Authorization;
namespace NexaConnect.UnitTests;

public sealed class FinancialDayBarrierTests
{
    private static readonly Action<string?,string,bool,bool>[] Policies =
    [NexaConnect.Services.Order.Domain.FinancialDayBarrier.ValidateTransition,
     NexaConnect.Services.Payment.Domain.FinancialDayBarrier.ValidateTransition,
     NexaConnect.Services.POS.Domain.FinancialDayBarrier.ValidateTransition];

    [Fact] public void An_expired_lease_cannot_arm_but_expiry_never_releases_an_armed_or_committed_barrier()
    {
        foreach(var policy in Policies)
        {
            Assert.Throws<InvalidOperationException>(()=>policy(null,"armed",false,false));
            policy(null,"armed",true,false);policy("armed","armed",false,false);
            policy("armed","committed",false,true);policy("committed","committed",false,true);
            policy("committed","armed",false,false);
        }
    }
    [Fact] public void Terminal_decisions_cannot_reverse_and_unarmed_abort_retains_a_tombstone()
    {
        foreach(var policy in Policies)
        {
            policy(null,"aborted",false,true);policy("armed","aborted",false,true);policy("aborted","aborted",false,true);
            Assert.Throws<InvalidOperationException>(()=>policy("aborted","armed",true,false));
            Assert.Throws<InvalidOperationException>(()=>policy("aborted","committed",true,true));
            Assert.Throws<InvalidOperationException>(()=>policy("committed","aborted",true,true));
            Assert.Throws<InvalidOperationException>(()=>policy(null,"committed",true,true));
            Assert.Throws<ArgumentException>(()=>policy("armed","committed",true,false));
            Assert.Throws<ArgumentException>(()=>policy("armed","armed",true,true));
        }
    }
    [Fact] public void Settlement_workload_requires_authenticated_exact_client_and_service_account()
    {
        const string client="nexaconnect-pos-service";
        ClaimsPrincipal Principal(string azp,string user,string? authentication="Bearer")=>new(new ClaimsIdentity([new("azp",azp),new("preferred_username",user)],authentication));
        Assert.True(ServiceWorkloadPrincipal.IsClientCredentials(Principal(client,"service-account-"+client),client));
        Assert.False(ServiceWorkloadPrincipal.IsClientCredentials(Principal(client,"manager"),client));
        Assert.False(ServiceWorkloadPrincipal.IsClientCredentials(Principal("nexaconnect-payment-service","service-account-nexaconnect-payment-service"),client));
        Assert.False(ServiceWorkloadPrincipal.IsClientCredentials(Principal(client,"service-account-"+client,null),client));
    }
}
