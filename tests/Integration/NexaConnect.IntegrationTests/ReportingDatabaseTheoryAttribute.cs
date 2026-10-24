namespace NexaConnect.IntegrationTests;
public sealed class ReportingDatabaseTheoryAttribute:TheoryAttribute
{
 public ReportingDatabaseTheoryAttribute()
 {
  var environment=Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
  if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_INTEGRATION_DB"))||environment is not("Development" or "Test" or "Testing"))Skip="NEXACONNECT_REPORTING_INTEGRATION_DB and a safe environment are required.";
 }
}
