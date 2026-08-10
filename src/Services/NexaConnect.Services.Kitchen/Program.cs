using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.Services.Kitchen.Application;
using NexaConnect.Services.Kitchen.Infrastructure;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "kitchen");
builder.Services.Configure<KitchenOptions>(builder.Configuration.GetSection("Kitchen"));

if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    var connectionString = builder.Configuration.GetConnectionString("Kitchen")
        ?? throw new InvalidOperationException("ConnectionStrings:Kitchen is required.");
    builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
    builder.Services.AddSingleton<IKitchenTicketStore, PostgresKitchenTicketStore>();
    builder.Services.AddPostgresInbox(builder.Configuration, "Kitchen");
}
else
{
    builder.Services.AddSingleton<IKitchenTicketStore, InMemoryKitchenTicketStore>();
}

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public sealed class KitchenProgram;
