using Microsoft.EntityFrameworkCore;
using PremiumSync.BillingService.Data;
using PremiumSync.BillingService.Endpoints;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

Directory.CreateDirectory("data");
builder.Services.AddDbContext<BillingDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Billing") ?? "Data Source=data/billing.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapBillingEndpoints();

app.Run();
