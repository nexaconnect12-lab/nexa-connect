using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Notification.Application.Messages;
using NexaConnect.Services.Notification.Infrastructure;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.Configure<NotificationProviderOptions>(builder.Configuration.GetSection("NotificationProvider"));
if (Uri.TryCreate(builder.Configuration["NotificationProvider:BaseUrl"], UriKind.Absolute, out var notificationBaseUrl))
{
    builder.Services.AddHttpClient<INotificationSender, HttpNotificationSender>(client => client.BaseAddress = notificationBaseUrl);
}
else
if (builder.Configuration.GetValue<string>("Persistence:Provider")?.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true)
{
    var dataSource = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("Notification") ?? throw new InvalidOperationException("ConnectionStrings:Notification is required.")).Build();
    builder.Services.AddSingleton(dataSource);
    builder.Services.AddSingleton<INotificationSender, PostgresNotificationSender>();
}
else builder.Services.AddSingleton<INotificationSender, InMemoryNotificationSender>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
