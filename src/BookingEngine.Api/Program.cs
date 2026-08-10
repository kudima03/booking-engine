using BookingEngine.Api;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

builder.Services.AddControllers();

builder.Services.AddOutputCache();

if (builder.Environment.IsDevelopment())
{
    _ = builder.Services.AddOpenApi();
}

WebApplication app = builder.Build();

app.UseOutputCache();

if (app.Environment.IsDevelopment())
{
    _ = app.MapOpenApi().CacheOutput(x => x.Expire(TimeSpan.FromDays(365)));
    _ = app.MapScalarApiReference();
}

app.MapDefaultEndpoints();

app.MapControllers();

app.Run();
