using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Authorization.Application.Decisions;
using NexaConnect.Services.Authorization.Infrastructure.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Authorization")
    ?? throw new InvalidOperationException("ConnectionStrings:Authorization is required.")));
builder.Services.AddScoped<IAuthorizationDecisionService, PostgresAuthorizationDecisionService>();
var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
