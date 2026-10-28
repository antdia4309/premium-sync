using Microsoft.EntityFrameworkCore;
using PremiumSync.PolicyService.Billing;
using PremiumSync.PolicyService.Data;
using PremiumSync.PolicyService.Domain;
using PremiumSync.PolicyService.Endpoints;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

Directory.CreateDirectory("data");
builder.Services.AddDbContext<PolicyDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Policy") ?? "Data Source=data/policy.db"));

builder.Services.AddHttpClient<BillingClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Billing:BaseUrl"] ?? "http://localhost:5002");
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<PolicyWorkflow>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // Fine for Phase 1. Swap for EF migrations once the model settles.
    scope.ServiceProvider.GetRequiredService<PolicyDbContext>().Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();   // interactive docs at /scalar
    app.MapDevEndpoints();
}

app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapPolicyEndpoints();

app.Run();

