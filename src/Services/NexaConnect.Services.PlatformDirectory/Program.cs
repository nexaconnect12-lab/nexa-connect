using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.PlatformDirectory;
using NexaConnect.Services.PlatformDirectory.Application.Access;
using NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
{
    string connectionString = builder.Configuration.GetConnectionString("PlatformDirectory")
        ?? throw new InvalidOperationException("ConnectionStrings:PlatformDirectory is required.");
    return NpgsqlDataSource.Create(connectionString);
});
builder.Services.AddScoped<IOrganizationAccessReader, PostgresOrganizationAccessReader>();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
