using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Observability;
using NexaConnect.Services.POS.Application.CashSessions;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Application.Terminals;
using NexaConnect.Services.POS.Infrastructure.Authorization;
using NexaConnect.Services.POS.Infrastructure.Identity;
using NexaConnect.Services.POS.Infrastructure.Persistence;
using NexaConnect.Services.POS.Infrastructure.Restaurant;
using NexaConnect.Services.POS.Application.OrderSettlements;
using NexaConnect.Services.POS.Infrastructure.Messaging;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-pos");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "pos");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("POS")
    ?? throw new InvalidOperationException("ConnectionStrings:POS is required.")));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<PosWorkloadTokenProvider>().AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<RestaurantHierarchyClient>().AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient("Authorization").AddNexaConnectCorrelationPropagation();
builder.Services.AddScoped<IShiftStore, PostgresShiftStore>();
builder.Services.AddScoped<ICashSessionStore, PostgresCashSessionStore>();
builder.Services.AddScoped<IOrderSettlementProjectionStore, PostgresOrderSettlementProjectionStore>();
builder.Services.AddScoped<ITerminalStore, PostgresTerminalStore>();
builder.Services.AddScoped<IRestaurantScopeReader, RestaurantHierarchyClient>();
builder.Services.AddScoped<IAuthorizationDecisionClient, AuthorizationDecisionClient>();
builder.Services.AddScoped<ShiftApplicationService>();
builder.Services.AddScoped<CashSessionApplicationService>();
builder.Services.AddScoped<OrderSettlementProjectionService>();
builder.Services.AddScoped<TerminalEnrollmentApplicationService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOrderSettlementConsumer(builder.Configuration);

var app = builder.Build();
app.UseNexaConnectRequestLogging();

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
