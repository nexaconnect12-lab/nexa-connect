namespace NexaConnect.UnitTests;

public sealed class PilotHostEventTests
{
    [Fact]public void Known_operational_event_has_no_business_payload()=>Assert.True(PilotHostEvent.Valid("recovering","1","not-configured",Guid.NewGuid().ToString("D")));
    [Theory][InlineData("private-token","0","running")][InlineData("running","4","running")][InlineData("running","-1","running")][InlineData("running","0","https://private.example")]
    public void Unrestricted_values_fail_closed(string phase,string count,string qr)=>Assert.False(PilotHostEvent.Valid(phase,count,qr,Guid.NewGuid().ToString("D")));
    [Theory][InlineData("")][InlineData("unvalidated-header")][InlineData("00000000-0000-0000-0000-000000000000")]
    public void Correlation_is_a_nonempty_generated_identifier(string value)=>Assert.False(PilotHostEvent.Valid("running","0","running",value));
}
