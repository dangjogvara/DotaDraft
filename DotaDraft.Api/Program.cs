using System.Text.Json.Serialization;
using DotaDraft.Api.Authentication;
using DotaDraft.Api.Data;
using DotaDraft.Api.Endpoints;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<AdminKeySecuritySchemeTransformer>();
    options.AddOperationTransformer<AdminKeySecuritySchemeTransformer>();
});

// Numbers are only read as JSON numbers, not strings ("5"). The API takes no JSON bodies, so this only
// stops the OpenAPI document from describing every number as "integer or string"
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

// Every error response is RFC 9457 ProblemDetails: explicit Problem results, empty status codes
// (e.g. 401, unmatched routes) via UseStatusCodePages, and unhandled exceptions via UseExceptionHandler
builder.Services.AddProblemDetails();

// Validates endpoint parameters with data annotations (e.g. [Range]) and returns a 400 ValidationProblem
builder.Services.AddValidation();

// The connection string lives in user-secrets locally (see .env.example), so fail clearly if it's missing
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection is not configured. Set it with dotnet user-secrets.")));

// Options are validated with their data annotations at startup, so bad configuration fails fast
// instead of on the first request or seed
builder.Services.AddOptions<OpenDotaOptions>()
    .Bind(builder.Configuration.GetSection(OpenDotaOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOpenDotaClient();

builder.Services.AddOptions<MatchupOptions>()
    .Bind(builder.Configuration.GetSection(MatchupOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<CounterOptions>()
    .Bind(builder.Configuration.GetSection(CounterOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// The Angular client runs on another origin (its dev server, or wherever it's deployed).
// Origins come from configuration; with none configured, no cross-origin requests are allowed.
const string AngularClientCorsPolicy = "AngularClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(AngularClientCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST")
    .WithHeaders("Content-Type", AdminKeyDefaults.HeaderName)
    // Lets the client read where to poll after queueing matchup seeding (202 Accepted)
    .WithExposedHeaders("Location")));

builder.Services.AddAdminKeyAuthentication();

// /health reports Unhealthy (503) when the database can't be reached
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

builder.Services.AddScoped<CounterService>();
builder.Services.AddHeroIngestion();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MatchupSeedJob>();
builder.Services.AddHostedService<MatchupSeedWorker>();

var app = builder.Build();

// In Development the framework's developer exception page handles exceptions, adding their details;
// elsewhere the handler returns a 500 ProblemDetails without them
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

app.UseAuthentication();
app.UseAuthorization();

app.RegisterHeroEndpoints();
app.RegisterDraftEndpoints();
app.RegisterAdminEndpoints();

app.MapHealthChecks("/health");

app.Run();

// Exposes the entry point to WebApplicationFactory in the test project
public partial class Program;