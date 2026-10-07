using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Infrastructure.Authorization;
using NexaConnect.Observability;
using NexaConnect.Services.Reporting.Application;
using NexaConnect.Services.Reporting.Infrastructure;
using NexaConnect.Services.Reporting.Infrastructure.Persistence;
using Npgsql;
using NexaConnect.Services.Reporting.Infrastructure.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.AddNexaConnectObservability("nexaconnect-reporting");
NexaConnect.Infrastructure.Authentication.AuthenticationServiceCollectionExtensions.EnsureProductionHttps(builder.Configuration, builder.Environment);
builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddNexaConnectDataProtection(builder.Configuration, builder.Environment, "reporting");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Reporting") ?? throw new InvalidOperationException("ConnectionStrings:Reporting is required.")));
builder.Services.AddScoped<FinancialCompleteness>();
builder.Services.AddScoped<IFinancialCompletenessRepository, PostgresFinancialCompletenessRepository>();
builder.Services.AddScoped<ReportingQueries>();
builder.Services.AddScoped<IReportingReadRepository, PostgresReportingReadRepository>();
builder.Services.AddScoped<SaleFinancialReporting>();
builder.Services.AddScoped<ISaleFinancialFactRepository, PostgresSaleFinancialFactRepository>();
builder.Services.AddScoped<RefundFinancialReporting>();
builder.Services.AddScoped<IRefundFinancialFactRepository, PostgresRefundFinancialFactRepository>();
builder.Services.AddScoped<ActivityService>();
builder.Services.AddScoped<IActivityProjectionRepository, PostgresActivityProjectionRepository>();
builder.Services.AddActivityConsumer(builder.Configuration);
builder.Services.AddHttpClient("PlatformDirectory", client => client.BaseAddress = new Uri(builder.Configuration["Services:PlatformDirectory"] ?? throw new InvalidOperationException("Services:PlatformDirectory is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<ProductAuthorizationClient>(client => client.BaseAddress = new Uri(builder.Configuration["Services:Authorization"] ?? throw new InvalidOperationException("Services:Authorization is required."))).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient<IServiceWorkloadTokenProvider,ServiceWorkloadTokenProvider>(client=>client.Timeout=TimeSpan.FromSeconds(15)).AddNexaConnectCorrelationPropagation();
builder.Services.AddHttpClient("ReportingRestaurant",client=>{client.BaseAddress=new Uri(builder.Configuration["Services:Restaurant"]??throw new InvalidOperationException("Services:Restaurant is required."));client.Timeout=TimeSpan.FromSeconds(15);}).AddNexaConnectCorrelationPropagation();
builder.Services.AddScoped<IReportingAccessDependencies,HttpReportingCustomerAuthorizer>();
builder.Services.AddScoped<IReportingCustomerAuthorizer,ReportingCustomerAuthorizer>();

builder.Services.AddScoped<CashCloseReporting>();
builder.Services.AddScoped<ICashCloseRepository, PostgresCashCloseRepository>();
builder.Services.AddHttpClient<ICashCloseAccess, HttpCashCloseAccess>(c => { c.BaseAddress = new Uri(builder.Configuration["Services:POS"] ?? throw new InvalidOperationException("Services:POS is required.")); c.Timeout = TimeSpan.FromSeconds(15); }).AddNexaConnectCorrelationPropagation();
if (builder.Configuration.GetValue<bool>("CashCloseConsumer:Enabled")) builder.Services.AddHostedService<CashCloseConsumer>();
if (builder.Configuration.GetValue<bool>("PaymentRefundConsumer:Enabled")) builder.Services.AddHostedService<PaymentRefundFinancialConsumer>();
if (builder.Configuration.GetValue<bool>("OrderSaleConsumer:Enabled")) builder.Services.AddHostedService<OrderSaleFinancialConsumer>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<EndOfDayDraft>();
builder.Services.AddScoped<IEndOfDaySources, HttpEndOfDaySources>();
foreach (string service in new[] { "Order", "Payment", "POS" })
{
    builder.Services.AddHttpClient("EndOfDay" + service, c =>
    {
        c.BaseAddress = new Uri(builder.Configuration["Services:" + service] ?? throw new InvalidOperationException("Services:" + service + " is required."));
        c.Timeout = TimeSpan.FromSeconds(15);
    }).AddNexaConnectCorrelationPropagation();
}
builder.Services.AddScoped<DayCutoffReconciliation>();
builder.Services.AddScoped<SealedDayReconciliation>();
builder.Services.AddScoped<ISealedSources,HttpCutoffSources>();
builder.Services.AddScoped<ICutoffSources,HttpCutoffSources>();
var app = builder.Build();
app.UseNexaConnectRequestLogging();
app.Use(async(context,next)=>{if((context.Request.Path.StartsWithSegments("/api/reporting/v1/customer/day-cutoff-reconciliation")||context.Request.Path.StartsWithSegments("/api/reporting/v1/customer/day-seal-reconciliation")))context.Response.Headers.CacheControl="no-store";await next();});
app.Use(async (context, next) => { if (context.Request.Path.Value?.EndsWith("/reports/end-of-day", StringComparison.OrdinalIgnoreCase) == true) context.Response.Headers.CacheControl = "no-store"; await next(context); });
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
