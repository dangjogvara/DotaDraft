using System.Text.Json.Serialization;
using DotaDraft.Api;
using DotaDraft.Api.Data;
using DotaDraft.Api.Endpoints;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Numbers are only read as JSON numbers, not strings ("5"). The API takes no JSON bodies, so this only
// stops the OpenAPI document from describing every number as "integer or string"
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

// Every error response is RFC 9457 ProblemDetails: explicit Problem results, empty status codes
// (e.g. unmatched routes) via UseStatusCodePages, and unhandled exceptions via UseExceptionHandler
builder.Services.AddProblemDetails();

// Validates endpoint parameters with data annotations (e.g. [Range]) and returns a 400 ValidationProblem
builder.Services.AddValidation();

// The connection string lives in user-secrets locally (see .env.example) and in the ConnectionStrings__DefaultConnection
// app setting on App Service, so fail clearly if it's missing
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection is not configured. Set it with dotnet user-secrets locally, " +
            "or as the ConnectionStrings__DefaultConnection environment variable.")));

builder.Services.AddDotaDraftOptions(builder.Configuration);

builder.Services.AddOpenDotaClient();

// The Angular client runs on another origin (its dev server, or wherever it's deployed).
// Origins come from configuration; with none configured, no cross-origin requests are allowed.
const string AngularClientCorsPolicy = "AngularClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(AngularClientCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET")));

// /health reports Unhealthy (503) when the database can't be reached
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

builder.Services.AddScoped<CounterService>();
builder.Services.AddScoped<HeroIngestionService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<SeedingWorker>();

// App Service's front end terminates HTTPS and forwards requests as plain HTTP, saying in X-Forwarded-Proto and
// X-Forwarded-For what the client used. Believe those headers from any proxy: on App Service only the front end can
// reach the app, and the defaults would only trust a proxy on localhost
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// First, so everything after it (the HTTPS redirect, request URLs, logging) sees the client's scheme and IP
app.UseForwardedHeaders();

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

// In production the built Angular app sits in wwwroot (dotnet publish puts it there), and the API hands it out:
// its .js, .css and image files as they are. Locally there's no wwwroot, as ng serve hands out the app instead
var servesAngularApp = Directory.Exists(app.Environment.WebRootPath);
if (servesAngularApp)
{
    app.UseDefaultFiles(); // "/" means /index.html
    app.UseStaticFiles();
}

app.UseCors(AngularClientCorsPolicy);

app.RegisterHeroEndpoints();
app.RegisterDraftEndpoints();

app.MapHealthChecks("/health");

// Every other URL gets the app's page, so links like /?against=1,14 open the app. Not /api/... URLs: an unknown
// one stays a 404 ProblemDetails, and a wrong method a 405. Not file-like URLs either (nonfile): a missing
// main.js is a 404, not the page
if (servesAngularApp)
    app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/).*$)}", "index.html");

app.Run();

// Exposes the entry point to WebApplicationFactory in the test project
public partial class Program;