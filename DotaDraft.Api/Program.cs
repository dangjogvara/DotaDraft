using DotaDraft.Api.Data;
using DotaDraft.Api.Endpoints;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// Every error response is RFC 9457 ProblemDetails: explicit Problem results, empty status codes
// (e.g. 401, unmatched routes) via UseStatusCodePages, and unhandled exceptions via UseExceptionHandler
builder.Services.AddProblemDetails();

// The connection string lives in user-secrets locally (see .env.example), so fail clearly if it's missing
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection is not configured. Set it with dotnet user-secrets.")));

builder.Services.Configure<OpenDotaOptions>(
    builder.Configuration.GetSection(OpenDotaOptions.SectionName));

builder.Services.AddOpenDotaClient();

builder.Services.AddOptions<MatchupOptions>()
    .Bind(builder.Configuration.GetSection(MatchupOptions.SectionName))
    .Validate(o => o.WindowSize > 0 && o.WindowCount > 0, "Matchups:WindowSize and Matchups:WindowCount must be positive.")
    .ValidateOnStart();

builder.Services.Configure<CounterOptions>(
    builder.Configuration.GetSection(CounterOptions.SectionName));

// The Angular client runs on another origin (its dev server, or wherever it's deployed).
// Origins come from configuration; with none configured, no cross-origin requests are allowed.
const string AngularClientCorsPolicy = "AngularClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(AngularClientCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST")
    .WithHeaders("Content-Type", AdminKeyFilter.HeaderName)
    // Lets the client read where to poll after queueing matchup seeding (202 Accepted)
    .WithExposedHeaders("Location")));

builder.Services.AddScoped<HeroIngestionService>();
builder.Services.AddSingleton<MatchupSeedJob>();
builder.Services.AddHostedService<MatchupSeedWorker>();

var app = builder.Build();

// Configure the HTTP request pipeline.
// In Development the framework's developer exception page handles exceptions, adding their details;
// elsewhere the handler returns a 500 ProblemDetails without them
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler();

app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(AngularClientCorsPolicy);

// Endpoints
app.RegisterHeroEndpoints();
app.RegisterDraftEndpoints();
app.RegisterAdminEndpoints();

app.Run();

// Exposes the entry point to WebApplicationFactory in the test project
public partial class Program;