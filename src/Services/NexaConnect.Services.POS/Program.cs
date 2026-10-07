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
using NexaConnect.Services.POS.Application.CashReviews;
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
builder.Services.AddScoped<ICashReviewStore, PostgresCashReviewStore>();
builder.Services.AddScoped<IOrderSettlementProjectionStore, PostgresOrderSettlementProjectionStore>();
builder.Services.AddScoped<ITerminalStore, PostgresTerminalStore>();
builder.Services.AddScoped<IRestaurantScopeReader, RestaurantHierarchyClient>();
builder.Services.AddScoped<IAuthorizationDecisionClient, AuthorizationDecisionClient>();
builder.Services.AddScoped<ShiftApplicationService>();
builder.Services.AddScoped<CashSessionApplicationService>();
builder.Services.AddScoped<CashReviewApplicationService>();
builder.Services.AddScoped<OrderSettlementProjectionService>();
builder.Services.AddScoped<TerminalEnrollmentApplicationService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOrderSettlementConsumer(builder.Configuration);

builder.Services.AddScoped<ICashClosePublicationStore, PostgresCashClosePublicationStore>();
builder.Services.AddScoped<CashClosePublisher>();
if (builder.Configuration.GetValue<bool>("CashClosePublication:Enabled")) builder.Services.AddHostedService<CashClosePublicationWorker>();
if (builder.Configuration.GetValue<bool>("CashClosePublication:Enabled") || builder.Configuration.GetValue<bool>("Outbox:Enabled")) builder.Services.AddHostedService<CashCloseBacklogMonitor>();
if (builder.Configuration.GetValue<bool>("Outbox:Enabled")) NexaConnect.Infrastructure.Messaging.OutboxServiceCollectionExtensions.AddPostgresOutbox(builder.Services, builder.Configuration, "POS");
builder.Services.AddScoped<NexaConnect.Services.POS.Application.CashReviews.PosDayRead>();
builder.Services.AddScoped<NexaConnect.Services.POS.Application.CashReviews.IPosDayReader, NexaConnect.Services.POS.Infrastructure.Persistence.PostgresPosDayReader>();
builder.Services.AddScoped<NexaConnect.Services.POS.Application.DayClose.IDayCloseStore, NexaConnect.Services.POS.Infrastructure.DayClose.PostgresDayCloseStore>();
builder.Services.AddScoped<NexaConnect.Services.POS.Application.DayClose.DayClosePreparation>();
builder.Services.AddHttpClient<NexaConnect.Services.POS.Application.DayClose.IDayCloseEvidenceReader, NexaConnect.Services.POS.Infrastructure.DayClose.HttpDayCloseEvidenceReader>(c => { c.BaseAddress = new Uri(builder.Configuration["Services:Reporting"] ?? "https://reporting.invalid/"); c.Timeout = TimeSpan.FromSeconds(15); }).AddNexaConnectCorrelationPropagation();

builder.Services.AddScoped<NexaConnect.Services.POS.Application.CashReviews.PosCutoffs>();
builder.Services.AddScoped<NexaConnect.Services.POS.Application.CashReviews.IPosCutoffStore, NexaConnect.Services.POS.Infrastructure.Persistence.PostgresPosCutoffStore>();
builder.Services.AddScoped<NexaConnect.Services.POS.Application.DayClose.ICutoffPreparationStore,NexaConnect.Services.POS.Infrastructure.DayClose.PostgresDayCutoffStore>();
builder.Services.AddScoped<NexaConnect.Services.POS.Application.DayClose.ICutoffEvidenceReader,NexaConnect.Services.POS.Infrastructure.DayClose.HttpCutoffEvidenceReader>();
builder.Services.AddScoped<NexaConnect.Services.POS.Application.DayClose.DayCloseCutoff>();
builder.Services.AddScoped<NexaConnect.Services.POS.Application.DayClose.IDaySealStore,NexaConnect.Services.POS.Infrastructure.DayClose.PostgresDaySealStore>();
builder.Services.AddScoped<NexaConnect.Services.POS.Application.DayClose.IDaySealEvidenceReader,NexaConnect.Services.POS.Infrastructure.DayClose.HttpDaySealEvidenceReader>();
builder.Services.AddScoped<NexaConnect.Services.POS.Application.DayClose.DayCloseSealing>();
foreach (string service in new[]{"Order","Payment","POS","Reporting"})
{
    builder.Services.AddHttpClient("DayCutoff"+service,c=>{ c.BaseAddress=new Uri(builder.Configuration["Services:"+service]??"https://cutoff-dependency.invalid/");c.Timeout=TimeSpan.FromSeconds(15); }).AddNexaConnectCorrelationPropagation();
}
var app = builder.Build();
app.UseNexaConnectRequestLogging();
app.Use(async (context,next) => { if(context.Request.Path.StartsWithSegments("/api/pos/v1/customer/day-cutoffs")) context.Response.Headers.CacheControl="no-store"; await next(); });
app.Use(async (context, next) => { if (context.Request.Path.StartsWithSegments("/api/pos/v1/customer", StringComparison.OrdinalIgnoreCase)) context.Response.Headers.CacheControl = "no-store"; await next(context); });

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
