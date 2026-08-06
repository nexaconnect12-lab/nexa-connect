using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Infrastructure.Authorization;
using NexaConnect.Services.POS.Infrastructure.Identity;
using NexaConnect.Services.POS.Infrastructure.Persistence;
using NexaConnect.Services.POS.Infrastructure.Restaurant;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("POS")
    ?? throw new InvalidOperationException("ConnectionStrings:POS is required.")));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<PosWorkloadTokenProvider>();
builder.Services.AddHttpClient<RestaurantHierarchyClient>();
builder.Services.AddHttpClient("Authorization");
builder.Services.AddScoped<IShiftStore, PostgresShiftStore>();
builder.Services.AddScoped<PostgresCashSessionStore>();
builder.Services.AddScoped<PostgresTerminalStore>();
builder.Services.AddScoped<IRestaurantScopeReader, RestaurantHierarchyClient>();
builder.Services.AddScoped<IAuthorizationDecisionClient, AuthorizationDecisionClient>();
builder.Services.AddScoped<ShiftApplicationService>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "HTTPS is required for the POS API.",
                status = StatusCodes.Status400BadRequest
            });
            return;
        }

        await next();
    });
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public sealed class PosProgram;
