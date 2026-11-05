using Microsoft.EntityFrameworkCore;
using PremiumSync.Reconciliation.Data;
using PremiumSync.Reconciliation.Endpoints;
using PremiumSync.Reconciliation.Engine;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

Directory.CreateDirectory("data");
builder.Services.AddDbContext<ReconDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Recon") ?? "Data Source=data/recon.db"));

builder.Services.Configure<ReconOptions>(builder.Configuration.GetSection("Recon"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ReconService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ReconDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapReconEndpoints();

app.Run();
