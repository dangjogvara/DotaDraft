using DotaDraft.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var heroesApi = app.MapGroup("api/heroes");

heroesApi.MapGet("/", GetHeroes);

static async Task<IResult> GetHeroes(AppDbContext db)
{
    var heroes = await db.Heroes
        .OrderBy(h => h.LocalizedName)
        .ToListAsync();
    return TypedResults.Ok(heroes);
}


app.Run();