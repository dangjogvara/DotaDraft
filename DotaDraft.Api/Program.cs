using System.Text.Json.Serialization;
using DotaDraft.Api;
using DotaDraft.Api.Data;
using DotaDraft.Api.Endpoints;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Stops the OpenAPI document describing every number as "integer or string"
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

// Every error is ProblemDetails, including unmatched routes (UseStatusCodePages) and exceptions (UseExceptionHandler)
builder.Services.AddProblemDetails();

builder.Services.AddValidation();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection is not configured. Set it with dotnet user-secrets locally, " +
            "or as the ConnectionStrings__DefaultConnection environment variable.")));

builder.Services.AddDotaDraftOptions(builder.Configuration);

builder.Services.AddOpenDotaClient();

// The Angular dev server runs on another origin. With no origins configured, no cross-origin requests are allowed
const string AngularClientCorsPolicy = "AngularClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(AngularClientCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET")));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

builder.Services.AddScoped<CounterService>();
builder.Services.AddScoped<HeroIngestionService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<SeedingWorker>();

var app = builder.Build();

// In Development the developer exception page shows exception details; elsewhere a 500 ProblemDetails hides them
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler();

app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Browsable API reference at /scalar, built from the OpenAPI document
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseCors(AngularClientCorsPolicy);

app.RegisterHeroEndpoints();
app.RegisterDraftEndpoints();
app.RegisterStatusEndpoints();

app.MapHealthChecks("/health");

app.Run();

// Exposes the entry point to WebApplicationFactory in the test project
public partial class Program;