using DotaDraft.Api.Data;
using DotaDraft.Api.Endpoints;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<OpenDotaOptions>(
    builder.Configuration.GetSection(OpenDotaOptions.SectionName));

builder.Services.AddOpenDotaClient();

builder.Services.AddScoped<HeroIngestionService>();
builder.Services.AddSingleton<MatchupSeedJob>();
builder.Services.AddHostedService<MatchupSeedWorker>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Endpoints
app.RegisterHeroEndpoints();
app.RegisterAdminEndpoints();

app.Run();

// Exposes the entry point to WebApplicationFactory in the test project
public partial class Program;