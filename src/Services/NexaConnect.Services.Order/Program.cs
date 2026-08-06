using NexaConnect.Infrastructure.Authentication;
using NexaConnect.Services.Order.Application.Orders;
using NexaConnect.Services.Order.Application.Workflow;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddNexaConnectApiAuthentication(builder.Configuration);
builder.Services.AddSingleton<InMemoryOrderApplicationService>();
builder.Services.AddSingleton<IOrderApplicationService>(services => services.GetRequiredService<InMemoryOrderApplicationService>());
builder.Services.AddSingleton<IOrderRepository>(services => services.GetRequiredService<InMemoryOrderApplicationService>());

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
