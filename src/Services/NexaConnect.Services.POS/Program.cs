using NexaConnect.Infrastructure.Authentication;
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
