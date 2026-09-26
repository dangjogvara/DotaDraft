using DotaDraft.Api.Data;
using DotaDraft.Api.Endpoints;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<OpenDotaOptions>(
    builder.Configuration.GetSection(OpenDotaOptions.SectionName));

builder.Services.AddHttpClient<OpenDotaService>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<OpenDotaOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.DefaultRequestHeaders.Add("User-Agent", "DotaDraftApp/1.0");
});

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